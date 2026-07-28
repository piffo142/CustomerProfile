-- Phase 3: service catalogue, attachments bucket, retention purge.

-- ---------------------------------------------------------------- service catalogue

create table service_catalog (
    id                uuid primary key,
    salon_id          uuid not null references salon(id),
    name              text not null,
    default_price     numeric(10,2) not null default 0,
    currency_code     text not null default 'GBP',
    updated_at        timestamptz not null default now(),
    updated_by_device uuid not null,
    deleted_at        timestamptz,
    sync_seq          bigint not null default nextval('sync_seq_gen')
);

create trigger service_catalog_bump before insert or update on service_catalog
  for each row execute function bump_sync();

create index ix_service_catalog_salon_seq on service_catalog(salon_id, sync_seq);

alter table service_catalog enable row level security;
create policy service_catalog_tenant on service_catalog
  for all
  using      (salon_id in (select auth_salon_ids()))
  with check (salon_id in (select auth_salon_ids()));

-- FK from service_record now that the catalogue exists.
alter table service_record
  add constraint fk_service_record_catalog
  foreign key (service_catalog_id) references service_catalog(id)
  on delete set null
  not valid;  -- pre-existing rows may carry ids that never synced; validate later

-- ---------------------------------------------------------------- RPCs: add catalogue

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
    'catalog',  coalesce((select jsonb_agg(to_jsonb(k)) from (
                   select * from service_catalog where sync_seq > p_cursor
                   order by sync_seq limit p_limit) k), '[]'::jsonb),
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

-- Extend sync_push's dispatch with the service_catalog branch (LWW + tiebreak,
-- same policy as client). Full function replaced for atomicity of definition.
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

      elsif v_entity = 'service_catalog' then
        insert into service_catalog as k
        select r.* from jsonb_populate_record(null::service_catalog, v_op->'payload') r
        on conflict (id) do update
          set name              = excluded.name,
              default_price     = excluded.default_price,
              currency_code     = excluded.currency_code,
              updated_at        = excluded.updated_at,
              updated_by_device = excluded.updated_by_device,
              deleted_at        = excluded.deleted_at
          where excluded.updated_at > k.updated_at
             or (excluded.updated_at = k.updated_at
                 and excluded.updated_by_device > k.updated_by_device);

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

create or replace function sync_checksum()
returns jsonb
language sql stable
as $$
  select jsonb_build_object(
    'clients', (select jsonb_build_object(
        'count', count(*), 'max_seq', coalesce(max(sync_seq), 0),
        'ids_hash', coalesce(md5(string_agg(id::text, ',' order by id)), ''))
      from client),
    'catalog', (select jsonb_build_object(
        'count', count(*), 'max_seq', coalesce(max(sync_seq), 0),
        'ids_hash', coalesce(md5(string_agg(id::text, ',' order by id)), ''))
      from service_catalog),
    'services', (select jsonb_build_object(
        'count', count(*), 'max_seq', coalesce(max(sync_seq), 0),
        'ids_hash', coalesce(md5(string_agg(id::text, ',' order by id)), ''))
      from service_record),
    'notes', (select jsonb_build_object(
        'count', count(*), 'max_seq', coalesce(max(sync_seq), 0),
        'ids_hash', coalesce(md5(string_agg(id::text, ',' order by id)), ''))
      from client_note),
    'consents', (select jsonb_build_object(
        'count', count(*), 'max_seq', coalesce(max(sync_seq), 0),
        'ids_hash', coalesce(md5(string_agg(id::text, ',' order by id)), ''))
      from client_consent)
  );
$$;

-- Realtime: postgres_changes subscriptions require the publication.
alter publication supabase_realtime add table client, service_catalog, service_record, client_note, client_consent;

-- ---------------------------------------------------------------- attachments bucket

insert into storage.buckets (id, name, public)
values ('attachments', 'attachments', false)
on conflict (id) do nothing;

-- Object keys are '<salon_id>/<file>'; tenancy rides on the path prefix.
create policy attachments_read on storage.objects
  for select using (
    bucket_id = 'attachments'
    and (storage.foldername(name))[1]::uuid in (select auth_salon_ids()));

create policy attachments_write on storage.objects
  for insert with check (
    bucket_id = 'attachments'
    and (storage.foldername(name))[1]::uuid in (select auth_salon_ids()));

create policy attachments_update on storage.objects
  for update using (
    bucket_id = 'attachments'
    and (storage.foldername(name))[1]::uuid in (select auth_salon_ids()));

-- ---------------------------------------------------------------- retention purge

-- Clients with no activity inside the salon's retention window are erased,
-- leaving only redaction_log rows so devices purge too. Schedule daily:
--   select cron.schedule('purge-expired-clients', '30 3 * * *',
--                        $$select purge_expired_clients()$$);
create or replace function purge_expired_clients()
returns int
language plpgsql
security definer
set search_path = public
as $$
declare
  v_count int;
begin
  with expired as (
    select c.id, c.salon_id
    from client c
    join salon s on s.id = c.salon_id
    where coalesce(
            (select max(r.performed_on) from service_record r where r.client_id = c.id),
            c.updated_at::date)
          < current_date - make_interval(years => s.retention_years)
  ),
  redacted as (
    insert into redaction_log(entity, entity_id, salon_id)
    select 'client', id, salon_id from expired
    on conflict do nothing
    returning entity_id
  )
  delete from client where id in (select id from expired);

  get diagnostics v_count = row_count;
  return v_count;
end $$;
