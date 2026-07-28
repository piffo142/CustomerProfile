using Microsoft.EntityFrameworkCore;
using SIG.ClientCard.Core.Abstractions;
using SIG.ClientCard.Data;
using SIG.ClientCard.Sync.Transport;

namespace SIG.ClientCard.Sync.Attachments;

/// <summary>
/// Drains the attachment queue to Supabase Storage. Same discipline as the
/// outbox: at-least-once, capped exponential backoff, never dropped on
/// transport failure. Runs after each successful row sync.
/// </summary>
public sealed class AttachmentUploader(
    IDbContextFactory<ClientCardContext> dbFactory,
    SupabaseStorageClient storage,
    IAttachmentStore store,
    IClock clock,
    SyncOptions options)
{
    private static readonly Random Jitter = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task UploadPendingAsync(CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct))
        {
            return; // an upload pass is already running
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var now = clock.UtcNow;
            var pending = await db.AttachmentQueue
                .Where(a => a.UploadedAt == null && (a.NextAttemptAt == null || a.NextAttemptAt <= now))
                .OrderBy(a => a.CreatedAt)
                .Take(20)
                .ToListAsync(ct);

            foreach (var entry in pending)
            {
                var bytes = await store.ReadAsync(entry.RelativePath, ct);
                if (bytes is null)
                {
                    // File vanished locally (cleared storage): nothing to upload, ever.
                    entry.LastError = "local file missing";
                    entry.UploadedAt = clock.UtcNow;
                    continue;
                }

                try
                {
                    await storage.UploadAsync(entry.RelativePath, bytes, entry.ContentType, ct);
                    entry.UploadedAt = clock.UtcNow;
                    entry.LastError = null;
                }
                catch (SyncTransportException ex)
                {
                    entry.Attempts++;
                    entry.LastError = ex.Message;

                    var backoff = options.BackoffBase * Math.Pow(2, Math.Min(entry.Attempts - 1, 20));
                    if (backoff > options.BackoffCap)
                    {
                        backoff = options.BackoffCap;
                    }

                    entry.NextAttemptAt = clock.UtcNow + backoff
                        + TimeSpan.FromMilliseconds(Jitter.Next(0, 1000));
                }
            }

            await db.SaveChangesAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }
}
