using System.Net;
using System.Text;
using System.Text.Json;
using SIG.ClientCard.Core.SyncContracts;

namespace SIG.ClientCard.Sync.Transport;

public sealed record SupabaseSession(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    Guid UserId,
    string? Email);

/// <summary>The server rejected the credentials or session — not a transient failure.</summary>
public sealed class SupabaseAuthException(string message) : Exception(message);

/// <summary>
/// Minimal GoTrue + PostgREST client for Phase 1: password sign-in, refresh
/// token rotation, and the salon-membership lookup. No MAUI dependency, so it
/// is testable and reusable outside the app.
/// </summary>
public sealed class SupabaseAuthClient(HttpClient http, string anonKey)
{
    public Task<SupabaseSession> SignInWithPasswordAsync(string email, string password, CancellationToken ct = default)
        => TokenRequestAsync("password", JsonSerializer.Serialize(new { email, password }), ct);

    public Task<SupabaseSession> RefreshSessionAsync(string refreshToken, CancellationToken ct = default)
        => TokenRequestAsync("refresh_token", JsonSerializer.Serialize(new { refresh_token = refreshToken }), ct);

    /// <summary>
    /// The salon this user belongs to, via the RLS-guarded salon_member table.
    /// Null when the user has no membership yet.
    /// </summary>
    public async Task<Guid?> GetSalonIdAsync(string accessToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, "rest/v1/salon_member?select=salon_id&limit=1");
        request.Headers.Add("apikey", anonKey);
        request.Headers.Authorization = new("Bearer", accessToken);

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new SupabaseAuthException($"salon_member lookup failed ({(int)response.StatusCode}): {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var rows = doc.RootElement;
        if (rows.GetArrayLength() == 0)
        {
            return null;
        }

        return rows[0].GetProperty("salon_id").GetGuid();
    }

    private async Task<SupabaseSession> TokenRequestAsync(string grantType, string body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"auth/v1/token?grant_type={grantType}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("apikey", anonKey);

        using var response = await http.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (response.StatusCode is HttpStatusCode.BadRequest
            or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new SupabaseAuthException(ExtractErrorMessage(json));
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new SyncTransportException($"auth/token -> {(int)response.StatusCode}: {json}");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var user = root.GetProperty("user");

        return new SupabaseSession(
            AccessToken: root.GetProperty("access_token").GetString()!,
            RefreshToken: root.GetProperty("refresh_token").GetString()!,
            ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()),
            UserId: user.GetProperty("id").GetGuid(),
            Email: user.TryGetProperty("email", out var email) ? email.GetString() : null);
    }

    private static string ExtractErrorMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var key in new[] { "error_description", "msg", "message", "error" })
            {
                if (doc.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString()!;
                }
            }
        }
        catch (JsonException)
        {
        }

        return "Sign-in failed.";
    }
}
