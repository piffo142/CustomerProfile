namespace SIG.ClientCard.Sync.Attachments;

/// <summary>
/// Local attachment files (photos, signatures). The local file is the source
/// of truth for the UI; upload is background reconciliation, like row sync.
/// </summary>
public interface IAttachmentStore
{
    /// <summary>Bytes of a stored attachment, or null when the file is missing.</summary>
    Task<byte[]?> ReadAsync(string relativePath, CancellationToken ct = default);
}
