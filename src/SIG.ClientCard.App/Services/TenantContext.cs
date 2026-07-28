using SIG.ClientCard.Core.Abstractions;

namespace SIG.ClientCard.App.Services;

/// <summary>
/// The current salon id. Before sign-in (or in Phase 0 local-only mode) it is
/// a locally-minted placeholder; the first successful sign-in adopts the real
/// tenant id from the server (after <c>TenantMigrator</c> re-stamps local data).
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private const string KeyName = "clientcard_salon_id";
    private Guid _salonId;

    public TenantContext()
    {
        var stored = Preferences.Default.Get<string?>(KeyName, null);
        if (stored is null || !Guid.TryParse(stored, out _salonId))
        {
            _salonId = Guid.CreateVersion7();
            Preferences.Default.Set(KeyName, _salonId.ToString("D"));
        }
    }

    public Guid SalonId => _salonId;

    public void Adopt(Guid salonId)
    {
        if (salonId == _salonId)
        {
            return;
        }

        _salonId = salonId;
        Preferences.Default.Set(KeyName, salonId.ToString("D"));
    }
}
