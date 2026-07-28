using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SIG.ClientCard.Data;
using SIG.ClientCard.Data.Infrastructure;

namespace SIG.ClientCard.Tests.TestInfra;

/// <summary>
/// Shared-cache in-memory SQLite so multiple short-lived contexts (the app's
/// usage pattern) see one database. The keeper connection pins it alive.
/// Schema comes from the real migrations, so they get exercised too.
/// </summary>
public sealed class TestDb : IDbContextFactory<ClientCardContext>, IDisposable
{
    private readonly SqliteConnection _keeper;
    private readonly DbContextOptions<ClientCardContext> _options;

    public TestDb()
    {
        SQLitePCL.Batteries_V2.Init();

        var connString = $"Data Source=file:testdb-{Guid.NewGuid():N}?mode=memory&cache=shared";
        _keeper = new SqliteConnection(connString);
        _keeper.Open();

        _options = new DbContextOptionsBuilder<ClientCardContext>()
            .UseSqlite(connString)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;

        using var db = CreateDbContext();
        db.Database.Migrate();
    }

    public ClientCardContext CreateDbContext() => new(_options);

    public void Dispose() => _keeper.Dispose();
}
