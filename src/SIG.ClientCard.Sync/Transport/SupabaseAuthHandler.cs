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
        var token = await tokens.GetAccessTokenAsync(cancellationToken);
        Decorate(request, token);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        var refreshed = await tokens.RefreshAsync(cancellationToken);
        Decorate(request, refreshed);
        return await base.SendAsync(request, cancellationToken);
    }

    private void Decorate(HttpRequestMessage request, string token)
    {
        request.Headers.Remove("apikey");
        request.Headers.Add("apikey", anonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
