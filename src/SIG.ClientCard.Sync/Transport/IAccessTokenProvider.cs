namespace SIG.ClientCard.Sync.Transport;

/// <summary>
/// Supplies the GoTrue access token. Refresh token lives in SecureStorage,
/// access token in memory only; refresh happens inside the provider.
/// </summary>
public interface IAccessTokenProvider
{
    /// <summary>Current access token, refreshing first if it is known to be stale.</summary>
    Task<string> GetAccessTokenAsync(CancellationToken ct = default);

    /// <summary>Force a refresh after a 401. Returns the new token.</summary>
    Task<string> RefreshAsync(CancellationToken ct = default);
}
