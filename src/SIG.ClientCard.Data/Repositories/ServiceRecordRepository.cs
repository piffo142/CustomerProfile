using Microsoft.EntityFrameworkCore;
using SIG.ClientCard.Core.Abstractions;
using SIG.ClientCard.Core.Entities;
using SIG.ClientCard.Core.SyncContracts;
using SIG.ClientCard.Data.Infrastructure;

namespace SIG.ClientCard.Data.Repositories;

public sealed class ServiceRecordRepository(
    IDbContextFactory<ClientCardContext> dbFactory,
    OutboxWriter outbox,
    IClock clock,
    IDeviceIdentity device,
    ITenantContext tenant) : IServiceRecordRepository
{
    public async Task<IReadOnlyList<ServiceRecord>> GetForClientAsync(Guid clientId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ServiceRecords.AsNoTracking()
            .Where(s => s.ClientId == clientId && s.DeletedAt == null)
            .OrderByDescending(s => s.PerformedOn)
            .ToListAsync(ct);
    }

    public async Task UpsertAsync(ServiceRecord record, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (record.Id == Guid.Empty)
        {
            record.Id = Guid.CreateVersion7();
        }

        if (record.SalonId == Guid.Empty)
        {
            record.SalonId = tenant.SalonId;
        }

        record.UpdatedAt = clock.UtcNow;
        record.UpdatedByDevice = device.DeviceId;

        var exists = await db.ServiceRecords.AnyAsync(s => s.Id == record.Id, ct);
        db.Entry(record).State = exists ? EntityState.Modified : EntityState.Added;

        outbox.EnqueueUpsert(db, SyncEntities.ServiceRecord, record.Id, ServiceRecordPayload.From(record));
        await db.SaveChangesAsync(ct);
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var record = await db.ServiceRecords.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new InvalidOperationException($"Service record {id} not found.");

        record.DeletedAt = clock.UtcNow;
        record.UpdatedAt = clock.UtcNow;
        record.UpdatedByDevice = device.DeviceId;

        outbox.EnqueueUpsert(db, SyncEntities.ServiceRecord, record.Id, ServiceRecordPayload.From(record));
        await db.SaveChangesAsync(ct);
    }
}
