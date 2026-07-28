using SIG.ClientCard.App.ViewModels;

namespace SIG.ClientCard.App.Views;

public partial class ClientDetailPage : ContentPage
{
    private readonly ClientDetailViewModel _viewModel;

    public ClientDetailPage(ClientDetailViewModel viewModel)
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
