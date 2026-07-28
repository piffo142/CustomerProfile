namespace SIG.ClientCard.Core.Entities;

/// <summary>
/// Local mirror of the tenant record. Carries the retention policy that drives
/// the scheduled purge job server-side.
/// </summary>
public class Salon
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Client records are purged this long after their last service.</summary>
    public int RetentionYears { get; set; } = 3;
}
