-- Client Card — server schema (Supabase / Postgres)
-- Project region: eu-west-2 (London) for UK salons — special category data stays in-region.

create extension if not exists citext;

-- ---------------------------------------------------------------- tenancy

create table salon (
    id              uuid primary key,
    name            text not null,
    -- Retention policy drives the scheduled purge job.
    retention_years int  not null default 3,
    created_at      timestamptz not null default now()
);

create table salon_member (
    salon_id   uuid not null references salon(id) on delete cascade,
    user_id    uuid not null,
    role       text not null default 'therapist',
    created_at timestamptz not null default now(),
    primary key (salon_id, user_id)
);

-- ---------------------------------------------------------------- sync plumbing

-- Single database-wide sequence shared by all synced tables, so one cursor
-- value covers a whole multi-table pull bundle. Per-table cursors mean
-- per-table consistency gaps.
create sequence sync_seq_gen;

create or replace function bump_sync() returns trigger
language plpgsql as $$
begin
  -- Deliberately do NOT overwrite a client-supplied updated_at: LWW compares
  -- the device's edit time, and stamping arrival time here would let a late
  -- push from a stale device beat a genuinely newer edit. Server-side writes
  -- that omit it still get a value.
  new.updated_at := coalesce(new.updated_at, now());
  new.sync_seq  := nextval('sync_seq_gen');
  return new;
end $$;

-- Idempotency ledger: op ids already applied. Needs a retention purge — 30
-- days is ample, since no legitimate client retries an op that old.
create table sync_applied_op (
    op_id      uuid primary key,
    applied_at timestamptz not null default now()
);

-- Right-to-erasure record: only the id and timestamp, never the payload, so
-- peers can purge without retaining what was erased.
create table redaction_log (
    entity      text not null,
    entity_id   uuid not null,
    salon_id    uuid not null,
    redacted_at timestamptz not null default now(),
    sync_seq    bigint not null default nextval('sync_seq_gen'),
    primary key (entity, entity_id)
);

-- ---------------------------------------------------------------- domain tables

create table client (
    id                  uuid primary key,          -- client-generated UUIDv7, never server-assigned
    salon_id            uuid not null references salon(id),
    last_name           text not null,
    first_name          text not null,
    address             text not null default '',
    phone               text not null default '',  -- E.164 normalised
    phone_raw           text not null default '',  -- shadow of the raw input
    email               citext,
    acquisition_source  text not null default 'unknown',
    acquisition_detail  text,
    date_of_birth       date,
    medical_flag        boolean not null default false,
    gp_details          text,
    patch_test_on       date,
    patch_test_result   text not null default 'not_tested',
    card_photo_path     text,
    updated_at          timestamptz not null default now(),  -- conflict resolution only, never a cursor
    updated_by_device   uuid not null,
    deleted_at          timestamptz,                          -- tombstone
    sync_seq            bigint not null default nextval('sync_seq_gen')
);

create table client_note (
    id                uuid primary key,
    salon_id          uuid not null references salon(id),
    client_id         uuid not null references client(id) on delete cascade,
    created_at        timestamptz not null,
    author_user_id    uuid,
    body              text not null,
    updated_at        timestamptz not null default now(),
    updated_by_device uuid not null,
    deleted_at        timestamptz,
    sync_seq          bigint not null default nextval('sync_seq_gen')
);

create table service_record (
    id                  uuid primary key,
    salon_id            uuid not null references salon(id),
    client_id           uuid not null references client(id) on delete cascade,
    performed_on        date not null,             -- the card records a day, not a datetime
    service_description text not null,
    price               numeric(10,2) not null,
    currency_code       text not null default 'GBP',
    service_catalog_id  uuid,                      -- future catalogue FK, present from v1
    photo_path          text,
    updated_at          timestamptz not null default now(),
    updated_by_device   uuid not null,
    deleted_at          timestamptz,
    sync_seq            bigint not null default nextval('sync_seq_gen')
);

create table client_consent (
    id                 uuid primary key,
    salon_id           uuid not null references salon(id),
    client_id          uuid not null references client(id) on delete cascade,
    purpose            text not null,
    granted_at         timestamptz not null,
    withdrawn_at       timestamptz,
    captured_by_user_id uuid,
    signature_blob_ref text,
    updated_at         timestamptz not null default now(),
    updated_by_device  uuid not null,
    deleted_at         timestamptz,
    sync_seq           bigint not null default nextval('sync_seq_gen')
);

-- change-tracking triggers
create trigger client_bump before insert or update on client
  for each row execute function bump_sync();
create trigger client_note_bump before insert or update on client_note
  for each row execute function bump_sync();
create trigger service_record_bump before insert or update on service_record
  for each row execute function bump_sync();
create trigger client_consent_bump before insert or update on client_consent
  for each row execute function bump_sync();

-- (salon_id, sync_seq) serves both the RLS predicate and the pull ordering.
create index ix_client_salon_seq         on client(salon_id, sync_seq);
create index ix_client_note_salon_seq    on client_note(salon_id, sync_seq);
create index ix_service_record_salon_seq on service_record(salon_id, sync_seq);
create index ix_client_consent_salon_seq on client_consent(salon_id, sync_seq);
create index ix_redaction_log_salon_seq  on redaction_log(salon_id, sync_seq);
create index ix_client_note_client       on client_note(client_id, created_at);
create index ix_service_record_client    on service_record(client_id, performed_on);

-- ---------------------------------------------------------------- RLS

create or replace function auth_salon_ids()
returns setof uuid
language sql
stable                       -- required for InitPlan caching; volatile loses it
security definer
set search_path = public
as $$
  select salon_id from salon_member where user_id = auth.uid()
$$;

alter table salon          enable row level security;
alter table salon_member   enable row level security;
alter table client         enable row level security;
alter table client_note    enable row level security;
alter table service_record enable row level security;
alter table client_consent enable row level security;
alter table redaction_log  enable row level security;
alter table sync_applied_op enable row level security;

create policy salon_tenant on salon
  for select using (id in (select auth_salon_ids()));

create policy salon_member_self on salon_member
  for select using (user_id = auth.uid());

-- `in (select ...)` lets the planner hoist the call into an InitPlan evaluated
-- once per query. `with check` is separate from `using`: omit it and a
-- compromised client can write rows into another salon's tenant.
create policy client_tenant on client
  for all
  using      (salon_id in (select auth_salon_ids()))
  with check (salon_id in (select auth_salon_ids()));

create policy client_note_tenant on client_note
  for all
  using      (salon_id in (select auth_salon_ids()))
  with check (salon_id in (select auth_salon_ids()));

create policy service_record_tenant on service_record
  for all
  using      (salon_id in (select auth_salon_ids()))
  with check (salon_id in (select auth_salon_ids()));

create policy client_consent_tenant on client_consent
  for all
  using      (salon_id in (select auth_salon_ids()))
  with check (salon_id in (select auth_salon_ids()));

create policy redaction_log_tenant on redaction_log
  for all
  using      (salon_id in (select auth_salon_ids()))
  with check (salon_id in (select auth_salon_ids()));

-- applied-op ledger is written by sync_push (invoker rights); readable rows
-- carry no tenant data, but restrict anyway to authenticated users.
create policy sync_applied_op_rw on sync_applied_op
  for all using (auth.uid() is not null) with check (auth.uid() is not null);
