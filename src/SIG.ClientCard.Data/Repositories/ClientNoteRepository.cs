using Microsoft.EntityFrameworkCore;
using SIG.ClientCard.Core.Abstractions;
using SIG.ClientCard.Core.Entities;
using SIG.ClientCard.Core.SyncContracts;
using SIG.ClientCard.Data.Infrastructure;

namespace SIG.ClientCard.Data.Repositories;

public sealed class ClientNoteRepository(
    IDbContextFactory<ClientCardContext> dbFactory,
    OutboxWriter outbox,
    IClock clock,
    IDeviceIdentity device,
    ITenantContext tenant) : IClientNoteRepository
{
    public async Task<IReadOnlyList<ClientNote>> GetForClientAsync(Guid clientId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ClientNotes.AsNoTracking()
            .Where(n => n.ClientId == clientId && n.DeletedAt == null)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task AddAsync(ClientNote note, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (note.Id == Guid.Empty)
        {
            note.Id = Guid.CreateVersion7();
        }

        if (note.SalonId == Guid.Empty)
        {
            note.SalonId = tenant.SalonId;
        }

        if (note.CreatedAt == default)
        {
            note.CreatedAt = clock.UtcNow;
        }

        note.UpdatedAt = clock.UtcNow;
        note.UpdatedByDevice = device.DeviceId;

        db.ClientNotes.Add(note);
        outbox.EnqueueUpsert(db, SyncEntities.ClientNote, note.Id, ClientNotePayload.From(note));
        await db.SaveChangesAsync(ct);
    }
}
