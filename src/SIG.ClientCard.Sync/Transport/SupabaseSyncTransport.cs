using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIG.ClientCard.Core.SyncContracts;

namespace SIG.ClientCard.Sync.Transport;

/// <summary>
/// Talks to the Postgres RPCs (sync_push / sync_pull) via PostgREST. RPCs
/// rather than Edge Functions: whole batch in one transaction, no cold start,
/// conflict logic next to the data. RLS applies — the functions are
/// security invoker, deliberately.
/// </summary>
public sealed class SupabaseSyncTransport(HttpClient http) : ISyncTransport
{
    public async Task<SyncPushResult> PushAsync(IReadOnlyList<SyncOp> ops, CancellationToken ct = default)
    {
        var array = new JsonArray();
        foreach (var op in ops)
        {
            array.Add(new JsonObject
            {
                ["op_id"] = op.OpId.ToString("D"),
                ["entity"] = op.Entity,
                ["entity_id"] = op.EntityId.ToString("D"),
                ["operation"] = op.Operation,
                ["payload"] = JsonNode.Parse(op.PayloadJson),
            });
        }

        var body = new JsonObject
        {
            ["p_ops"] = array,
            ["p_client_version"] = SyncProtocol.SchemaVersion,
        };
        var json = await PostRpcAsync("sync_push", body.ToJsonString(), ct);

        var result = JsonSerializer.Deserialize<SyncPushResult>(json, SyncJson.Options);
        return result ?? new SyncPushResult();
    }

    public async Task<SyncPullBundle> PullAsync(long cursor, int limit, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["p_cursor"] = cursor,
            ["p_limit"] = limit,
            ["p_client_version"] = SyncProtocol.SchemaVersion,
        };
        var json = await PostRpcAsync("sync_pull", body.ToJsonString(), ct);

        var bundle = JsonSerializer.Deserialize<SyncPullBundle>(json, SyncJson.Options);
        return bundle ?? new SyncPullBundle();
    }

    public async Task<SyncChecksum> GetChecksumAsync(CancellationToken ct = default)
    {
        var json = await PostRpcAsync("sync_checksum", "{}", ct);
        var checksum = JsonSerializer.Deserialize<SyncChecksum>(json, SyncJson.Options);
        return checksum ?? new SyncChecksum();
    }

    private async Task<string> PostRpcAsync(string function, string body, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            response = await http.PostAsync($"rest/v1/rpc/{function}", content, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new SyncTransportException($"rpc/{function} unreachable", ex);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync(ct);
            }

            var detail = await response.Content.ReadAsStringAsync(ct);

            // Version handshake: the server refuses clients below its minimum
            // schema version. Not retryable; the app needs updating.
            if (detail.Contains("client_too_old", StringComparison.OrdinalIgnoreCase))
            {
                throw new SyncUpdateRequiredException(
                    "The server requires a newer app version before syncing can continue.");
            }

            // Everything else — 5xx, 429, malformed/unauthorized requests — is
            // surfaced as transport-level so ops are never silently parked;
            // per-op validation rejections come back in the RPC result instead.
            throw new SyncTransportException($"rpc/{function} -> {(int)response.StatusCode}: {detail}");
        }
    }
}
