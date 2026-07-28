using SIG.ClientCard.Data.Infrastructure;
using SIG.ClientCard.Sync.Transport;

namespace SIG.ClientCard.App.Services;

/// <summary>
/// Session management per the plan: refresh token in SecureStorage, access
/// token in memory only. The 401 → refresh → retry cycle is handled by
/// <see cref="SupabaseAuthHandler"/> calling <see cref="RefreshAsync"/>.
/// </summary>
public sealed class AuthService(
    SupabaseAuthClient authClient,
    TenantContext tenant,
    TenantMigrator migrator) : IAccessTokenProvider
{
    private const string RefreshTokenKey = "clientcard_sb_refresh";
    private static readonly TimeSpan ExpirySlack = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private SupabaseSession? _session;

    public bool IsSignedIn => _session is not null;
    public string? Email => _session?.Email;

    public event EventHandler? AuthStateChanged;

    /// <summary>Silent session restore on app start. False means show the login page.</summary>
    public async Task<bool> TryRestoreAsync(CancellationToken ct = default)
    {
        string? refreshToken;
        try
        {
            refreshToken = await SecureStorage.Default.GetAsync(RefreshTokenKey);
        }
        catch (Exception)
        {
            // Keystore unusable after a device-to-device restore: clear and re-auth.
            SecureStorage.Default.RemoveAll();
            return false;
        }

        if (string.IsNullOrEmpty(refreshToken))
        {
            return false;
        }

        try
        {
            await ApplySessionAsync(await authClient.RefreshSessionAsync(refreshToken, ct), ct);
            return true;
        }
        catch (SupabaseAuthException)
        {
            SecureStorage.Default.Remove(RefreshTokenKey);
            return false;
        }
        catch (Exception)
        {
            // Offline or server unreachable: stay signed out; local work continues.
            return false;
        }
    }

    public async Task SignInAsync(string email, string password, CancellationToken ct = default)
        => await ApplySessionAsync(await authClient.SignInWithPasswordAsync(email, password, ct), ct);

    public Task SignOutAsync()
    {
        _session = null;
        SecureStorage.Default.Remove(RefreshTokenKey);
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        var session = _session ?? throw new InvalidOperationException("Not signed in.");
        if (session.ExpiresAt > DateTimeOffset.UtcNow + ExpirySlack)
        {
            return session.AccessToken;
        }

        return await RefreshAsync(ct);
    }

    public async Task<string> RefreshAsync(CancellationToken ct = default)
    {
        await _refreshGate.WaitAsync(ct);
        try
        {
            var refreshToken = _session?.RefreshToken
                ?? throw new InvalidOperationException("Not signed in.");

            var session = await authClient.RefreshSessionAsync(refreshToken, ct);
            _session = session;
            await StoreRefreshTokenAsync(session.RefreshToken);
            return session.AccessToken;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task ApplySessionAsync(SupabaseSession session, CancellationToken ct)
    {
        _session = session;
        await StoreRefreshTokenAsync(session.RefreshToken);

        // Tenancy: the salon id comes from the auth session, not local state.
        var salonId = await authClient.GetSalonIdAsync(session.AccessToken, ct)
            ?? throw new SupabaseAuthException(
                "This account is not a member of any salon yet. Ask the owner to add you.");

        if (tenant.SalonId != salonId)
        {
            await migrator.AdoptSalonAsync(salonId, ct);
            tenant.Adopt(salonId);
        }

        AuthStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static async Task StoreRefreshTokenAsync(string refreshToken)
    {
        try
        {
            await SecureStorage.Default.SetAsync(RefreshTokenKey, refreshToken);
        }
        catch (Exception)
        {
            SecureStorage.Default.RemoveAll();
            await SecureStorage.Default.SetAsync(RefreshTokenKey, refreshToken);
        }
    }
}
