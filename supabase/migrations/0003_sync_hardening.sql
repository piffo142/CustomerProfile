-- Phase 2 hardening: version handshake + checksum reconciliation.

-- ---------------------------------------------------------------- version gate

-- Single-row config. Raise min_client_version when a schema change makes old
-- clients unsafe; they then see "update required" instead of corrupting data.
create table sync_config (
    id                 int primary key default 1 check (id = 1),
    min_client_version int not null default 1
);
insert into sync_config (id, min_client_version) values (1, 1);

alter table sync_config enable row level security;
create policy sync_config_read on sync_config
  for select using (auth.uid() is not null);

create or replace function assert_client_version(p_client_version int)
returns void
language plpgsql stable as $$
begin
  if p_client_version < (select min_client_version from sync_config where id = 1) then
    -- The literal 'client_too_old' is the wire contract with the app.
    raise exception 'client_too_old: app version % is below the supported minimum', p_client_version
      using errcode = 'P0001';
  end if;
end $$;

-- ---------------------------------------------------------------- versioned RPCs

-- Replace sync_pull with a version-checked variant (plpgsql for the guard).
drop function if exists sync_pull(bigint, int);

create or replace function sync_pull(p_cursor bigint, p_limit int default 500, p_client_version int default 1)
returns jsonb
language plpgsql stable
as $$
begin
  perform assert_client_version(p_client_version);

  return jsonb_build_object(
    'clients',  coalesce((select jsonb_agg(to_jsonb(c)) from (
                   select * from client where sync_seq > p_cursor
                   order by sync_seq limit p_limit) c), '[]'::jsonb),
    'services', coalesce((select jsonb_agg(to_jsonb(s)) from (
                   select * from service_record where sync_seq > p_cursor
                   order by sync_seq limit p_limit) s), '[]'::jsonb),
    'notes',    coalesce((select jsonb_agg(to_jsonb(n)) from (
                   select * from client_note where sync_seq > p_cursor
                   order by sync_seq limit p_limit) n), '[]'::jsonb),
    'consents', coalesce((select jsonb_agg(to_jsonb(x)) from (
                   select * from client_consent where sync_seq > p_cursor
                   order by sync_seq limit p_limit) x), '[]'::jsonb),
    'redactions', coalesce((select jsonb_agg(to_jsonb(r)) from (
                   select * from redaction_log where sync_seq > p_cursor
                   order by sync_seq limit p_limit) r), '[]'::jsonb)
  );
end $$;

-- Version-checked wrapper around the existing push body.
drop function if exists sync_push(jsonb);
-- Recreate sync_push from 0002 with the added guard: run 0002's definition
-- first, then this file replaces it. To keep this migration self-contained we
-- rename the original logic:
--   (the CREATE below assumes 0002 has been applied)

create or replace function sync_push(p_ops jsonb, p_client_version int default 1)
returns jsonb
language plpgsql
security invoker
as $$
declare
  v_op       jsonb;
  v_entity   text;
  v_rejected uuid[] := '{}';
begin
  perform assert_client_version(p_client_version);

  for v_op in select * from jsonb_array_elements(p_ops) loop
    if exists (select 1 from sync_applied_op where op_id = (v_op->>'op_id')::uuid) then
      continue;
    end if;

    v_entity := v_op->>'entity';

    begin
      if v_op->>'operation' = 'delete' then
        if v_entity = 'client' then
          insert into redaction_log(entity, entity_id, salon_id, redacted_at)
          select 'client', c.id, c.salon_id, now() from client c
          where c.id = (v_op->>'entity_id')::uuid
          on conflict do nothing;

          delete from client where id = (v_op->>'entity_id')::uuid;
        else
          v_rejected := v_rejected || (v_op->>'op_id')::uuid;
        end if;

      elsif v_entity = 'client' then
        insert into client as c (id, salon_id, last_name, first_name, address,
                                 phone, phone_raw, email, acquisition_source,
                                 acquisition_detail, date_of_birth, medical_flag,
                                 gp_details, patch_test_on, patch_test_result,
                                 card_photo_path, updated_at, updated_by_device,
                                 deleted_at)
        select r.id, r.salon_id, r.last_name, r.first_name, r.address,
               r.phone, r.phone_raw, r.email, r.acquisition_source,
               r.acquisition_detail, r.date_of_birth, r.medical_flag,
               r.gp_details, r.patch_test_on, r.patch_test_result,
               r.card_photo_path, r.updated_at, r.updated_by_device, r.deleted_at
        from jsonb_populate_record(null::client, v_op->'payload') r
        on conflict (id) do update
          set last_name          = excluded.last_name,
              first_name         = excluded.first_name,
              address            = excluded.address,
              phone              = excluded.phone,
              phone_raw          = excluded.phone_raw,
              email              = excluded.email,
              acquisition_source = excluded.acquisition_source,
              acquisition_detail = excluded.acquisition_detail,
              date_of_birth      = excluded.date_of_birth,
              medical_flag       = excluded.medical_flag,
              gp_details         = excluded.gp_details,
              patch_test_on      = excluded.patch_test_on,
              patch_test_result  = excluded.patch_test_result,
              card_photo_path    = excluded.card_photo_path,
              updated_at         = excluded.updated_at,
              updated_by_device  = excluded.updated_by_device,
              deleted_at         = excluded.deleted_at
          where excluded.updated_at > c.updated_at
             or (excluded.updated_at = c.updated_at
                 and excluded.updated_by_device > c.updated_by_device);

      elsif v_entity = 'client_note' then
        insert into client_note
        select r.* from jsonb_populate_record(null::client_note, v_op->'payload') r
        on conflict (id) do nothing;

      elsif v_entity = 'service_record' then
        insert into service_record as s
        select r.* from jsonb_populate_record(null::service_record, v_op->'payload') r
        on conflict (id) do update
          set performed_on        = excluded.performed_on,
              service_description = excluded.service_description,
              price               = excluded.price,
              currency_code       = excluded.currency_code,
              service_catalog_id  = excluded.service_catalog_id,
              photo_path          = excluded.photo_path,
              updated_at          = excluded.updated_at,
              updated_by_device   = excluded.updated_by_device,
              deleted_at          = coalesce(s.deleted_at, excluded.deleted_at)
          where s.deleted_at is null
            and (excluded.deleted_at is not null
                 or excluded.updated_at > s.updated_at
                 or (excluded.updated_at = s.updated_at
                     and excluded.updated_by_device > s.updated_by_device));

      elsif v_entity = 'client_consent' then
        insert into client_consent as cc
        select r.* from jsonb_populate_record(null::client_consent, v_op->'payload') r
        on conflict (id) do update
          set granted_at          = case when excluded.updated_at > cc.updated_at then excluded.granted_at else cc.granted_at end,
              captured_by_user_id = case when excluded.updated_at > cc.updated_at then excluded.captured_by_user_id else cc.captured_by_user_id end,
              signature_blob_ref  = case when excluded.updated_at > cc.updated_at then excluded.signature_blob_ref else cc.signature_blob_ref end,
              updated_at          = greatest(excluded.updated_at, cc.updated_at),
              updated_by_device   = case when excluded.updated_at > cc.updated_at then excluded.updated_by_device else cc.updated_by_device end,
              withdrawn_at        = least(cc.withdrawn_at, excluded.withdrawn_at);

      else
        v_rejected := v_rejected || (v_op->>'op_id')::uuid;
      end if;

      insert into sync_applied_op(op_id, applied_at)
      values ((v_op->>'op_id')::uuid, now())
      on conflict do nothing;

    exception
      when foreign_key_violation or check_violation or not_null_violation
           or invalid_text_representation then
        v_rejected := v_rejected || (v_op->>'op_id')::uuid;
    end;
  end loop;

  return jsonb_build_object(
    'cursor', (select last_value from sync_seq_gen),
    'rejected_op_ids', to_jsonb(coalesce(v_rejected, '{}')));
end $$;

-- ---------------------------------------------------------------- checksum

-- Per-entity count + md5 of canonical ids in uuid order, for the client's
-- periodic full reconciliation. security invoker: RLS scopes it to the
-- caller's salon, matching what that device should hold locally.
create or replace function sync_checksum()
returns jsonb
language sql stable
as $$
  select jsonb_build_object(
    'clients', (select jsonb_build_object(
        'count', count(*),
        'max_seq', coalesce(max(sync_seq), 0),
        'ids_hash', coalesce(md5(string_agg(id::text, ',' order by id)), ''))
      from client),
    'services', (select jsonb_build_object(
        'count', count(*),
        'max_seq', coalesce(max(sync_seq), 0),
        'ids_hash', coalesce(md5(string_agg(id::text, ',' order by id)), ''))
      from service_record),
    'notes', (select jsonb_build_object(
        'count', count(*),
        'max_seq', coalesce(max(sync_seq), 0),
        'ids_hash', coalesce(md5(string_agg(id::text, ',' order by id)), ''))
      from client_note),
    'consents', (select jsonb_build_object(
        'count', count(*),
        'max_seq', coalesce(max(sync_seq), 0),
        'ids_hash', coalesce(md5(string_agg(id::text, ',' order by id)), ''))
      from client_consent)
  );
$$;
