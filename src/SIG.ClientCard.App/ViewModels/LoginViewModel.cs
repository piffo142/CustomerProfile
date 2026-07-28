using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIG.ClientCard.App.Services;
using SIG.ClientCard.Sync.Transport;

namespace SIG.ClientCard.App.ViewModels;

public partial class LoginViewModel(AuthService auth) : ObservableObject
{
    [ObservableProperty]
    private string _email = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _error = "";

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private async Task SignInAsync()
    {
        if (IsBusy)
        {
            return;
        }

        Error = "";
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            Error = "Enter your email and password.";
            return;
        }

        IsBusy = true;
        try
        {
            await auth.SignInAsync(Email.Trim(), Password);
            Password = "";
            await Shell.Current.GoToAsync("//clients");
        }
        catch (SupabaseAuthException ex)
        {
            Error = ex.Message;
        }
        catch (Exception)
        {
            Error = "Cannot reach the server. Check your connection and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Local-only session: everything works offline and syncs after the next sign-in.</summary>
    [RelayCommand]
    private Task WorkOfflineAsync() => Shell.Current.GoToAsync("//clients");
}
