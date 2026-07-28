using SIG.ClientCard.Core.Abstractions;

namespace SIG.ClientCard.App.Services;

/// <summary>
/// Phase 0: a fixed local salon id. Phase 1 replaces this with the salon id
/// from the Supabase auth session.
/// </summary>
public sealed class LocalTenantContext : ITenantContext
{
    private const string KeyName = "clientcard_salon_id";

    public LocalTenantContext()
    {
        var stored = Preferences.Default.Get<string?>(KeyName, null);
        if (stored is null || !Guid.TryParse(stored, out var id))
        {
            id = Guid.CreateVersion7();
            Preferences.Default.Set(KeyName, id.ToString("D"));
        }

        SalonId = id;
    }

    public Guid SalonId { get; }
}
