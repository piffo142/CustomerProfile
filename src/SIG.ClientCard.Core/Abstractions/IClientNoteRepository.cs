using SIG.ClientCard.Core.Entities;

namespace SIG.ClientCard.Core.Abstractions;

public interface IClientNoteRepository
{
    /// <summary>Reverse-chronological feed for a client.</summary>
    Task<IReadOnlyList<ClientNote>> GetForClientAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Append a note. Notes are never updated or individually deleted.</summary>
    Task AddAsync(ClientNote note, CancellationToken ct = default);
}
