using SIG.ClientCard.App.ViewModels;

namespace SIG.ClientCard.App.Views;

public partial class ClientEditPage : ContentPage
{
    private readonly ClientEditViewModel _viewModel;

    public ClientEditPage(ClientEditViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }
}
