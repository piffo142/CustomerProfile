using SIG.ClientCard.App.ViewModels;

namespace SIG.ClientCard.App.Views;

public partial class AddServicePage : ContentPage
{
    public AddServicePage(AddServiceViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
