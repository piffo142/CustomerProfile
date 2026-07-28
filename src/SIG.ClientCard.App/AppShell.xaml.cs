using SIG.ClientCard.App.ViewModels;
using SIG.ClientCard.App.Views;

namespace SIG.ClientCard.App;

public partial class AppShell : Shell
{
    public AppShell(SyncStatusViewModel syncStatus)
    {
        InitializeComponent();
        BindingContext = syncStatus;

        Routing.RegisterRoute("clientdetail", typeof(ClientDetailPage));
        Routing.RegisterRoute("clientedit", typeof(ClientEditPage));
        Routing.RegisterRoute("addservice", typeof(AddServicePage));
        Routing.RegisterRoute("addnote", typeof(AddNotePage));
    }
}
