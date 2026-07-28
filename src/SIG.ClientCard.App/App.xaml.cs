using SIG.ClientCard.App.Services;
using SIG.ClientCard.Sync;
using SIG.ClientCard.Sync.Attachments;

namespace SIG.ClientCard.App;

public partial class App : Application
{
    private readonly IServiceProvider _services;
    private readonly SyncScheduler _scheduler;

    public App(IServiceProvider services, SyncScheduler scheduler)
    {
        InitializeComponent();
        _services = services;
        _scheduler = scheduler;

        if (SupabaseConfig.IsConfigured)
        {
            // 15-minute foreground poll. Background sync is deliberately out of
            // scope for v1 — the salon device is in near-constant foreground use.
            _scheduler.Start();

            Connectivity.ConnectivityChanged += (_, e) =>
            {
                if (e.NetworkAccess == NetworkAccess.Internet)
                {
                    _ = _scheduler.SyncNowAsync();
                }
            };

            // Photos/signatures drain to Supabase Storage after each row sync.
            var uploader = services.GetRequiredService<AttachmentUploader>();
            _scheduler.SyncCompleted += (_, _) => _ = uploader.UploadPendingAsync();

            // Realtime signal: instant pull triggers while signed in.
            _ = services.GetRequiredService<RealtimeCoordinator>();
        }
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new(_services.GetRequiredService<AppShell>());

    protected override async void OnStart()
    {
        base.OnStart();
        if (!SupabaseConfig.IsConfigured)
        {
            return;
        }

        // Silent restore: refresh token from SecureStorage → session in memory.
        // Failure (first run, signed out, revoked, or restored device) shows the
        // login page; "Work offline" remains available there.
        var auth = _services.GetRequiredService<AuthService>();
        if (await auth.TryRestoreAsync())
        {
            _ = _scheduler.SyncNowAsync();
        }
        else
        {
            await Shell.Current.GoToAsync("//login");
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (SupabaseConfig.IsConfigured && Connectivity.NetworkAccess == NetworkAccess.Internet)
        {
            _ = _scheduler.SyncNowAsync();
        }
    }
}
