using Microsoft.EntityFrameworkCore;
using SIG.ClientCard.Core;
using SIG.ClientCard.Core.Abstractions;
using SIG.ClientCard.Core.Entities;
using SIG.ClientCard.Core.SyncContracts;
using SIG.ClientCard.Data.Infrastructure;

namespace SIG.ClientCard.Data.Repositories;

public sealed class ClientRepository(
    IDbContextFactory<ClientCardContext> dbFactory,
    OutboxWriter outbox,
    IClock clock,
    IDeviceIdentity device,
    ITenantContext tenant) : IClientRepository
{
    public async Task<Client?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Clients.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null, ct);
    }

    public async Task<Client?> GetWithDetailAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Clients.AsNoTracking()
            .Include(c => c.Notes.Where(n => n.DeletedAt == null).OrderByDescending(n => n.CreatedAt))
            .Include(c => c.Services.Where(s => s.DeletedAt == null).OrderByDescending(s => s.PerformedOn))
            .Include(c => c.Consents.Where(x => x.DeletedAt == null))
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null, ct);
    }

    public async Task<IReadOnlyList<Client>> SearchAsync(string? term, int skip, int take, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Clients.AsNoTracking().Where(c => c.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            query = query.Where(c =>
                EF.Functions.Like(c.LastName, $"%{t}%") ||
                EF.Functions.Like(c.FirstName, $"%{t}%") ||
                EF.Functions.Like(c.Phone, $"%{t}%") ||
                (c.Email != null && EF.Functions.Like(c.Email, $"%{t}%")));
        }

        return await query
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .Skip(skip).Take(take)
            .ToListAsync(ct);
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Clients.CountAsync(c => c.DeletedAt == null, ct);
    }

    public async Task UpsertAsync(Client client, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (client.Id == Guid.Empty)
        {
            client.Id = Guid.CreateVersion7();
        }

        if (client.SalonId == Guid.Empty)
        {
            client.SalonId = tenant.SalonId;
        }

        // Phone is stored E.164 normalised; the raw input rides in a shadow column.
        if (string.IsNullOrWhiteSpace(client.PhoneRaw))
        {
            client.PhoneRaw = client.Phone;
        }

        client.Phone = PhoneNumber.NormalizeE164(client.PhoneRaw);
        client.UpdatedAt = clock.UtcNow;
        client.UpdatedByDevice = device.DeviceId;

        var exists = await db.Clients.AnyAsync(c => c.Id == client.Id, ct);

        // Setting Entry.State attaches the root entity alone — unlike
        // Add/Update, it does not walk the navigation graph, so children stay
        // managed by their own repositories.
        db.Entry(client).State = exists ? EntityState.Modified : EntityState.Added;

        outbox.EnqueueUpsert(db, SyncEntities.Client, client.Id, ClientPayload.From(client));
        await db.SaveChangesAsync(ct);
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new InvalidOperationException($"Client {id} not found.");

        client.DeletedAt = clock.UtcNow;
        client.UpdatedAt = clock.UtcNow;
        client.UpdatedByDevice = device.DeviceId;

        outbox.EnqueueUpsert(db, SyncEntities.Client, client.Id, ClientPayload.From(client));
        await db.SaveChangesAsync(ct);
    }

    public async Task EraseAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var client = await db.Clients
            .Include(c => c.Notes)
            .Include(c => c.Services)
            .Include(c => c.Consents)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new InvalidOperationException($"Client {id} not found.");

        // Hard delete locally (children cascade); the server does the same and
        // writes a redaction_log row so peers can purge without the payload.
        db.Clients.Remove(client);

        // Any queued ops for this client or its children would re-create data
        // after erasure.
        var erasedIds = client.Notes.Select(n => n.Id)
            .Concat(client.Services.Select(s => s.Id))
            .Concat(client.Consents.Select(c => c.Id))
            .Append(id)
            .ToList();
        await db.Outbox.Where(o => erasedIds.Contains(o.EntityId)).ExecuteDeleteAsync(ct);

        outbox.EnqueueDelete(db, SyncEntities.Client, id);
        await db.SaveChangesAsync(ct);
    }
}
