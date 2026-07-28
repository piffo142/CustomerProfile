namespace SIG.ClientCard.Sync;

/// <summary>
/// Debounced, single-flight sync triggering. The app calls
/// <see cref="RequestSync"/> on outbox writes (debounced 2s), app resume and
/// connectivity restored; <see cref="Start"/> adds the 15-minute foreground
/// poll. Background (suspended-app) sync is deliberately out of scope for v1.
/// </summary>
public sealed class SyncScheduler(SyncEngine engine) : IAsyncDisposable
{
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan ForegroundInterval = TimeSpan.FromMinutes(15);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _disposal = new();
    private readonly object _debounceLock = new();
    private CancellationTokenSource? _debounceCts;
    private Task? _pollLoop;

    /// <summary>
    /// Optional predicate consulted before each run (e.g. signed-in + online).
    /// When it returns false the trigger is skipped silently.
    /// </summary>
    public Func<bool>? CanSync { get; set; }

    public event EventHandler? SyncCompleted;
    public event EventHandler<Exception>? SyncFaulted;

    public void Start()
    {
        _pollLoop ??= Task.Run(async () =>
        {
            while (!_disposal.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(ForegroundInterval, _disposal.Token);
                    await RunOnceAsync(_disposal.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        });
    }

    /// <summary>Debounced trigger; bursts of writes collapse into one sync.</summary>
    public void RequestSync()
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposal.Token);
        lock (_debounceLock)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = cts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Debounce, cts.Token);
                await RunOnceAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // superseded by a newer request or disposal
            }
            finally
            {
                // Whoever still owns the slot cleans it up; a superseded cts
                // was already disposed by the next RequestSync.
                lock (_debounceLock)
                {
                    if (ReferenceEquals(_debounceCts, cts))
                    {
                        _debounceCts = null;
                        cts.Dispose();
                    }
                }
            }
        });
    }

    /// <summary>Immediate trigger (app resume, connectivity restored, pull-to-refresh).</summary>
    public Task SyncNowAsync(CancellationToken ct = default) => RunOnceAsync(ct);

    private async Task RunOnceAsync(CancellationToken ct)
    {
        if (CanSync is { } gate && !gate())
        {
            return; // signed out or offline; the next trigger tries again
        }

        if (!await _gate.WaitAsync(0, ct))
        {
            return; // a sync is already in flight
        }

        try
        {
            await engine.SyncNowAsync(ct);
            SyncCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SyncFaulted?.Invoke(this, ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _disposal.CancelAsync();
        if (_pollLoop is not null)
        {
            try
            {
                await _pollLoop;
            }
            catch (OperationCanceledException)
            {
            }
        }

        lock (_debounceLock)
        {
            _debounceCts?.Dispose();
            _debounceCts = null;
        }

        _disposal.Dispose();
        _gate.Dispose();
    }
}
