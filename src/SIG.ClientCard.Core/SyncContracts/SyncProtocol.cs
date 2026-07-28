namespace SIG.ClientCard.Core.SyncContracts;

public static class SyncProtocol
{
    /// <summary>
    /// Sent with every push/pull. The server's sync_config.min_client_version
    /// gates it: clients below the minimum are refused with 'client_too_old'
    /// instead of being allowed to corrupt newer data with an old schema.
    /// Bump this when the wire format or schema changes incompatibly.
    /// </summary>
    public const int SchemaVersion = 1;
}
