using Microsoft.EntityFrameworkCore;
using SIG.ClientCard.Core.Abstractions;
using SIG.ClientCard.Core.Entities;
using SIG.ClientCard.Core.Enums;
using SIG.ClientCard.Core.SyncContracts;
using SIG.ClientCard.Data.Infrastructure;

namespace SIG.ClientCard.Data.Repositories;

public sealed class ClientConsentRepository(
    IDbContextFactory<ClientCardContext> dbFactory,
    OutboxWriter outbox,
    IClock clock,
    IDeviceIdentity device,
    ITenantContext tenant) : IClientConsentRepository
{
    public async Task<IReadOnlyList<ClientConsent>> GetForClientAsync(Guid clientId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ClientConsents.AsNoTracking()
            .Where(c => c.ClientId == clientId && c.DeletedAt == null)
            .OrderBy(c => c.Purpose)
            .ToListAsync(ct);
    }

    public async Task GrantAsync(ClientConsent consent, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (consent.Id == Guid.Empty)
        {
            consent.Id = Guid.CreateVersion7();
        }

        if (consent.SalonId == Guid.Empty)
        {
            consent.SalonId = tenant.SalonId;
        }

        if (consent.GrantedAt == default)
        {
            consent.GrantedAt = clock.UtcNow;
        }

        consent.UpdatedAt = clock.UtcNow;
        consent.UpdatedByDevice = device.DeviceId;

        db.ClientConsents.Add(consent);
        outbox.EnqueueUpsert(db, SyncEntities.ClientConsent, consent.Id, ClientConsentPayload.From(consent));
        await db.SaveChangesAsync(ct);
    }

    public async Task WithdrawAsync(Guid clientId, ConsentPurpose purpose, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var active = await db.ClientConsents
            .Where(c => c.ClientId == clientId && c.Purpose == purpose && c.WithdrawnAt == null)
            .ToListAsync(ct);

        foreach (var consent in active)
        {
            consent.WithdrawnAt = clock.UtcNow;
            consent.UpdatedAt = clock.UtcNow;
            consent.UpdatedByDevice = device.DeviceId;
            outbox.EnqueueUpsert(db, SyncEntities.ClientConsent, consent.Id, ClientConsentPayload.From(consent));
        }

        await db.SaveChangesAsync(ct);
    }
}
