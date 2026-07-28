using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIG.ClientCard.App.Services;
using SIG.ClientCard.Core.Abstractions;
using SIG.ClientCard.Core.Entities;
using SIG.ClientCard.Core.Enums;
using SIG.ClientCard.Sync;

namespace SIG.ClientCard.App.ViewModels;

[QueryProperty(nameof(ClientId), "clientId")]
public partial class ClientEditViewModel(
    IClientRepository clients,
    SyncScheduler scheduler) : ObservableObject
{
    private Client _client = new();

    [ObservableProperty]
    private string _clientId = "";

    [ObservableProperty]
    private string _title = "New client";

    [ObservableProperty]
    private string _lastName = "";

    [ObservableProperty]
    private string _firstName = "";

    [ObservableProperty]
    private string _address = "";

    [ObservableProperty]
    private string _phone = "";

    [ObservableProperty]
    private string _email = "";

    public IReadOnlyList<string> AcquisitionOptions { get; } =
        ["Unknown", "Referral", "Location", "Other"];

    [ObservableProperty]
    private int _acquisitionIndex;

    [ObservableProperty]
    private bool _acquisitionDetailVisible;

    [ObservableProperty]
    private string _acquisitionDetail = "";

    [ObservableProperty]
    private bool _hasDateOfBirth;

    [ObservableProperty]
    private DateTime _dateOfBirth = new(1990, 1, 1);

    [ObservableProperty]
    private bool _medicalFlag;

    [ObservableProperty]
    private string _gpDetails = "";

    [ObservableProperty]
    private bool _hasPatchTest;

    [ObservableProperty]
    private DateTime _patchTestOn = DateTime.Today;

    public IReadOnlyList<string> PatchTestOptions { get; } = ["Not tested", "Pass", "Fail"];

    [ObservableProperty]
    private int _patchTestIndex;

    [ObservableProperty]
    private string _validationError = "";

    partial void OnAcquisitionIndexChanged(int value)
        => AcquisitionDetailVisible = (AcquisitionSource)value
            is AcquisitionSource.Referral or AcquisitionSource.Other;

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (!Guid.TryParse(ClientId, out var id))
        {
            return;
        }

        var existing = await clients.GetAsync(id);
        if (existing is null)
        {
            return;
        }

        _client = existing;
        Title = existing.DisplayName;
        LastName = existing.LastName;
        FirstName = existing.FirstName;
        Address = existing.Address;
        Phone = existing.PhoneRaw.Length > 0 ? existing.PhoneRaw : existing.Phone;
        Email = existing.Email ?? "";
        AcquisitionIndex = (int)existing.AcquisitionSource;
        AcquisitionDetail = existing.AcquisitionDetail ?? "";
        HasDateOfBirth = existing.DateOfBirth is not null;
        if (existing.DateOfBirth is { } dob)
        {
            DateOfBirth = dob.ToDateTime(TimeOnly.MinValue);
        }

        MedicalFlag = existing.MedicalFlag;
        GpDetails = existing.GpDetails ?? "";
        HasPatchTest = existing.PatchTestOn is not null;
        if (existing.PatchTestOn is { } patch)
        {
            PatchTestOn = patch.ToDateTime(TimeOnly.MinValue);
        }

        PatchTestIndex = (int)existing.PatchTestResult;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(LastName) && string.IsNullOrWhiteSpace(FirstName))
        {
            ValidationError = "A name is required.";
            return;
        }

        _client.LastName = LastName.Trim();
        _client.FirstName = FirstName.Trim();
        _client.Address = Address.Trim();
        _client.PhoneRaw = Phone.Trim();
        _client.Email = string.IsNullOrWhiteSpace(Email) ? null : Email.Trim();
        _client.AcquisitionSource = (AcquisitionSource)AcquisitionIndex;
        _client.AcquisitionDetail = AcquisitionDetailVisible && !string.IsNullOrWhiteSpace(AcquisitionDetail)
            ? AcquisitionDetail.Trim()
            : null;
        _client.DateOfBirth = HasDateOfBirth ? DateOnly.FromDateTime(DateOfBirth) : null;
        _client.MedicalFlag = MedicalFlag;
        _client.GpDetails = string.IsNullOrWhiteSpace(GpDetails) ? null : GpDetails.Trim();
        _client.PatchTestOn = HasPatchTest ? DateOnly.FromDateTime(PatchTestOn) : null;
        _client.PatchTestResult = HasPatchTest ? (PatchTestResult)PatchTestIndex : PatchTestResult.NotTested;

        await clients.UpsertAsync(_client);

        if (SupabaseConfig.IsConfigured)
        {
            scheduler.RequestSync();
        }

        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private Task CancelAsync() => Shell.Current.GoToAsync("..");
}
