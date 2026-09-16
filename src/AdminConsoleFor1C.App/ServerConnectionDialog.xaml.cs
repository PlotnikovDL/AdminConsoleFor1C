using AdminConsoleFor1C.Core.Administration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AdminConsoleFor1C.App;

public sealed partial class ServerConnectionDialog : ContentDialog
{
    private readonly SessionsPageViewModel owner;
    private readonly ServerConnectionEditorViewModel viewModel;
    public bool Saved { get; private set; }

    public ServerConnectionDialog(SessionsPageViewModel owner, XamlRoot root, OneCServerConnectionProfile? profile = null)
    {
        this.owner = owner;
        viewModel = AdminConsoleViewModelFactory.CreateConnectionEditorViewModel(profile);
        InitializeComponent();
        XamlRoot = root;
        DataContext = viewModel;
        ClusterPassword.Password = profile is null ? "" : owner.GetPassword(profile.Id) ?? "";
        Loaded += async (_, _) => await viewModel.LoadAsync();
        Closed += (_, _) => ClusterPassword.Password = "";
    }

    private async void Save_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        var profile = viewModel.Validate();
        if (profile is null) return;
        var deferral = args.GetDeferral();
        IsPrimaryButtonEnabled = false;
        try
        {
            await owner.SaveConnectionAsync(profile, ClusterPassword.Password);
            Saved = true;
            args.Cancel = false;
        }
        catch (Exception ex) { viewModel.Error = ex.Message; }
        finally { IsPrimaryButtonEnabled = true; deferral.Complete(); }
    }
}
