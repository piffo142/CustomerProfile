using Microsoft.EntityFrameworkCore;
using SIG.ClientCard.Core.Abstractions;
using SIG.ClientCard.Core.Entities;
using SIG.ClientCard.Core.SyncContracts;
using SIG.ClientCard.Data.Infrastructure;

namespace SIG.ClientCard.Data.Repositories;

public sealed class ServiceCatalogRepository(
    IDbContextFactory<ClientCardContext> dbFactory,
    OutboxWriter outbox,
    IClock clock,
    IDeviceIdentity device,
    ITenantContext tenant) : IServiceCatalogRepository
{
    public async Task<IReadOnlyList<ServiceCatalogItem>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ServiceCatalogItems.AsNoTracking()
            .Where(i => i.DeletedAt == null)
            .OrderBy(i => i.Name)
            .ToListAsync(ct);
    }

    public async Task<ServiceCatalogItem> LearnAsync(string name, decimal price, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var trimmed = name.Trim();

        var existing = await db.ServiceCatalogItems
            .FirstOrDefaultAsync(i => i.DeletedAt == null && i.Name.ToLower() == trimmed.ToLower(), ct);

        if (existing is not null)
        {
            if (existing.DefaultPrice == price)
            {
                return existing; // nothing changed; no sync noise
            }

            existing.DefaultPrice = price;
            existing.UpdatedAt = clock.UtcNow;
            existing.UpdatedByDevice = device.DeviceId;
            outbox.EnqueueUpsert(db, SyncEntities.ServiceCatalog, existing.Id, ServiceCatalogPayload.From(existing));
            await db.SaveChangesAsync(ct);
            return existing;
        }

        var item = new ServiceCatalogItem
        {
            Id = Guid.CreateVersion7(),
            SalonId = tenant.SalonId,
            Name = trimmed,
            DefaultPrice = price,
            UpdatedAt = clock.UtcNow,
            UpdatedByDevice = device.DeviceId,
        };

        db.ServiceCatalogItems.Add(item);
        outbox.EnqueueUpsert(db, SyncEntities.ServiceCatalog, item.Id, ServiceCatalogPayload.From(item));
        await db.SaveChangesAsync(ct);
        return item;
    }
}
