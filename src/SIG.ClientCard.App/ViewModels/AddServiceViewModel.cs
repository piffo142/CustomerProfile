using System.Collections.ObjectModel;
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
    IServiceCatalogRepository catalog,
    AttachmentService attachments,
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

    [ObservableProperty]
    private bool _hasPhoto;

    [ObservableProperty]
    private ServiceCatalogItem? _selectedCatalogItem;

    /// <summary>Learned automatically from past entries; free text remains the fallback.</summary>
    public ObservableCollection<ServiceCatalogItem> CatalogItems { get; } = [];

    private string? _photoPath;

    partial void OnSelectedCatalogItemChanged(ServiceCatalogItem? value)
    {
        if (value is not null)
        {
            ServiceDescription = value.Name;
            PriceText = value.DefaultPrice.ToString("0.00", CultureInfo.CurrentCulture);
        }
    }

    [RelayCommand]
    public async Task LoadCatalogAsync()
    {
        CatalogItems.Clear();
        foreach (var item in await catalog.GetAllAsync())
        {
            CatalogItems.Add(item);
        }
    }

    [RelayCommand]
    private async Task AddPhotoAsync()
    {
        _photoPath = await attachments.CapturePhotoAsync();
        HasPhoto = _photoPath is not null;
    }

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

        price = decimal.Round(price, 2);
        var description = ServiceDescription.Trim();

        // Learn the entry into the catalogue so the picker improves with use.
        var catalogItem = await catalog.LearnAsync(description, price);

        await services.UpsertAsync(new ServiceRecord
        {
            ClientId = clientId,
            PerformedOn = DateOnly.FromDateTime(PerformedOn),
            ServiceDescription = description,
            Price = price,
            ServiceCatalogId = catalogItem.Id,
            PhotoPath = _photoPath,
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
