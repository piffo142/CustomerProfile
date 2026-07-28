using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;

namespace SIG.ClientCard.Data.Infrastructure;

/// <summary>
/// Phase 0 devices stamp rows with a locally-minted salon id. On first sign-in
/// the real tenant id arrives from the server, and every local row — plus every
/// queued outbox payload — must be re-stamped before the first push, or RLS
/// with-check rejects the lot. Runs in one transaction; safe because nothing
/// has ever synced under the placeholder id.
/// </summary>
public sealed class TenantMigrator(IDbContextFactory<ClientCardContext> dbFactory)
{
    public async Task AdoptSalonAsync(Guid salonId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var sid = salonId.ToString("D");
        await db.Database.ExecuteSqlAsync($"UPDATE client SET salon_id = {sid}", ct);
        await db.Database.ExecuteSqlAsync($"UPDATE client_note SET salon_id = {sid}", ct);
        await db.Database.ExecuteSqlAsync($"UPDATE service_record SET salon_id = {sid}", ct);
        await db.Database.ExecuteSqlAsync($"UPDATE client_consent SET salon_id = {sid}", ct);

        foreach (var op in await db.Outbox.ToListAsync(ct))
        {
            if (JsonNode.Parse(op.Payload) is JsonObject payload && payload.ContainsKey("salon_id"))
            {
                payload["salon_id"] = sid;
                op.Payload = payload.ToJsonString();
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
