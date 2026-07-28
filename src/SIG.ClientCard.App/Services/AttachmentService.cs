using Microsoft.EntityFrameworkCore;
using SIG.ClientCard.Core.Abstractions;
using SIG.ClientCard.Data;
using SIG.ClientCard.Data.Entities;
using SIG.ClientCard.Sync.Attachments;

namespace SIG.ClientCard.App.Services;

/// <summary>
/// Local-first attachments: the file lands in app storage (under the SQLCipher
/// key's protection boundary of the device), a queue row is written, and the
/// uploader pushes it to Supabase Storage in the background. The stored
/// relative path doubles as the bucket object key (salon-id prefixed, which
/// the storage RLS policies rely on).
/// </summary>
public sealed class AttachmentService(
    IDbContextFactory<ClientCardContext> dbFactory,
    ITenantContext tenant,
    IClock clock) : IAttachmentStore
{
    public static string Root => Path.Combine(FileSystem.AppDataDirectory, "attachments");

    public static string GetAbsolutePath(string relativePath)
        => Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public async Task<string> SaveAsync(Stream content, string extension, string contentType, CancellationToken ct = default)
    {
        var relative = $"{tenant.SalonId:D}/{Guid.CreateVersion7():D}{extension}";
        var absolute = GetAbsolutePath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);

        await using (var file = File.Create(absolute))
        {
            await content.CopyToAsync(file, ct);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.AttachmentQueue.Add(new AttachmentQueueEntry
        {
            Id = Guid.CreateVersion7(),
            RelativePath = relative,
            ContentType = contentType,
            CreatedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        return relative;
    }

    public Task<string> SaveTextAsync(string content, string extension, string contentType, CancellationToken ct = default)
        => SaveAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)), extension, contentType, ct);

    public async Task<byte[]?> ReadAsync(string relativePath, CancellationToken ct = default)
    {
        var absolute = GetAbsolutePath(relativePath);
        if (!File.Exists(absolute))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(absolute, ct);
    }

    /// <summary>Capture a photo (camera first, library fallback) straight into the attachment store.</summary>
    public async Task<string?> CapturePhotoAsync(CancellationToken ct = default)
    {
        FileResult? photo = null;
        try
        {
            if (MediaPicker.Default.IsCaptureSupported)
            {
                photo = await MediaPicker.Default.CapturePhotoAsync();
            }
        }
        catch (Exception)
        {
            // Camera denied/unavailable: fall through to the picker.
        }

        photo ??= await MediaPicker.Default.PickPhotoAsync();
        if (photo is null)
        {
            return null;
        }

        await using var stream = await photo.OpenReadAsync();
        return await SaveAsync(stream, ".jpg", "image/jpeg", ct);
    }
}
