# Supabase setup — Phase 1 (auth + tenancy) and Phase 2 (sync)

Everything the server needs is in `supabase/migrations/*.sql`. This document
covers the parts that require **manual configuration** in the Supabase
dashboard: creating the project, applying the SQL, creating users, and seeding
the salon. Allow 20–30 minutes.

## 1. Create the project

1. Sign in at [supabase.com](https://supabase.com) and click **New project**.
2. Region: **eu-west-2 (London)**. This is not cosmetic — client notes are UK
   GDPR Article 9 special category data and should stay in-region.
3. Choose a strong database password and store it in your password manager
   (you rarely need it again, but losing it is painful).
4. Wait for provisioning to finish (~2 minutes).

## 2. Apply the schema and RPCs

1. Open **SQL Editor** in the left sidebar.
2. Paste the full contents of `supabase/migrations/0001_schema.sql` and click
   **Run**. It creates the tables, the shared `sync_seq` sequence + triggers,
   and all row-level-security policies.
3. Repeat with `supabase/migrations/0002_sync_rpcs.sql` (the `sync_push` /
   `sync_pull` functions and the applied-op purge helper).
4. Repeat with `supabase/migrations/0003_sync_hardening.sql` (version
   handshake + `sync_checksum` for the periodic integrity check). To force
   outdated app installs to update before syncing again, raise
   `update sync_config set min_client_version = <n>;` when you ship a
   breaking schema change.

Alternatively, with the Supabase CLI: `supabase link --project-ref <ref>`
then `supabase db push`.

## 3. Configure authentication

1. Go to **Authentication → Sign In / Up → Email**.
2. Phase 1 uses **email + password** sign-in only. Disable anything you don't
   want (magic links, social providers).
3. **Confirm email**: for a small salon team it is reasonable to turn this
   *off* so accounts work the moment you create them. If you leave it on,
   each therapist must click the confirmation email before first sign-in.
4. Under **Authentication → URL Configuration** nothing is needed for the
   app (it talks to GoTrue directly, no redirects).

## 4. Create the salon and its staff

### 4.1 Create user accounts

**Authentication → Users → Add user → Create new user**, one per therapist.
Use their real email and a starter password (they can change it later).
Copy each user's **UUID** from the users table — you need them in the next step.

### 4.2 Seed the salon and memberships

Open the **SQL Editor** and run (replace the values):

```sql
-- One row per salon (tenant).
insert into salon (id, name, retention_years)
values (gen_random_uuid(), 'Beautiful You — Aesthetics and Beauty', 3)
returning id;
```

Note the returned salon `id`, then link every user to it:

```sql
insert into salon_member (salon_id, user_id, role) values
  ('<salon-id-from-above>', '<user-uuid-1>', 'owner'),
  ('<salon-id-from-above>', '<user-uuid-2>', 'therapist');
```

The `salon_member` table is the entire access model: RLS derives every
permission from it. **A user with no `salon_member` row can sign in but sees
no data**, and the app will refuse to proceed with
"This account is not a member of any salon yet."

## 5. Point the app at the project

1. In the dashboard, **Project Settings → API**:
   - **Project URL** — e.g. `https://abcdefgh.supabase.co`
   - **anon public** API key
2. Edit `src/SIG.ClientCard.App/Services/SupabaseConfig.cs`:

```csharp
public const string Url = "https://abcdefgh.supabase.co/";   // keep the trailing slash
public const string AnonKey = "eyJ...";                       // the anon key, never service_role
```

3. Rebuild and deploy the app.

The anon key is safe to ship in the app binary — every request is still
gated by RLS and the user's JWT. The **service_role** key must never leave
the dashboard.

## 6. First sign-in on each device

1. Launch the app → the login page appears.
2. Sign in with the therapist's email/password.
3. On first sign-in the app:
   - stores the refresh token in the platform secure store (Keychain/Keystore),
   - looks up the user's salon in `salon_member`,
   - **re-stamps all locally-created Phase 0 data with the real salon id**
     (one-time, automatic), and
   - starts syncing: outbox pushes, then pulls to convergence.
4. "Work offline" on the login page keeps the app fully usable without a
   session; queued changes sync after the next sign-in.

## 7. Housekeeping (recommended)

Schedule the idempotency-ledger purge. In the SQL Editor:

```sql
create extension if not exists pg_cron;
select cron.schedule('purge-sync-applied-ops', '0 3 * * *',
                     $$select purge_sync_applied_ops()$$);
```

(If `pg_cron` is unavailable on your plan, run `select purge_sync_applied_ops();`
manually every few weeks — the table only grows with pushed ops.)

## 8. Verify the security model

Worth five minutes before real client data arrives:

1. Create a second salon and a user who belongs only to it.
2. Sign in as that user in the app (or via the API) and confirm they see
   none of the first salon's clients — reads return empty, writes with the
   wrong `salon_id` are rejected by the `with check` policies and land in
   the app's dead-letter badge rather than another tenant's data.
3. In the dashboard **Table Editor**, confirm `client` rows carry the salon id
   you seeded, not a random one (proof the tenant adoption migration ran).

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| "Invalid login credentials" | Wrong password, or email confirmation still pending (see §3.3) |
| "This account is not a member of any salon yet" | Missing `salon_member` row (§4.2) |
| Sign-in works but sync count never drops | RLS rejecting pushes — check the salon id on the rows vs `salon_member`, and the dead-letter badge |
| Everything re-asks for login after app restart | SecureStorage unavailable (fresh device restore) — sign in once more, this is by design |
