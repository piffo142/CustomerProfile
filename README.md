# Beautiful You — Client Card

Offline-first client records for a beauty salon: the paper client card
(name, contact, "how did you hear about us", service/price history, notes)
as a .NET MAUI app for **Android, iOS, macOS (Mac Catalyst) and Windows**,
backed by encrypted local SQLite with a Supabase sync path designed in from
day one.

Built to the plan in `docs/` (domain model → sync design → phasing). Currently
at **Phase 0 (local-only)** with the full sync engine implemented and tested
against a fake transport, ready for a Supabase project to be plugged in at
Phase 2.

## Solution layout

```
SIG.ClientCard.Core   entities, enums, interfaces, wire contracts. No package refs.
SIG.ClientCard.Data   EF Core 10 + SQLite, DbContext, migrations, compiled model, repositories
SIG.ClientCard.Sync   outbox push, cursor pull, conflict policy, Supabase transport
SIG.ClientCard.App    MAUI: Shell, views, viewmodels (CommunityToolkit.Mvvm)
SIG.ClientCard.Tests  xUnit: repositories + sync engine on in-memory SQLite, fake transport
supabase/migrations   Postgres schema, RLS, sync_push / sync_pull RPCs
```

> The plan targeted .NET 9; this implementation targets **.NET 10 (LTS)** —
> .NET 9 (STS) reached end of support in May 2026. Everything the plan relies
> on (`Guid.CreateVersion7`, EF compiled models, iOS interpreter flags) is
> unchanged.

## Key design decisions (from the plan)

- **Local SQLite is the source of truth for the UI.** No screen ever awaits a
  network call; sync is a background reconciliation process.
- **Notes are append-only rows**, not a mutable column — two therapists editing
  notes offline merge as a union, so clinical history can never be silently
  destroyed by last-writer-wins.
- **Every write goes into the domain table and the sync outbox in one SQLite
  transaction** (single `SaveChanges`), so a crash can never lose a queued change.
- **Conflict policy per entity**: client = LWW + device-id tiebreak;
  notes = union; service records = delete-wins (a voided treatment must not
  resurrect); consents = withdrawal always wins (legal requirement).
- **Cursor safety**: one database-wide `sync_seq` sequence, pulls re-deliver an
  overlap window below the cursor, and every apply is an idempotent upsert.
- **GDPR**: consent table in the v1 schema; erasure is a hard delete plus a
  `redaction_log` row (id + timestamp only) so peers purge without retaining
  the payload; database encrypted at rest with SQLCipher, key in SecureStorage
  (with the Android restore edge case handled — a failed Keystore read clears
  and rebuilds instead of bricking the app).

## Building

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0),
plus the MAUI workload for the platforms you target:

```bash
dotnet workload install maui           # macOS/Windows: all platforms
dotnet workload install maui-android   # Linux: Android only
```

Libraries and tests build on any OS:

```bash
dotnet build src/SIG.ClientCard.Core src/SIG.ClientCard.Data src/SIG.ClientCard.Sync
dotnet test tests/SIG.ClientCard.Tests
```

The app (`src/SIG.ClientCard.App`):

```bash
# Android (needs Android SDK + JDK 17+)
dotnet build src/SIG.ClientCard.App -f net10.0-android

# iOS / Mac Catalyst (needs a Mac with Xcode)
dotnet build src/SIG.ClientCard.App -f net10.0-ios
dotnet build src/SIG.ClientCard.App -f net10.0-maccatalyst

# Windows (on Windows)
dotnet build src/SIG.ClientCard.App -f net10.0-windows10.0.19041.0
```

iOS Release builds use the Mono interpreter (`MtouchInterpreter=-all`) because
EF Core's reflection paths do not survive the iOS linker. Test Release on a
device early, not at submission.

### After changing the EF model

The compiled model (`Data/CompiledModels`) must be regenerated after every
migration — CI fails if it drifts:

```bash
dotnet tool install -g dotnet-ef
cd src/SIG.ClientCard.Data
dotnet ef migrations add <Name>
dotnet ef dbcontext optimize --output-dir CompiledModels --namespace SIG.ClientCard.Data.CompiledModels
```

## Enabling sync (Phase 1–2)

1. Create a Supabase project in **eu-west-2 (London)**.
2. Apply `supabase/migrations/*.sql` in order (SQL editor or `supabase db push`).
3. Fill in `Url` and `AnonKey` in `src/SIG.ClientCard.App/Services/SupabaseConfig.cs`.
4. Seed a `salon` row and `salon_member` rows for your users (RLS derives all
   access from `salon_member`).

Until then the app runs local-only and the outbox simply accumulates; the sync
status in the flyout footer shows "Local only".

Before committing to the hand-rolled engine long-term, the plan recommends a
two-day PowerSync.MAUI spike at Phase 2 — the schema and Phase 0/1 code are
identical either way.

## Branding

The entry screen carries the Beautiful You logo, recreated as vector line-art
(`Resources/Images/logo_beautiful_you.svg`) with the wordmark rendered as
styled labels. To use the original artwork instead, drop a PNG export in
`Resources/Images/` and update the `Image` source in `Views/ClientsPage.xaml`.

## Open question (decide before Phase 1)

This overlaps SIG BeautyDesk substantially — same tenant model, same client
entity, same MAUI stack. Decide now whether this is a standalone product or
the client-records module of BeautyDesk: it determines whether `Client` is
owned here or consumed from a shared package, and whether the Supabase tables
land in BeautyDesk's existing project (additive tables in the same project
beat cross-project sync). Merging two live client tables later is the worst
version of this problem.
