using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIG.ClientCard.App.Services;
using SIG.ClientCard.Core.Abstractions;
using SIG.ClientCard.Core.Entities;
using SIG.ClientCard.Sync;

namespace SIG.ClientCard.App.ViewModels;

[QueryProperty(nameof(ClientId), "clientId")]
public partial class AddServiceViewModel(
    IServiceRecordRepository services,
    SyncScheduler scheduler) : ObservableObject
{
    [ObservableProperty]
    private string _clientId = "";

    [ObservableProperty]
    private DateTime _performedOn = DateTime.Today;

    [ObservableProperty]
    private string _serviceDescription = "";

    [ObservableProperty]
    private string _priceText = "";

    [ObservableProperty]
    private string _validationError = "";

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!Guid.TryParse(ClientId, out var clientId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ServiceDescription))
        {
            ValidationError = "Describe the service.";
            return;
        }

        if (!decimal.TryParse(PriceText, NumberStyles.Currency, CultureInfo.CurrentCulture, out var price)
            || price < 0)
        {
            ValidationError = "Enter a valid price.";
            return;
        }

        await services.UpsertAsync(new ServiceRecord
        {
            ClientId = clientId,
            PerformedOn = DateOnly.FromDateTime(PerformedOn),
            ServiceDescription = ServiceDescription.Trim(),
            Price = decimal.Round(price, 2),
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
