using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIG.ClientCard.Data;

namespace SIG.ClientCard.App.ViewModels;

public partial class ReportsViewModel(ReportService reports) : ObservableObject
{
    [ObservableProperty]
    private string _activeClientsText = "";

    [ObservableProperty]
    private string _revenueText = "";

    public ObservableCollection<MonthlyRevenue> Monthly { get; } = [];
    public ObservableCollection<ServicePopularity> TopServices { get; } = [];

    [RelayCommand]
    public async Task LoadAsync()
    {
        var data = await reports.GetAsync(DateOnly.FromDateTime(DateTime.Today));

        ActiveClientsText = $"{data.ActiveClients} active client(s)";
        RevenueText = $"£{data.RevenueLast12Months:N2} in the last 12 months";

        Monthly.Clear();
        foreach (var month in data.Monthly)
        {
            Monthly.Add(month);
        }

        TopServices.Clear();
        foreach (var service in data.TopServices)
        {
            TopServices.Add(service);
        }
    }
}
