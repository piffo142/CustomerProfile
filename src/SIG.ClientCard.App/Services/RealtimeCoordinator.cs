using SIG.ClientCard.Sync;
using SIG.ClientCard.Sync.Transport;

namespace SIG.ClientCard.App.Services;

/// <summary>
/// Starts the Realtime signal while a session is active and stops it on sign
/// out. A signal only ever triggers a normal debounced pull — payloads are
/// never applied directly, preserving the cursor's ordering guarantees.
/// </summary>
public sealed class RealtimeCoordinator : IAsyncDisposable
{
    private readonly RealtimeSyncSignal _signal;
    private readonly AuthService _auth;

    public RealtimeCoordinator(AuthService auth, TenantContext tenant, SyncScheduler scheduler)
    {
        _auth = auth;
        _signal = new RealtimeSyncSignal(
            new Uri(SupabaseConfig.Url),
            SupabaseConfig.AnonKey,
            auth.GetAccessTokenAsync,
            () => tenant.SalonId);

        _signal.ChangesSignaled += (_, _) => scheduler.RequestSync();
        _auth.AuthStateChanged += (_, _) => Update();
        Update();
    }

    private void Update()
    {
        if (_auth.IsSignedIn)
        {
            _signal.Start();
        }
        else
        {
            _ = _signal.StopAsync();
        }
    }

    public async ValueTask DisposeAsync() => await _signal.DisposeAsync();
}
