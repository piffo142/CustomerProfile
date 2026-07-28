using SIG.ClientCard.Core.Entities;
using SIG.ClientCard.Core.Enums;

namespace SIG.ClientCard.Core.Abstractions;

public interface IClientConsentRepository
{
    Task<IReadOnlyList<ClientConsent>> GetForClientAsync(Guid clientId, CancellationToken ct = default);

    Task GrantAsync(ClientConsent consent, CancellationToken ct = default);

    Task WithdrawAsync(Guid clientId, ConsentPurpose purpose, CancellationToken ct = default);
}
