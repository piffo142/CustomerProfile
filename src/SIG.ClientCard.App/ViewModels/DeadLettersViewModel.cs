using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIG.ClientCard.App.Services;
using SIG.ClientCard.Data.Entities;
using SIG.ClientCard.Sync;

namespace SIG.ClientCard.App.ViewModels;

/// <summary>
/// Parked sync ops: changes the server rejected. Each can be retried (after a
/// server-side fix) or discarded. The underlying local record is untouched
/// either way — discarding only drops the failed sync attempt.
/// </summary>
public partial class DeadLettersViewModel(SyncEngine engine, SyncScheduler scheduler) : ObservableObject
{
    public ObservableCollection<SyncDeadLetterEntry> Items { get; } = [];

    [ObservableProperty]
    private bool _isEmpty = true;

    [RelayCommand]
    public async Task LoadAsync()
    {
        Items.Clear();
        foreach (var entry in await engine.GetDeadLettersAsync())
        {
            Items.Add(entry);
        }

        IsEmpty = Items.Count == 0;
    }

    [RelayCommand]
    private async Task RetryAsync(SyncDeadLetterEntry entry)
    {
        await engine.RequeueDeadLetterAsync(entry.OpId);
        await LoadAsync();
        if (SupabaseConfig.IsConfigured)
        {
            scheduler.RequestSync();
        }
    }

    [RelayCommand]
    private async Task DiscardAsync(SyncDeadLetterEntry entry)
    {
        var confirmed = await Shell.Current.DisplayAlert(
            "Discard change",
            "This failed change will never be sent to the server. The record on this device is not affected. Discard?",
            "Discard", "Cancel");
        if (!confirmed)
        {
            return;
        }

        await engine.DiscardDeadLetterAsync(entry.OpId);
        await LoadAsync();
    }
}
