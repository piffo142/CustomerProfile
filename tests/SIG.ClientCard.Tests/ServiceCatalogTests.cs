using Microsoft.EntityFrameworkCore;
using SIG.ClientCard.Core.Entities;
using SIG.ClientCard.Core.SyncContracts;
using SIG.ClientCard.Data.Infrastructure;
using SIG.ClientCard.Data.Repositories;
using SIG.ClientCard.Sync;
using SIG.ClientCard.Tests.TestInfra;

namespace SIG.ClientCard.Tests;

public class ServiceCatalogTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly FakeClock _clock = new();
    private readonly FakeDevice _device = new();
    private readonly FakeTenant _tenant = new();
    private readonly FakeTransport _transport = new();

    private ServiceCatalogRepository Catalog => new(_db, new OutboxWriter(_clock), _clock, _device, _tenant);
    private SyncEngine Engine => new(_db, _transport, _clock, new SyncOptions());

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Learn_creates_then_dedupes_case_insensitively()
    {
        var first = await Catalog.LearnAsync("Full Body Massage", 60m);
        var second = await Catalog.LearnAsync("full body massage", 60m);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await Catalog.GetAllAsync());

        // Same item, new price → default updated, still one item, sync op queued.
        var repriced = await Catalog.LearnAsync("FULL BODY MASSAGE", 65m);
        Assert.Equal(first.Id, repriced.Id);
        Assert.Equal(65m, (await Catalog.GetAllAsync()).Single().DefaultPrice);

        await using var db = _db.CreateDbContext();
        Assert.Equal(2, await db.Outbox.CountAsync(o => o.Entity == SyncEntities.ServiceCatalog));
    }

    [Fact]
    public async Task Catalog_pushes_before_service_records()
    {
        var clients = new ClientRepository(_db, new OutboxWriter(_clock), _clock, _device, _tenant);
        var services = new ServiceRecordRepository(_db, new OutboxWriter(_clock), _clock, _device, _tenant);

        var client = new Client { LastName = "Smith", FirstName = "Anna" };
        await clients.UpsertAsync(client);
        var item = await Catalog.LearnAsync("Facial", 45m);
        await services.UpsertAsync(new ServiceRecord
        {
            ClientId = client.Id,
            PerformedOn = new DateOnly(2026, 7, 1),
            ServiceDescription = "Facial",
            Price = 45m,
            ServiceCatalogId = item.Id,
        });

        await Engine.PushPendingAsync();

        var batch = Assert.Single(_transport.PushedBatches);
        Assert.Equal(
            [SyncEntities.Client, SyncEntities.ServiceCatalog, SyncEntities.ServiceRecord],
            batch.Select(o => o.Entity).ToArray());
    }

    [Fact]
    public async Task Pulled_catalog_items_apply_with_lww()
    {
        var mine = await Catalog.LearnAsync("Manicure", 25m);

        var newer = ServiceCatalogPayload.From(mine);
        newer.DefaultPrice = 30m;
        newer.UpdatedAt = mine.UpdatedAt + TimeSpan.FromHours(1);
        newer.UpdatedByDevice = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
        newer.SyncSeq = 3;

        var fresh = new ServiceCatalogPayload
        {
            Id = Guid.CreateVersion7(),
            SalonId = _tenant.SalonId,
            Name = "Pedicure",
            DefaultPrice = 28m,
            UpdatedAt = _clock.UtcNow,
            UpdatedByDevice = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),
            SyncSeq = 4,
        };

        _transport.PullPages.Enqueue(new SyncPullBundle { Catalog = [newer, fresh] });
        await Engine.PullToConvergenceAsync();

        var all = await Catalog.GetAllAsync();
        Assert.Equal(2, all.Count);
        Assert.Equal(30m, all.Single(i => i.Id == mine.Id).DefaultPrice);
    }
}
