using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIG.ClientCard.App.Services;
using SIG.ClientCard.Sync;

namespace SIG.ClientCard.App.ViewModels;

public partial class SyncStatusViewModel : ObservableObject
{
    private readonly SyncEngine _engine;
    private readonly AuthService? _auth;

    [ObservableProperty]
    private string _statusLine = "";

    [ObservableProperty]
    private string _detailLine = "";

    [ObservableProperty]
    private string _deadLetterLine = "";

    [ObservableProperty]
    private bool _hasDeadLetters;

    [ObservableProperty]
    private string _accountLine = "";

    [ObservableProperty]
    private bool _isSignedIn;

    [ObservableProperty]
    private bool _showSignIn;

    public SyncStatusViewModel(SyncEngine engine, SyncScheduler scheduler, IServiceProvider services)
    {
        _engine = engine;
        _auth = SupabaseConfig.IsConfigured ? services.GetRequiredService<AuthService>() : null;

        scheduler.SyncCompleted += (_, _) => MainThread.BeginInvokeOnMainThread(async () => await RefreshAsync());
        scheduler.SyncFaulted += (_, _) => MainThread.BeginInvokeOnMainThread(async () => await RefreshAsync());
        if (_auth is not null)
        {
            _auth.AuthStateChanged += (_, _) => MainThread.BeginInvokeOnMainThread(async () => await RefreshAsync());
        }

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
            IsSignedIn = false;
            ShowSignIn = false;
            AccountLine = "";
            return;
        }

        IsSignedIn = _auth!.IsSignedIn;
        ShowSignIn = !IsSignedIn;
        AccountLine = IsSignedIn ? _auth.Email ?? "Signed in" : "Not signed in — working offline";

        var status = await _engine.GetStatusAsync();
        StatusLine = status.PendingOps == 0 ? "All changes synced" : $"{status.PendingOps} change(s) waiting to sync";
        DetailLine = status.LastPullAt is { } t ? $"Last sync {t.ToLocalTime():HH:mm, d MMM}" : "Not synced yet";
        HasDeadLetters = status.DeadLetteredOps > 0;
        DeadLetterLine = HasDeadLetters ? $"{status.DeadLetteredOps} change(s) need attention" : "";
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        if (_auth is not null)
        {
            await _auth.SignOutAsync();
            Shell.Current.FlyoutIsPresented = false;
            await Shell.Current.GoToAsync("//login");
        }
    }

    [RelayCommand]
    private async Task GoToSignInAsync()
    {
        Shell.Current.FlyoutIsPresented = false;
        await Shell.Current.GoToAsync("//login");
    }
}
