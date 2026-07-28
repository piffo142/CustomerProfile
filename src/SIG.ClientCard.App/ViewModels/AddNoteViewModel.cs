using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIG.ClientCard.App.Services;
using SIG.ClientCard.Core.Abstractions;
using SIG.ClientCard.Core.Entities;
using SIG.ClientCard.Sync;

namespace SIG.ClientCard.App.ViewModels;

[QueryProperty(nameof(ClientId), "clientId")]
public partial class AddNoteViewModel(
    IClientNoteRepository notes,
    SyncScheduler scheduler) : ObservableObject
{
    [ObservableProperty]
    private string _clientId = "";

    [ObservableProperty]
    private string _body = "";

    [ObservableProperty]
    private string _validationError = "";

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!Guid.TryParse(ClientId, out var clientId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Body))
        {
            ValidationError = "Write the note first.";
            return;
        }

        // Notes are append-only: no edit, no delete. History survives sync.
        await notes.AddAsync(new ClientNote
        {
            ClientId = clientId,
            Body = Body.Trim(),
        });

        if (SupabaseConfig.IsConfigured)
        {
            scheduler.RequestSync();
        }

        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private Task CancelAsync() => Shell.Current.GoToAsync("..");
}
