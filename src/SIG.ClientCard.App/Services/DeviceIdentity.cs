using SIG.ClientCard.Core.Abstractions;

namespace SIG.ClientCard.App.Services;

/// <summary>Stable per-install device id, minted on first run.</summary>
public sealed class DeviceIdentity : IDeviceIdentity
{
    private const string KeyName = "clientcard_device_id";

    public DeviceIdentity()
    {
        var stored = Preferences.Default.Get<string?>(KeyName, null);
        if (stored is null || !Guid.TryParse(stored, out var id))
        {
            id = Guid.CreateVersion7();
            Preferences.Default.Set(KeyName, id.ToString("D"));
        }

        DeviceId = id;
    }

    public Guid DeviceId { get; }
}
