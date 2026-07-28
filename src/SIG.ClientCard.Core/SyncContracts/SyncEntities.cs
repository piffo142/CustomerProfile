namespace SIG.ClientCard.Core.SyncContracts;

/// <summary>
/// Entity names as they appear in sync ops, and the static push order.
/// Push order matters: client before service_record before client_note, or FK
/// constraints reject the batch. Encoded as an ordinal rather than relying on
/// outbox insertion order.
/// </summary>
public static class SyncEntities
{
    public const string Client = "client";
    public const string ServiceRecord = "service_record";
    public const string ClientNote = "client_note";
    public const string ClientConsent = "client_consent";

    public static int PushOrdinal(string entity) => entity switch
    {
        Client => 0,
        ServiceRecord => 1,
        ClientNote => 2,
        ClientConsent => 3,
        _ => int.MaxValue,
    };
}

public static class SyncOperations
{
    /// <summary>Insert-or-update, including tombstone (soft) deletes carried in the payload.</summary>
    public const string Upsert = "upsert";

    /// <summary>
    /// GDPR erasure: hard delete server-side plus a redaction_log row carrying
    /// only the id and timestamp, so peers can purge without retaining the payload.
    /// </summary>
    public const string Delete = "delete";
}
