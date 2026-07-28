using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SIG.ClientCard.Sync.Transport;

/// <summary>
/// Supabase Realtime as a *signal*, never a transport: any postgres_changes
/// event for the salon's rows triggers an immediate pull through the normal
/// RPC path, which keeps the cursor's ordering guarantees. Payloads are
/// deliberately not applied directly.
///
/// Best-effort by design: on any failure it reconnects with backoff, and the
/// 15-minute poll remains the safety net.
/// </summary>
public sealed class RealtimeSyncSignal(
    Uri projectUrl,
    string anonKey,
    Func<CancellationToken, Task<string>> accessTokenProvider,
    Func<Guid> salonIdProvider) : IAsyncDisposable
{
    private static readonly string[] WatchedTables =
        ["client", "service_catalog", "service_record", "client_note", "client_consent"];

    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>Raised (from a background thread) whenever the server reports a relevant change.</summary>
    public event EventHandler? ChangesSignaled;

    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync();
        try
        {
            if (_loop is not null)
            {
                await _loop;
            }
        }
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
        _cts = null;
        _loop = null;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndListenAsync(ct);
                attempt = 0; // clean disconnect: retry promptly
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                attempt++;
            }

            var backoff = TimeSpan.FromSeconds(Math.Min(60, 2 * Math.Pow(2, Math.Min(attempt, 5))));
            try
            {
                await Task.Delay(backoff, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ConnectAndListenAsync(CancellationToken ct)
    {
        var wsUri = new UriBuilder(projectUrl)
        {
            Scheme = "wss",
            Path = "/realtime/v1/websocket",
            Query = $"apikey={anonKey}&vsn=1.0.0",
        }.Uri;

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(wsUri, ct);

        var accessToken = await accessTokenProvider(ct);
        var salonId = salonIdProvider().ToString("D");

        // Join one channel carrying postgres_changes subscriptions for every
        // synced table, filtered to this salon.
        var changes = new JsonArray();
        foreach (var table in WatchedTables)
        {
            changes.Add(new JsonObject
            {
                ["event"] = "*",
                ["schema"] = "public",
                ["table"] = table,
                ["filter"] = $"salon_id=eq.{salonId}",
            });
        }

        var join = new JsonObject
        {
            ["topic"] = "realtime:sync",
            ["event"] = "phx_join",
            ["ref"] = "1",
            ["payload"] = new JsonObject
            {
                ["access_token"] = accessToken,
                ["config"] = new JsonObject
                {
                    ["postgres_changes"] = changes,
                },
            },
        };
        await SendAsync(socket, join.ToJsonString(), ct);

        var heartbeatRef = 2;
        using var heartbeat = new PeriodicTimer(TimeSpan.FromSeconds(25));
        var heartbeatTask = Task.Run(async () =>
        {
            while (await heartbeat.WaitForNextTickAsync(ct))
            {
                var beat = new JsonObject
                {
                    ["topic"] = "phoenix",
                    ["event"] = "heartbeat",
                    ["payload"] = new JsonObject(),
                    ["ref"] = (heartbeatRef++).ToString(),
                };
                await SendAsync(socket, beat.ToJsonString(), ct);
            }
        }, ct);

        var buffer = new byte[16 * 1024];
        var message = new StringBuilder();
        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            message.Clear();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);

            HandleMessage(message.ToString());
        }

        await heartbeatTask;
    }

    private void HandleMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var ev = doc.RootElement.TryGetProperty("event", out var e) ? e.GetString() : null;
            if (ev == "postgres_changes")
            {
                ChangesSignaled?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (JsonException)
        {
            // Malformed frame: ignore; the poll is the safety net.
        }
    }

    private static Task SendAsync(ClientWebSocket socket, string json, CancellationToken ct)
        => socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, ct);

    public async ValueTask DisposeAsync() => await StopAsync();
}
