using Microsoft.EntityFrameworkCore;

namespace SIG.ClientCard.Data;

public sealed record MonthlyRevenue(int Year, int Month, decimal Total, int Visits)
{
    public string Label => new DateOnly(Year, Month, 1).ToString("MMM yyyy");
}

public sealed record ServicePopularity(string Name, int Count, decimal Revenue);

public sealed record ReportData(
    int ActiveClients,
    decimal RevenueLast12Months,
    IReadOnlyList<MonthlyRevenue> Monthly,
    IReadOnlyList<ServicePopularity> TopServices);

/// <summary>
/// Local reporting straight off SQLite — salon-scale data, so rows are
/// aggregated in memory rather than fighting provider translation.
/// </summary>
public sealed class ReportService(IDbContextFactory<ClientCardContext> dbFactory)
{
    public async Task<ReportData> GetAsync(DateOnly today, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var cutoff = today.AddMonths(-11);
        var windowStart = new DateOnly(cutoff.Year, cutoff.Month, 1);

        var rows = await db.ServiceRecords.AsNoTracking()
            .Where(s => s.DeletedAt == null && s.PerformedOn >= windowStart)
            .Select(s => new { s.PerformedOn, s.Price, s.ServiceDescription })
            .ToListAsync(ct);

        var monthly = rows
            .GroupBy(r => (r.PerformedOn.Year, r.PerformedOn.Month))
            .Select(g => new MonthlyRevenue(g.Key.Year, g.Key.Month, g.Sum(x => x.Price), g.Count()))
            .OrderByDescending(m => (m.Year, m.Month))
            .ToList();

        var top = rows
            .GroupBy(r => r.ServiceDescription.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new ServicePopularity(g.Key, g.Count(), g.Sum(x => x.Price)))
            .OrderByDescending(s => s.Revenue)
            .Take(10)
            .ToList();

        var activeClients = await db.Clients.CountAsync(c => c.DeletedAt == null, ct);

        return new ReportData(
            ActiveClients: activeClients,
            RevenueLast12Months: rows.Sum(r => r.Price),
            Monthly: monthly,
            TopServices: top);
    }
}
