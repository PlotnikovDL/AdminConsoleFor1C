using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AdminConsoleFor1C.App;

public sealed partial class ServiceRegistrationDialog : ContentDialog
{
    private readonly AgentsPageViewModel owner;
    private readonly ServiceRegistrationViewModel viewModel;

    public ServiceRegistrationDialog(AgentsPageViewModel owner, XamlRoot root, string? executablePath = null)
    {
        this.owner = owner;
        viewModel = owner.CreateRegistrationViewModel(executablePath);
        InitializeComponent();
        XamlRoot = root;
        DataContext = viewModel;
        DialogContent.Width = Math.Max(240, Math.Min(600, root.Size.Width - 96));
        Loaded += async (_, _) => await viewModel.LoadAccountsAsync();
        Closed += (_, _) => ClearPasswords();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(viewModel.AccountIndex)) ClearPasswords();
        };
    }

    private async void Register_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        var request = viewModel.Validate(UserPassword.Password, ConfirmPassword.Password);
        if (request is null)
            return;

        var deferral = args.GetDeferral();
        viewModel.IsBusy = true;
        try
        {
            await owner.RegisterServiceAsync(request, UserPassword.Password);
            args.Cancel = false;
        }
        catch (Exception exception)
        {
            viewModel.ErrorMessage = exception.Message;
        }
        finally
        {
            viewModel.IsBusy = false;
            ClearPasswords();
            deferral.Complete();
        }
    }

    private void Dialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        args.Cancel = viewModel.IsBusy;
    }

    private void ClearPasswords()
    {
        UserPassword.Password = string.Empty;
        ConfirmPassword.Password = string.Empty;
    }
}
