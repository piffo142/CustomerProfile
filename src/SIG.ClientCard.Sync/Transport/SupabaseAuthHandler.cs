using System.Net;
using System.Net.Http.Headers;

namespace SIG.ClientCard.Sync.Transport;

/// <summary>
/// Handles the 401 → refresh → retry cycle so no call site deals with it.
/// </summary>
public sealed class SupabaseAuthHandler(IAccessTokenProvider tokens, string anonKey) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // An HttpRequestMessage cannot be sent twice, so buffer the content up
        // front and retry with a clone after a refresh.
        byte[]? body = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        var token = await tokens.GetAccessTokenAsync(cancellationToken);
        Decorate(request, token);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        var refreshed = await tokens.RefreshAsync(cancellationToken);
        using var retry = Clone(request, body);
        Decorate(retry, refreshed);
        return await base.SendAsync(retry, cancellationToken);
    }

    private static HttpRequestMessage Clone(HttpRequestMessage request, byte[]? body)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (body is not null && request.Content is not null)
        {
            var content = new ByteArrayContent(body);
            foreach (var header in request.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Content = content;
        }

        return clone;
    }

    private void Decorate(HttpRequestMessage request, string token)
    {
        request.Headers.Remove("apikey");
        request.Headers.Add("apikey", anonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
