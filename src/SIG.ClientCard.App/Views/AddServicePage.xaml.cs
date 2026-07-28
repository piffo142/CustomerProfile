using SIG.ClientCard.App.ViewModels;

namespace SIG.ClientCard.App.Views;

public partial class AddServicePage : ContentPage
{
    private readonly AddServiceViewModel _viewModel;

    public AddServicePage(AddServiceViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCatalogAsync();
    }
}
