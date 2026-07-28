using SIG.ClientCard.Core.Entities;

namespace SIG.ClientCard.Core.Abstractions;

public interface IServiceCatalogRepository
{
    Task<IReadOnlyList<ServiceCatalogItem>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Learn a service from a free-text entry: case-insensitive match on name
    /// updates the default price, otherwise a new catalogue item is created.
    /// </summary>
    Task<ServiceCatalogItem> LearnAsync(string name, decimal price, CancellationToken ct = default);
}
