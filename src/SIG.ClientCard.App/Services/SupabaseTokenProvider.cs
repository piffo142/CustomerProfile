using SIG.ClientCard.Sync.Transport;

namespace SIG.ClientCard.App.Services;

/// <summary>
/// Phase 1 skeleton for GoTrue: refresh token in SecureStorage, access token in
/// memory only. Until auth ships, the anon key is the bearer (RLS still gates
/// every row server-side).
/// </summary>
public sealed class SupabaseTokenProvider : IAccessTokenProvider
{
    public Task<string> GetAccessTokenAsync(CancellationToken ct = default)
        => Task.FromResult(SupabaseConfig.AnonKey);

    public Task<string> RefreshAsync(CancellationToken ct = default)
        => Task.FromResult(SupabaseConfig.AnonKey);
}
