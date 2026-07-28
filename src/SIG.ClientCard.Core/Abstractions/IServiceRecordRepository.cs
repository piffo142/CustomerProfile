using SIG.ClientCard.Core.Entities;

namespace SIG.ClientCard.Core.Abstractions;

public interface IServiceRecordRepository
{
    Task<IReadOnlyList<ServiceRecord>> GetForClientAsync(Guid clientId, CancellationToken ct = default);

    Task UpsertAsync(ServiceRecord record, CancellationToken ct = default);

    /// <summary>Void a treatment. Delete-wins over concurrent edits during sync.</summary>
    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);
}
