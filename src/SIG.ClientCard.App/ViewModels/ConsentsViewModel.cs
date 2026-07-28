using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIG.ClientCard.App.Services;
using SIG.ClientCard.Core.Abstractions;
using SIG.ClientCard.Core.Entities;
using SIG.ClientCard.Core.Enums;
using SIG.ClientCard.Sync;

namespace SIG.ClientCard.App.ViewModels;

public sealed partial class ConsentRow : ObservableObject
{
    public ConsentPurpose Purpose { get; init; }
    public string Label { get; init; } = "";

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string _statusText = "";
}

[QueryProperty(nameof(ClientId), "clientId")]
public partial class ConsentsViewModel(
    IClientConsentRepository consents,
    AttachmentService attachments,
    SyncScheduler scheduler) : ObservableObject
{
    [ObservableProperty]
    private string _clientId = "";

    [ObservableProperty]
    private ConsentRow? _granting;

    public ObservableCollection<ConsentRow> Rows { get; } = [];

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (!Guid.TryParse(ClientId, out var clientId))
        {
            return;
        }

        var existing = await consents.GetForClientAsync(clientId);

        Rows.Clear();
        foreach (var (purpose, label) in Purposes())
        {
            var active = existing.FirstOrDefault(c => c.Purpose == purpose && c.IsActive);
            var withdrawn = existing.Where(c => c.Purpose == purpose && !c.IsActive)
                .OrderByDescending(c => c.WithdrawnAt).FirstOrDefault();

            Rows.Add(new ConsentRow
            {
                Purpose = purpose,
                Label = label,
                IsActive = active is not null,
                StatusText = active is not null
                    ? $"Granted {active.GrantedAt.ToLocalTime():d MMM yyyy}"
                    : withdrawn is not null
                        ? $"Withdrawn {withdrawn.WithdrawnAt!.Value.ToLocalTime():d MMM yyyy}"
                        : "Not recorded",
            });
        }
    }

    [RelayCommand]
    private void BeginGrant(ConsentRow row) => Granting = row;

    [RelayCommand]
    private void CancelGrant() => Granting = null;

    /// <summary>Called by the page with the signature SVG (or null when signed on paper).</summary>
    public async Task CompleteGrantAsync(string? signatureSvg)
    {
        if (Granting is null || !Guid.TryParse(ClientId, out var clientId))
        {
            return;
        }

        string? blobRef = null;
        if (signatureSvg is not null)
        {
            blobRef = await attachments.SaveTextAsync(signatureSvg, ".svg", "image/svg+xml");
        }

        await consents.GrantAsync(new ClientConsent
        {
            ClientId = clientId,
            Purpose = Granting.Purpose,
            SignatureBlobRef = blobRef,
        });

        Granting = null;
        RequestSync();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task WithdrawAsync(ConsentRow row)
    {
        if (!Guid.TryParse(ClientId, out var clientId))
        {
            return;
        }

        var confirmed = await Shell.Current.DisplayAlert(
            "Withdraw consent",
            $"Withdraw \"{row.Label}\"? Withdrawal takes precedence on every device and cannot be un-done (a new consent must be captured).",
            "Withdraw", "Cancel");
        if (!confirmed)
        {
            return;
        }

        await consents.WithdrawAsync(clientId, row.Purpose);
        RequestSync();
        await LoadAsync();
    }

    private void RequestSync()
    {
        if (SupabaseConfig.IsConfigured)
        {
            scheduler.RequestSync();
        }
    }

    private static IEnumerable<(ConsentPurpose, string)> Purposes() =>
    [
        (ConsentPurpose.RecordKeeping, "Holding this client record"),
        (ConsentPurpose.SpecialCategoryData, "Health notes (allergies, conditions)"),
        (ConsentPurpose.Marketing, "Marketing messages"),
        (ConsentPurpose.Photography, "Before/after photography"),
    ];
}
