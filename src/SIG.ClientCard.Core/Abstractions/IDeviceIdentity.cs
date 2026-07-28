namespace SIG.ClientCard.Core.Abstractions;

/// <summary>
/// Stable per-install device id, stamped into <c>updated_by_device</c> on every
/// write. Used for echo suppression, audit, and the deterministic LWW tiebreak.
/// </summary>
public interface IDeviceIdentity
{
    Guid DeviceId { get; }
}
