using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIG.ClientCard.App.Services;
using SIG.ClientCard.Sync;

namespace SIG.ClientCard.App.ViewModels;

public partial class SyncStatusViewModel : ObservableObject
{
    private readonly SyncEngine _engine;

    [ObservableProperty]
    private string _statusLine = "";

    [ObservableProperty]
    private string _detailLine = "";

    [ObservableProperty]
    private string _deadLetterLine = "";

    [ObservableProperty]
    private bool _hasDeadLetters;

    public SyncStatusViewModel(SyncEngine engine, SyncScheduler scheduler)
    {
        _engine = engine;
        scheduler.SyncCompleted += (_, _) => MainThread.BeginInvokeOnMainThread(async () => await RefreshAsync());
        scheduler.SyncFaulted += (_, _) => MainThread.BeginInvokeOnMainThread(async () => await RefreshAsync());
        _ = RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!SupabaseConfig.IsConfigured)
        {
            StatusLine = "Local only";
            DetailLine = "Sync not configured (Phase 0)";
            HasDeadLetters = false;
            return;
        }

        var status = await _engine.GetStatusAsync();
        StatusLine = status.PendingOps == 0 ? "All changes synced" : $"{status.PendingOps} change(s) waiting to sync";
        DetailLine = status.LastPullAt is { } t ? $"Last sync {t.ToLocalTime():HH:mm, d MMM}" : "Not synced yet";
        HasDeadLetters = status.DeadLetteredOps > 0;
        DeadLetterLine = HasDeadLetters ? $"{status.DeadLetteredOps} change(s) need attention" : "";
    }
}
