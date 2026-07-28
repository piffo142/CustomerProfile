using SIG.ClientCard.App.ViewModels;

namespace SIG.ClientCard.App.Views;

public partial class ConsentsPage : ContentPage
{
    private readonly ConsentsViewModel _viewModel;

    public ConsentsPage(ConsentsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnSaveConsentClicked(object? sender, EventArgs e)
    {
        // Signature is optional — a paper form can be photographed instead.
        var svg = Pad.HasInk ? Pad.ToSvg() : null;
        await _viewModel.CompleteGrantAsync(svg);
        Pad.Clear();
    }

    private void OnClearClicked(object? sender, EventArgs e) => Pad.Clear();
}
