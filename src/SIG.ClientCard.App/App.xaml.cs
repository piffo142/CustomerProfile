using SIG.ClientCard.App.Services;
using SIG.ClientCard.Sync;

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
        }
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new(_services.GetRequiredService<AppShell>());

    protected override void OnResume()
    {
        base.OnResume();
        if (SupabaseConfig.IsConfigured && Connectivity.NetworkAccess == NetworkAccess.Internet)
        {
            _ = _scheduler.SyncNowAsync();
        }
    }
}
