namespace SIG.ClientCard.Data.Entities;

/// <summary>
/// Photos and signatures are written to local app storage first (the local
/// file is the source of truth for the UI) and queued here for upload to
/// Supabase Storage. Same at-least-once discipline as the sync outbox:
/// transport failures back off and retry forever.
/// </summary>
public class AttachmentQueueEntry
{
    public Guid Id { get; set; }

    /// <summary>Path relative to the local attachments root AND the storage bucket key.</summary>
    public string RelativePath { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UploadedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
}
