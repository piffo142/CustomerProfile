using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIG.ClientCard.App.Services;
using SIG.ClientCard.Core.Abstractions;
using SIG.ClientCard.Core.Entities;
using SIG.ClientCard.Core.Enums;
using SIG.ClientCard.Sync;

namespace SIG.ClientCard.App.ViewModels;

[QueryProperty(nameof(ClientId), "clientId")]
public partial class ClientDetailViewModel(
    IClientRepository clients,
    SyncScheduler scheduler) : ObservableObject
{
    [ObservableProperty]
    private string _clientId = "";

    [ObservableProperty]
    private Client? _client;

    [ObservableProperty]
    private string _acquisitionText = "";

    [ObservableProperty]
    private string _patchTestText = "";

    public ObservableCollection<ServiceRecord> Services { get; } = [];
    public ObservableCollection<ClientNote> Notes { get; } = [];

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (!Guid.TryParse(ClientId, out var id))
        {
            return;
        }

        var client = await clients.GetWithDetailAsync(id);
        if (client is null)
        {
            await Shell.Current.GoToAsync("..");
            return;
        }

        Client = client;
        AcquisitionText = client.AcquisitionSource switch
        {
            AcquisitionSource.Referral => $"Referred by {client.AcquisitionDetail}",
            AcquisitionSource.Location => "Found us by location",
            AcquisitionSource.Other => $"Other: {client.AcquisitionDetail}",
            _ => "Source unknown",
        };
        PatchTestText = client.PatchTestResult switch
        {
            PatchTestResult.NotTested => "Patch test: not recorded",
            var result => $"Patch test: {result} ({client.PatchTestOn:d MMM yyyy})",
        };

        Services.Clear();
        foreach (var service in client.Services)
        {
            Services.Add(service);
        }

        Notes.Clear();
        foreach (var note in client.Notes)
        {
            Notes.Add(note);
        }
    }

    [RelayCommand]
    private Task EditAsync() => Shell.Current.GoToAsync($"clientedit?clientId={ClientId}");

    [RelayCommand]
    private Task AddServiceAsync() => Shell.Current.GoToAsync($"addservice?clientId={ClientId}");

    [RelayCommand]
    private Task AddNoteAsync() => Shell.Current.GoToAsync($"addnote?clientId={ClientId}");

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Client is null)
        {
            return;
        }

        var confirmed = await Shell.Current.DisplayAlert(
            "Delete client",
            $"Delete {Client.DisplayName}? The record is removed from every device once synced.",
            "Delete", "Cancel");
        if (!confirmed)
        {
            return;
        }

        await clients.SoftDeleteAsync(Client.Id);
        RequestSync();
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task EraseAsync()
    {
        if (Client is null)
        {
            return;
        }

        var confirmed = await Shell.Current.DisplayAlert(
            "Erase permanently (GDPR)",
            $"Permanently erase {Client.DisplayName}, including all notes, services and consents? " +
            "This is irreversible and propagates to all devices.",
            "Erase", "Cancel");
        if (!confirmed)
        {
            return;
        }

        await clients.EraseAsync(Client.Id);
        RequestSync();
        await Shell.Current.GoToAsync("..");
    }

    private void RequestSync()
    {
        if (SupabaseConfig.IsConfigured)
        {
            scheduler.RequestSync();
        }
    }
}
