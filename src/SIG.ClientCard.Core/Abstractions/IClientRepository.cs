using SIG.ClientCard.Core.Entities;

namespace SIG.ClientCard.Core.Abstractions;

public interface IClientRepository
{
    Task<Client?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Client with notes, services and consents loaded for the detail page.</summary>
    Task<Client?> GetWithDetailAsync(Guid id, CancellationToken ct = default);

    /// <summary>Live (non-deleted) clients matching the search term, ordered Last, First.</summary>
    Task<IReadOnlyList<Client>> SearchAsync(string? term, int skip, int take, CancellationToken ct = default);

    Task<int> CountAsync(CancellationToken ct = default);

    Task UpsertAsync(Client client, CancellationToken ct = default);

    /// <summary>Soft delete: sets the tombstone so the delete propagates via sync.</summary>
    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// GDPR erasure: hard-deletes the client and all children and enqueues a
    /// redaction op so peers purge without retaining the payload.
    /// </summary>
    Task EraseAsync(Guid id, CancellationToken ct = default);
}
