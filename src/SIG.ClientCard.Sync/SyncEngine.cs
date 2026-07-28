using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SIG.ClientCard.Core.Abstractions;
using SIG.ClientCard.Core.Entities;
using SIG.ClientCard.Core.SyncContracts;
using SIG.ClientCard.Data;
using SIG.ClientCard.Data.Entities;

namespace SIG.ClientCard.Sync;

/// <summary>
/// Offline-first reconciliation. Local SQLite is the source of truth for the
/// UI; this engine is a background process. No screen ever awaits it.
/// </summary>
public sealed class SyncEngine(
    IDbContextFactory<ClientCardContext> dbFactory,
    ISyncTransport transport,
    IClock clock,
    SyncOptions options)
{
    private static readonly Random Jitter = new();

    /// <summary>Push everything eligible, then pull to convergence.</summary>
    public async Task SyncNowAsync(CancellationToken ct = default)
    {
        await PushPendingAsync(ct);
        await PullToConvergenceAsync(ct);
    }

    // ---------------------------------------------------------------- push

    public async Task PushPendingAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var now = clock.UtcNow;

            var eligible = await db.Outbox
                .Where(o => o.NextAttemptAt == null || o.NextAttemptAt <= now)
                .ToListAsync(ct);

            if (eligible.Count == 0)
            {
                return;
            }

            // Static push order: client before service_record before client_note,
            // or FK constraints reject the batch.
            var batch = eligible
                .OrderBy(o => SyncEntities.PushOrdinal(o.Entity))
                .ThenBy(o => o.CreatedAt)
                .Take(options.PushBatchSize)
                .ToList();

            var ops = batch
                .Select(o => new SyncOp(o.OpId, o.Entity, o.EntityId, o.Operation, o.Payload, o.CreatedAt))
                .ToList();

            SyncPushResult result;
            try
            {
                result = await transport.PushAsync(ops, ct);
            }
            catch (SyncTransportException ex)
            {
                await RecordFailureAsync(db, batch, ex.Message, ct);
                return; // network is down or flaky; the next trigger retries
            }

            var rejected = result.RejectedOpIds.ToHashSet();
            foreach (var entry in batch)
            {
                if (rejected.Contains(entry.OpId))
                {
                    // Validation rejection: never retry a 4xx. Park and surface.
                    db.DeadLetters.Add(new SyncDeadLetterEntry
                    {
                        OpId = entry.OpId,
                        Entity = entry.Entity,
                        EntityId = entry.EntityId,
                        Operation = entry.Operation,
                        Payload = entry.Payload,
                        CreatedAt = entry.CreatedAt,
                        Attempts = entry.Attempts + 1,
                        LastError = "rejected by server",
                        ParkedAt = clock.UtcNow,
                    });
                }

                db.Outbox.Remove(entry);
            }

            var state = await GetStateAsync(db, ct);
            state.LastPushAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);

            if (eligible.Count <= batch.Count)
            {
                return;
            }
        }
    }

    private async Task RecordFailureAsync(
        ClientCardContext db, List<SyncOutboxEntry> batch, string error, CancellationToken ct)
    {
        foreach (var entry in batch)
        {
            entry.Attempts++;
            entry.LastError = error;

            if (entry.Attempts >= options.MaxAttempts)
            {
                db.DeadLetters.Add(new SyncDeadLetterEntry
                {
                    OpId = entry.OpId,
                    Entity = entry.Entity,
                    EntityId = entry.EntityId,
                    Operation = entry.Operation,
                    Payload = entry.Payload,
                    CreatedAt = entry.CreatedAt,
                    Attempts = entry.Attempts,
                    LastError = error,
                    ParkedAt = clock.UtcNow,
                });
                db.Outbox.Remove(entry);
                continue;
            }

            // Exponential backoff with jitter, capped.
            var backoff = options.BackoffBase * Math.Pow(2, entry.Attempts - 1);
            if (backoff > options.BackoffCap)
            {
                backoff = options.BackoffCap;
            }

            var jitter = TimeSpan.FromMilliseconds(Jitter.Next(0, 1000));
            entry.NextAttemptAt = clock.UtcNow + backoff + jitter;
        }

        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- pull

    public async Task PullToConvergenceAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var shortPage = await PullOnceAsync(ct);
            if (shortPage)
            {
                return;
            }
        }
    }

    /// <summary>Pulls one page. Returns true when the page came back short (converged).</summary>
    public async Task<bool> PullOnceAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var state = await GetStateAsync(db, ct);

        // Overlap window: re-deliver a slice below the cursor to cover
        // statement-time sequence allocation. Idempotent applies make it harmless.
        var from = Math.Max(0, state.Cursor - options.OverlapWindow);

        var bundle = await transport.PullAsync(from, options.PullLimit, ct);

        // Whole bundle + cursor advance in one SQLite transaction: cursor commit
        // and data apply must be atomic or a crash mid-apply loses data silently.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        foreach (var payload in bundle.Clients)
        {
            await ApplyClientAsync(db, payload, ct);
        }

        foreach (var payload in bundle.Services)
        {
            await ApplyServiceAsync(db, payload, ct);
        }

        foreach (var payload in bundle.Notes)
        {
            await ApplyNoteAsync(db, payload, ct);
        }

        foreach (var payload in bundle.Consents)
        {
            await ApplyConsentAsync(db, payload, ct);
        }

        foreach (var redaction in bundle.Redactions)
        {
            await ApplyRedactionAsync(db, redaction, ct);
        }

        var newCursor = ComputeNewCursor(state.Cursor, bundle, options.PullLimit, out var converged);
        state.Cursor = newCursor;
        state.LastPullAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return converged;
    }

    /// <summary>
    /// Advance to the minimum max-seq across FULL arrays — a full array is
    /// bounded by the page limit, so rows above its max in other arrays would be
    /// skipped if we advanced to the overall max. When every array is short the
    /// page is complete and the overall max is safe.
    /// </summary>
    internal static long ComputeNewCursor(long current, SyncPullBundle bundle, int limit, out bool converged)
    {
        var arrays = new (int Count, long MaxSeq)[]
        {
            (bundle.Clients.Count, bundle.Clients.Count == 0 ? 0 : bundle.Clients.Max(x => x.SyncSeq)),
            (bundle.Services.Count, bundle.Services.Count == 0 ? 0 : bundle.Services.Max(x => x.SyncSeq)),
            (bundle.Notes.Count, bundle.Notes.Count == 0 ? 0 : bundle.Notes.Max(x => x.SyncSeq)),
            (bundle.Consents.Count, bundle.Consents.Count == 0 ? 0 : bundle.Consents.Max(x => x.SyncSeq)),
            (bundle.Redactions.Count, bundle.Redactions.Count == 0 ? 0 : bundle.Redactions.Max(x => x.SyncSeq)),
        };

        var fullArrays = arrays.Where(a => a.Count >= limit).ToList();
        converged = fullArrays.Count == 0;

        var candidate = converged
            ? arrays.Where(a => a.Count > 0).Select(a => a.MaxSeq).DefaultIfEmpty(current).Max()
            : fullArrays.Min(a => a.MaxSeq);

        return Math.Max(current, candidate);
    }

    // ------------------------------------------------- per-entity conflict policy

    /// <summary>
    /// Deterministic LWW: newer updated_at wins; on an exact tie the higher
    /// device id wins, compared as canonical uuid text to match Postgres uuid
    /// ordering. Without the tiebreak two devices can settle on different
    /// winners and oscillate.
    /// </summary>
    internal static bool IncomingWins(
        DateTimeOffset incomingUpdatedAt, Guid incomingDevice,
        DateTimeOffset localUpdatedAt, Guid localDevice)
    {
        if (incomingUpdatedAt != localUpdatedAt)
        {
            return incomingUpdatedAt > localUpdatedAt;
        }

        return string.CompareOrdinal(incomingDevice.ToString("D"), localDevice.ToString("D")) > 0;
    }

    private async Task ApplyClientAsync(ClientCardContext db, ClientPayload payload, CancellationToken ct)
    {
        var local = await db.Clients.FirstOrDefaultAsync(c => c.Id == payload.Id, ct);
        if (local is null)
        {
            db.Clients.Add(payload.ToEntity());
            return;
        }

        if (!IncomingWins(payload.UpdatedAt, payload.UpdatedByDevice, local.UpdatedAt, local.UpdatedByDevice))
        {
            return;
        }

        db.Entry(local).CurrentValues.SetValues(payload.ToEntity());
    }

    private async Task ApplyServiceAsync(ClientCardContext db, ServiceRecordPayload payload, CancellationToken ct)
    {
        var local = await db.ServiceRecords.FirstOrDefaultAsync(s => s.Id == payload.Id, ct);
        if (local is null)
        {
            if (await ParentExistsAsync(db, payload.ClientId, ct))
            {
                db.ServiceRecords.Add(payload.ToEntity());
            }

            return;
        }

        // Delete-wins over update: a voided treatment must not resurrect.
        if (payload.DeletedAt is not null)
        {
            db.Entry(local).CurrentValues.SetValues(payload.ToEntity());
            return;
        }

        if (local.DeletedAt is not null)
        {
            return;
        }

        if (IncomingWins(payload.UpdatedAt, payload.UpdatedByDevice, local.UpdatedAt, local.UpdatedByDevice))
        {
            db.Entry(local).CurrentValues.SetValues(payload.ToEntity());
        }
    }

    private async Task ApplyNoteAsync(ClientCardContext db, ClientNotePayload payload, CancellationToken ct)
    {
        // Append-only union: no conflict possible by construction. Existing
        // notes are never overwritten.
        var exists = await db.ClientNotes.AnyAsync(n => n.Id == payload.Id, ct);
        if (!exists && await ParentExistsAsync(db, payload.ClientId, ct))
        {
            db.ClientNotes.Add(payload.ToEntity());
        }
    }

    private async Task ApplyConsentAsync(ClientCardContext db, ClientConsentPayload payload, CancellationToken ct)
    {
        var local = await db.ClientConsents.FirstOrDefaultAsync(c => c.Id == payload.Id, ct);
        if (local is null)
        {
            if (await ParentExistsAsync(db, payload.ClientId, ct))
            {
                db.ClientConsents.Add(payload.ToEntity());
            }

            return;
        }

        // Withdrawal always wins — legal requirement, never LWW. Preserve the
        // earliest withdrawal regardless of which side wins the field merge.
        var earliestWithdrawal = (local.WithdrawnAt, payload.WithdrawnAt) switch
        {
            (null, var incoming) => incoming,
            (var mine, null) => mine,
            (var mine, var incoming) => mine < incoming ? mine : incoming,
        };

        if (IncomingWins(payload.UpdatedAt, payload.UpdatedByDevice, local.UpdatedAt, local.UpdatedByDevice))
        {
            db.Entry(local).CurrentValues.SetValues(payload.ToEntity());
        }

        local.WithdrawnAt = earliestWithdrawal;
    }

    private static async Task ApplyRedactionAsync(ClientCardContext db, RedactionPayload redaction, CancellationToken ct)
    {
        switch (redaction.Entity)
        {
            case SyncEntities.Client:
                var client = await db.Clients
                    .Include(c => c.Notes).Include(c => c.Services).Include(c => c.Consents)
                    .FirstOrDefaultAsync(c => c.Id == redaction.EntityId, ct);
                if (client is not null)
                {
                    db.Clients.Remove(client);
                }

                break;

            case SyncEntities.ServiceRecord:
                await db.ServiceRecords
                    .Where(s => s.Id == redaction.EntityId).ExecuteDeleteAsync(ct);
                break;

            case SyncEntities.ClientNote:
                await db.ClientNotes
                    .Where(n => n.Id == redaction.EntityId).ExecuteDeleteAsync(ct);
                break;
        }
    }

    private static Task<bool> ParentExistsAsync(ClientCardContext db, Guid clientId, CancellationToken ct)
        => db.Clients.AnyAsync(c => c.Id == clientId, ct);

    // ---------------------------------------------------------------- status

    public async Task<SyncStatus> GetStatusAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var state = await db.SyncState.AsNoTracking().FirstOrDefaultAsync(ct);
        return new SyncStatus(
            PendingOps: await db.Outbox.CountAsync(ct),
            DeadLetteredOps: await db.DeadLetters.CountAsync(ct),
            LastPushAt: state?.LastPushAt,
            LastPullAt: state?.LastPullAt);
    }

    private static async Task<SyncStateRow> GetStateAsync(ClientCardContext db, CancellationToken ct)
    {
        var state = await db.SyncState.FirstOrDefaultAsync(s => s.Id == SyncStateRow.SingletonId, ct);
        if (state is null)
        {
            state = new SyncStateRow { Id = SyncStateRow.SingletonId };
            db.SyncState.Add(state);
        }

        return state;
    }
}
