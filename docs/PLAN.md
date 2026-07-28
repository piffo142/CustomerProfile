# Client Profile — .NET MAUI + SQLite + Supabase Sync

## 1. Domain model

The card carries three distinct concerns that the paper conflates. Split them.

**Client (header)** — mutable, low write frequency, high conflict risk on `Notes`.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` (v7) | client-generated, never server-assigned |
| `SalonId` | `Guid` | tenant discriminator, on every synced row |
| `LastName`, `FirstName` | `string` | card has `Name(Last,First)` — store separately, display joined |
| `Address` | `string` | single multi-line field; don't over-normalise until you need geo |
| `Phone` | `string` | store E.164 normalised, keep raw input in a shadow column |
| `Email` | `string?` | `citext` server-side |
| `AcquisitionSource` | `enum` | `Referral` / `Location` / `Other` |
| `AcquisitionDetail` | `string?` | referrer name, or the free text from "Other" |

The "How did you hear about us" block is a source + detail pair, not three independent fields. The `Location` checkbox has no companion line on the card, so it degenerates to source-only.

**ClientNote** — append-only child rows, *not* a mutable text column.

This is the single most important deviation from the paper. Two therapists editing notes on separate devices under last-writer-wins silently destroys clinical history. As immutable rows (`Id`, `ClientId`, `CreatedAt`, `AuthorUserId`, `Body`) the merge is a union and conflict resolution disappears entirely. Render as a reverse-chronological feed.

**ServiceRecord** — the Date/Service/Price grid.

`Id`, `ClientId`, `PerformedOn` (`date`, not `datetime` — the card records a day), `ServiceDescription`, `Price` (`decimal(10,2)`), `CurrencyCode`. Append-mostly, rarely edited, so conflicts are near-zero. Add `ServiceCatalogId` as a nullable FK now even if the catalogue doesn't exist yet — retrofitting it after 50k free-text rows means a fuzzy-match migration.

### What the card omits and the app must not

Free-text notes on a beauty therapy client will contain allergies, medications, skin conditions and contraindications. That is UK GDPR Article 9 special category data the moment it lands. Consequences that shape the schema, not just the policy doc:

- `ClientConsent` table: `Purpose`, `GrantedAt`, `WithdrawnAt`, `CapturedByUserId`, signature blob reference.
- Right to erasure conflicts with tombstone-based sync. Hard delete plus a `redaction_log` row carrying only the id and timestamp, so peers can purge without retaining the payload.
- Supabase project in `eu-west-2` (London) for UK salons.
- Retention policy field on the salon record, with a scheduled purge job.

Also missing and worth adding at v1: patch test date/result, date of birth, GP/medical flag, and a photo attachment per service record.

---

## 2. Build-vs-buy: read this before writing sync code

Bidirectional sync is the highest-maintenance subsystem in an app like this. Before committing to a hand-rolled engine, evaluate PowerSync — it now has first-party MAUI support. <cite index="2-1">The PowerSync.MAUI package extends their .NET SDK to iOS, Android and Windows and loads the right platform-specific extension at runtime</cite>, and <cite index="9-1">the dotnet monorepo publishes `PowerSync.Common` and `PowerSync.Maui` to NuGet with a MAUI to-do demo running on iOS, Android and Windows</cite>.

The Supabase integration is non-invasive: <cite index="5-1">it connects without schema changes or write permissions, streams into the SDK's local SQLite, and queues local writes for upload as soon as connectivity returns, with conflict resolution applied in your own backend API</cite>. <cite index="3-1">Client schema is applied as SQLite views over schemaless synced data, which removes client-side migration handling.</cite>

Caveats: the .NET SDK is young — <cite index="3-1">alpha as of the initial announcement</cite>, listed as beta more recently. Taking it means accepting a third-party dependency at the centre of your data layer, plus PowerSync service hosting cost, and losing direct control over the local schema (views, not tables — which affects EF Core usage).

**Recommendation:** spike PowerSync.MAUI for two days at Phase 2. If it holds up, it saves months. If not, the design below is the fallback and it's fully specified. Either way, Phase 0 and 1 are identical — do not let this decision block the start.

The remainder of this document specifies the hand-rolled path.

---

## 3. Solution structure

```
SIG.ClientCard.Core        // entities, enums, interfaces. No package refs.
SIG.ClientCard.Data        // EF Core, DbContext, migrations, repositories
SIG.ClientCard.Sync        // outbox, cursor, push/pull, conflict policy
SIG.ClientCard.App         // MAUI: Shell, views, viewmodels
SIG.ClientCard.Tests       // xUnit, sync engine under a fake transport
```

Sync lives in its own assembly so it can be unit-tested against an in-memory SQLite connection with zero MAUI dependency. Nearly all the defects will be here; make them cheap to reproduce.

### Local persistence

EF Core 9 + `Microsoft.Data.Sqlite`. Two configuration items that are not optional on mobile:

**Compiled model.** EF Core's runtime model build costs 300–600ms on mid-range Android at first query. Run `dotnet ef dbcontext optimize` and wire it up:

```csharp
optionsBuilder.UseModel(ClientCardContextModel.Instance);
```

Regenerating this after every migration is a build-step obligation. Wire it into CI or it will silently drift.

**iOS trimming.** EF Core does not support NativeAOT and relies on reflection paths the iOS linker will strip. Set the interpreter for Release iOS builds:

```xml
<PropertyGroup Condition="'$(TargetFramework)'=='net9.0-ios' and '$(Configuration)'=='Release'">
  <UseInterpreter>true</UseInterpreter>
  <MtouchInterpreter>-all</MtouchInterpreter>
</PropertyGroup>
```

This costs startup time. If it proves unacceptable, `sqlite-net-pcl` is the trim-friendly alternative — but you lose migrations, which matters more than it appears once sync is live and schema versions differ across devices.

**Encryption at rest.** Client health data on a device that gets left in a salon. Use `SQLitePCLRaw.bundle_e_sqlcipher`, key generated once and held in `SecureStorage`.

Android edge case: `SecureStorage` reads fail after a device-to-device restore because the Keystore key doesn't transfer. Wrap every read in a try/catch that clears storage and forces re-auth, or the app bricks itself on new handsets.

**Connection setup:**

```csharp
// WAL is safe on app-private storage on both platforms.
// It is NOT safe on external/shared storage on Android.
await conn.ExecuteAsync("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
```

`foreign_keys` is off by default in SQLite and EF Core does not enable it for you.

---

## 4. Sync design

Offline-first. The local SQLite database is the source of truth for the UI; the network is a background reconciliation process. No screen ever awaits a network call.

### 4.1 Identity

`Guid.CreateVersion7()` — .NET 9 built-in. Time-ordered, so B-tree index locality is preserved on both SQLite and Postgres, unlike v4. Client-generated means no id round-trip and no temp-id remapping.

Store as `uuid` in Postgres. In SQLite store as `TEXT` in canonical lowercase form — not `BLOB`, because .NET's `Guid.ToByteArray()` uses mixed-endian layout and will not match Postgres byte ordering if you ever diff outside the app.

### 4.2 Change tracking columns

Every synced table carries:

| Column | Purpose |
|---|---|
| `updated_at timestamptz` | conflict resolution only, never a cursor |
| `updated_by_device uuid` | echo suppression, audit |
| `deleted_at timestamptz` | tombstone; without it, deletes never propagate |
| `sync_seq bigint` | server-assigned monotonic cursor |

`sync_seq` comes from a **single database-wide sequence shared by all synced tables**, so one cursor value covers a whole multi-table pull bundle. Per-table cursors mean per-table consistency gaps.

```sql
create sequence sync_seq_gen;

create or replace function bump_sync() returns trigger
language plpgsql as $$
begin
  new.updated_at := now();
  new.sync_seq  := nextval('sync_seq_gen');
  return new;
end $$;

create trigger client_bump before insert or update on client
  for each row execute function bump_sync();
```

### 4.3 The cursor trap

Sequence values are allocated at statement time, not commit time. A long transaction can commit `sync_seq = 500` *after* a client has already pulled up to 520. That row is then invisible forever. Timestamp cursors have the identical defect via clock skew and commit ordering.

Two ways out:

1. **Pragmatic:** pull from `cursor - OverlapWindow` (e.g. 1000 sequence values) and rely on idempotent upserts to make the re-delivery harmless. Cheap, correct in practice, costs a little bandwidth.
2. **Rigorous:** gate on transaction visibility using `pg_snapshot_xmin(pg_current_snapshot())`, returning only rows from transactions known to have committed. Correct by construction, more machinery.

Start with (1). It is the reason every write path must be an idempotent upsert rather than an insert.

### 4.4 Device outbox

```sql
CREATE TABLE sync_outbox (
    op_id        TEXT PRIMARY KEY,   -- UUIDv7, the idempotency key
    entity       TEXT NOT NULL,
    entity_id    TEXT NOT NULL,
    operation    TEXT NOT NULL,      -- upsert | delete
    payload      TEXT NOT NULL,      -- JSON snapshot of the row
    created_at   TEXT NOT NULL,
    attempts     INTEGER NOT NULL DEFAULT 0,
    last_error   TEXT
);
CREATE INDEX ix_outbox_created ON sync_outbox(created_at);
```

Writes go into the domain table and the outbox **in the same SQLite transaction**. Anything less and a crash between the two loses the change permanently.

Push order matters: `client` before `service_record` before `client_note`, or FK constraints reject the batch. Encode this as a static ordinal on the entity type rather than relying on insertion order.

Retry: exponential backoff with jitter, cap at ~5 minutes. After N failures, park the row into `sync_dead_letter` and surface a badge in the UI. Never retry forever against a 4xx — distinguish transport failure (retry) from validation rejection (park and alert).

### 4.5 Push RPC

Use a Postgres function via PostgREST rather than an Edge Function. It keeps the whole batch in one transaction, has no cold start, and puts conflict logic next to the data.

```sql
create or replace function sync_push(p_ops jsonb)
returns jsonb
language plpgsql
security invoker  -- RLS still applies. Deliberate.
as $$
declare
  v_op      jsonb;
  v_result  jsonb := '[]'::jsonb;
begin
  for v_op in select * from jsonb_array_elements(p_ops) loop
    if exists (select 1 from sync_applied_op where op_id = (v_op->>'op_id')::uuid) then
      continue;  -- idempotent replay
    end if;

    -- dispatch per entity; LWW guard shown for client
    if v_op->>'entity' = 'client' then
      insert into client as c (id, salon_id, last_name, first_name, address,
                               phone, email, acquisition_source, acquisition_detail,
                               deleted_at, updated_at, updated_by_device)
      select * from jsonb_populate_record(null::client, v_op->'payload')
      on conflict (id) do update
        set last_name = excluded.last_name,
            /* ... */
            deleted_at = excluded.deleted_at
        where excluded.updated_at > c.updated_at
           or (excluded.updated_at = c.updated_at
               and excluded.updated_by_device > c.updated_by_device);
    end if;

    insert into sync_applied_op(op_id, applied_at) values ((v_op->>'op_id')::uuid, now());
  end loop;

  return jsonb_build_object('cursor', (select last_value from sync_seq_gen));
end $$;
```

The device-id tiebreak on equal timestamps makes the resolution deterministic across peers. Without it two devices can settle on different winners and oscillate.

`sync_applied_op` needs a retention purge — 30 days is ample, since no legitimate client retries an op that old.

### 4.6 Pull RPC

```sql
create or replace function sync_pull(p_cursor bigint, p_limit int default 500)
returns jsonb
language sql stable
as $$
  select jsonb_build_object(
    'clients',  coalesce((select jsonb_agg(to_jsonb(c)) from (
                   select * from client where sync_seq > p_cursor
                   order by sync_seq limit p_limit) c), '[]'::jsonb),
    'services', coalesce((select jsonb_agg(to_jsonb(s)) from (
                   select * from service_record where sync_seq > p_cursor
                   order by sync_seq limit p_limit) s), '[]'::jsonb),
    'notes',    coalesce((select jsonb_agg(to_jsonb(n)) from (
                   select * from client_note where sync_seq > p_cursor
                   order by sync_seq limit p_limit) n), '[]'::jsonb)
  );
$$;
```

The client advances its cursor to the **minimum** `sync_seq` not yet consumed across all three arrays, and re-pulls until a page comes back short. Advancing to the max on a partial page skips rows.

Apply the whole bundle inside one SQLite transaction, then commit the cursor. Cursor commit and data apply must be atomic or a crash mid-apply loses data silently.

### 4.7 Conflict policy per entity

| Entity | Policy | Rationale |
|---|---|---|
| `client` | Row-level LWW + device tiebreak | Demographic edits, genuinely rare |
| `client_note` | Union (append-only) | No conflict possible by construction |
| `service_record` | LWW, but delete-wins over update | A voided treatment must not resurrect |
| `client_consent` | Withdrawal always wins | Legal requirement, never LWW |

Delete-wins and withdrawal-wins are asymmetric rules that a generic LWW engine will get wrong. They must be explicit branches in `sync_push`.

### 4.8 Triggering

- On app resume, if `Connectivity.NetworkAccess == Internet`.
- On outbox insert, debounced 2s.
- Every 15 minutes while foregrounded.
- On connectivity restored.

Skip background sync entirely for v1. iOS BGTaskScheduler and Android WorkManager both need platform-specific plumbing for marginal benefit when the salon device is in near-constant foreground use.

Phase 3: Supabase Realtime on the tenant's rows to trigger an immediate pull rather than waiting for the poll. Realtime is the *signal*; the RPC pull remains the *transport*. Do not try to apply Realtime payloads directly — you lose the ordering guarantees the cursor gives you.

---

## 5. Multi-tenancy and RLS

```sql
create or replace function auth_salon_ids()
returns setof uuid
language sql
stable
security definer
set search_path = public
as $$
  select salon_id from salon_member where user_id = auth.uid()
$$;

alter table client enable row level security;

create policy client_tenant on client
  for all
  using      (salon_id in (select auth_salon_ids()))
  with check (salon_id in (select auth_salon_ids()));
```

Three details that are easy to get wrong:

- `in (select ...)` lets the planner hoist the call into an InitPlan evaluated once per query. Writing `salon_id = any(auth_salon_ids())` inline can produce a per-row call and a table scan on every read.
- `stable` is required for that caching. Mark it `volatile` by accident and you lose it.
- `with check` is separate from `using`. Omit it and a compromised client can write rows into another salon's tenant even though it cannot read them.

Index `(salon_id, sync_seq)` on every synced table — it serves both the RLS predicate and the pull ordering.

### Auth

Supabase GoTrue. Refresh token in `SecureStorage`, access token in memory only. Handle the 401→refresh→retry cycle inside a `DelegatingHandler` so no call site deals with it.

The `salon_id` claim should come from a custom access token hook rather than a table lookup on every RLS evaluation — but only after the membership model stabilises, since claims are cached until token refresh and stale claims are a nasty class of bug.

---

## 6. UI

Shell, tab-based:

- **Clients** — `CollectionView` with `SearchBar`. Server-side filtering is irrelevant; query SQLite. Use `RemainingItemsThreshold` for incremental load, and set `ItemsUpdatingScrollMode="KeepItemsInView"` or the list jumps on sync-driven updates.
- **Client detail** — header card, then the service history grid, then the notes feed. One scroll, no tabs within the page.
- **Add service** — modal, date defaults to today, service picker with free-text fallback, numeric keypad for price.
- **Sync status** — pending-op count and last-sync time in the flyout footer. Users tolerate delay; they do not tolerate uncertainty.

CommunityToolkit.Mvvm for source-generated `ObservableProperty` and `RelayCommand`. No third-party MVVM framework.

Card migration path: `MediaPicker` to photograph existing paper cards, store the blob in Supabase Storage, attach the path to the client record. Lets a salon go live in an afternoon without back-keying years of history.

---

## 7. Phasing

**Phase 0 — local only (2–3 weeks).** Full CRUD, SQLite, all UI. No auth, no network. Ship it to one salon and get it used. Every schema mistake found here is free; found in Phase 2 it costs a migration across N devices.

**Phase 1 — auth and tenancy (1 week).** Supabase auth, `salon_id` stamped on rows, RLS live. Still no sync. Validates the security model in isolation.

**Phase 2 — sync (3–4 weeks, or 1 week if PowerSync fits).** Outbox, cursor, push/pull RPCs, conflict rules, dead-letter UI.

**Phase 3 — hardening.** Realtime signalling, attachments, consent capture with signature, service catalogue, reporting.

Do not merge phases. The temptation to build sync alongside the schema produces a design where neither is clean.

---

## 8. Risk register

| Risk | Impact | Mitigation |
|---|---|---|
| Hand-rolled sync becomes the maintenance centre of gravity | High | Spike PowerSync at Phase 2 before committing |
| Cursor gap loses rows silently | High | Overlap window + idempotent upserts; assert with a periodic full-hash reconciliation |
| `Notes` as mutable column destroys history | High | Append-only note rows from day one — retrofitting is a data-loss migration |
| EF Core startup cost on Android | Medium | Compiled model, wired into CI |
| iOS Release build strips EF Core reflection paths | Medium | Interpreter enabled; test Release on device early, not at submission |
| Special category data with no consent record | High | `ClientConsent` in v1 schema, even if UI lands in Phase 3 |
| `SecureStorage` failure after device restore | Medium | Catch, clear, force re-auth |
| Schema drift across devices on partial rollout | Medium | Version handshake in the pull RPC; server refuses clients below minimum version |

---

## 9. Open question

This overlaps SIG BeautyDesk substantially — same tenant model, same client entity, same MAUI stack. Decide now whether this is a standalone product or the client-records module of BeautyDesk, because it determines whether `Client` is owned here or consumed from a shared `SIG.Salon.Core` package. Building it standalone and merging later means reconciling two client tables across live tenant data, which is the worst version of this problem.

If BeautyDesk already has a Supabase project, this app should target the same one with additive tables rather than a second project — cross-project sync is not a thing you want to own.
