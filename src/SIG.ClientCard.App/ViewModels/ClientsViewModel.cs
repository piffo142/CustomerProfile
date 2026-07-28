using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIG.ClientCard.Core.Abstractions;
using SIG.ClientCard.Core.Entities;

namespace SIG.ClientCard.App.ViewModels;

public partial class ClientsViewModel(IClientRepository clients) : ObservableObject
{
    private const int PageSize = 50;

    public ObservableCollection<Client> Items { get; } = [];

    [ObservableProperty]
    private string _searchTerm = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isEmpty;

    partial void OnSearchTermChanged(string value) => _ = ReloadAsync();

    [RelayCommand]
    public async Task ReloadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            // Server-side filtering is irrelevant; SQLite answers instantly.
            var page = await clients.SearchAsync(SearchTerm, 0, PageSize);
            Items.Clear();
            foreach (var client in page)
            {
                Items.Add(client);
            }

            IsEmpty = Items.Count == 0;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Incremental load driven by RemainingItemsThreshold.</summary>
    [RelayCommand]
    public async Task LoadMoreAsync()
    {
        if (IsBusy || Items.Count < PageSize)
        {
            return;
        }

        var page = await clients.SearchAsync(SearchTerm, Items.Count, PageSize);
        foreach (var client in page)
        {
            Items.Add(client);
        }
    }

    [RelayCommand]
    private Task AddClientAsync() => Shell.Current.GoToAsync("clientedit");

    [RelayCommand]
    private Task OpenClientAsync(Client client)
        => Shell.Current.GoToAsync($"clientdetail?clientId={client.Id:D}");
}
