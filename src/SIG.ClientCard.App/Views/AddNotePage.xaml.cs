using SIG.ClientCard.App.ViewModels;

namespace SIG.ClientCard.App.Views;

public partial class AddNotePage : ContentPage
{
    public AddNotePage(AddNoteViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
