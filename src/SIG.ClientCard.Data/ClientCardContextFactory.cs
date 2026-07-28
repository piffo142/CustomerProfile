using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SIG.ClientCard.Data.Infrastructure;

namespace SIG.ClientCard.Data;

/// <summary>Design-time factory for `dotnet ef` (migrations, compiled model).</summary>
public sealed class ClientCardContextFactory : IDesignTimeDbContextFactory<ClientCardContext>
{
    public ClientCardContext CreateDbContext(string[] args)
    {
        SQLitePCL.Batteries_V2.Init();

        var options = new DbContextOptionsBuilder<ClientCardContext>()
            .UseSqlite("Data Source=design-time.db")
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;

        return new ClientCardContext(options);
    }
}
