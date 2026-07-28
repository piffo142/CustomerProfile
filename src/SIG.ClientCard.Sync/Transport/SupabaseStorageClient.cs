namespace SIG.ClientCard.Sync.Transport;

/// <summary>
/// Upload path for attachments: Supabase Storage REST. The object key equals
/// the local relative path (salon-id prefixed), which the bucket RLS policies
/// use for tenant scoping.
/// </summary>
public sealed class SupabaseStorageClient(HttpClient http, string bucket = "attachments")
{
    public async Task UploadAsync(string relativePath, byte[] content, string contentType, CancellationToken ct = default)
    {
        using var body = new ByteArrayContent(content);
        body.Headers.TryAddWithoutValidation("Content-Type", contentType);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"storage/v1/object/{bucket}/{relativePath}")
        {
            Content = body,
        };
        request.Headers.Add("x-upsert", "true"); // idempotent re-upload

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new SyncTransportException($"storage upload unreachable: {relativePath}", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                throw new SyncTransportException(
                    $"storage upload {relativePath} -> {(int)response.StatusCode}: {detail}");
            }
        }
    }
}
