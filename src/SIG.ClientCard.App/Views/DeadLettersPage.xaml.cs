using SIG.ClientCard.App.ViewModels;

namespace SIG.ClientCard.App.Views;

public partial class DeadLettersPage : ContentPage
{
    private readonly DeadLettersViewModel _viewModel;

    public DeadLettersPage(DeadLettersViewModel viewModel)
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
