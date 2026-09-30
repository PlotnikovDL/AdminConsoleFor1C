using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Administration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AdminConsoleFor1C.App;

public sealed partial class CreateInfobaseDialog : ContentDialog
{
    private readonly OneCServerConnectionProfile profile;
    private readonly OneCClusterInfo cluster;
    private readonly string? clusterPassword;
    private readonly CreateInfobaseViewModel viewModel;
    private OneCEmptyInfobaseOptions? prepared;
    private bool busy;
    public string? CreatedName { get; private set; }
    public bool ResultUncertain { get; private set; }

    public CreateInfobaseDialog(OneCServerConnectionProfile profile, OneCClusterInfo cluster, string? clusterPassword, XamlRoot root)
    {
        this.profile = profile;
        this.cluster = cluster;
        this.clusterPassword = clusterPassword;
        viewModel = new(profile, cluster);
        viewModel.Initialize();
        InitializeComponent();
        XamlRoot = root;
        DataContext = viewModel;
        DialogBody.Width = Math.Max(280, Math.Min(700, root.Size.Width - 100));
        Closed += (_, _) => DatabasePassword.Password = "";
    }

    private async void Primary_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (busy || ResultUncertain) return;
        if (prepared is null)
        {
            prepared = viewModel.Prepare();
            FormScroll.ChangeView(null, 0, null);
            if (prepared is null) return;
            FormPanel.Visibility = Visibility.Collapsed;
            ReviewPanel.Visibility = Visibility.Visible;
            StepTitle.Text = "2 из 2 · Проверьте место создания и параметры";
            PrimaryButtonText = "Создать базу";
            SecondaryButtonText = "Назад";
            return;
        }
        var deferral = args.GetDeferral();
        busy = true;
        IsPrimaryButtonEnabled = false;
        IsSecondaryButtonEnabled = false;
        CloseButtonText = "";
        Progress.Visibility = Visibility.Visible;
        viewModel.Error = null;
        StepTitle.Text = $"Создаётся база «{prepared.Name}» на {profile.AgentAddress}…";
        try
        {
            await AdminConsoleViewModelFactory.CreateInfobaseClient().CreateAsync(profile,
                new(profile.Id, cluster), prepared, clusterPassword, DatabasePassword.Password);
            CreatedName = prepared.Name;
            args.Cancel = false;
        }
        catch (OneCInfobaseCreationUncertainException ex)
        { ResultUncertain = true; viewModel.Error = ex.Message; }
        catch (Exception ex) { viewModel.Error = ex.Message; }
        finally
        {
            busy = false;
            CloseButtonText = ResultUncertain ? "Закрыть и обновить список" : "Отмена";
            IsPrimaryButtonEnabled = !ResultUncertain;
            IsSecondaryButtonEnabled = !ResultUncertain;
            Progress.Visibility = Visibility.Collapsed;
            FormScroll.ChangeView(null, 0, null);
            StepTitle.Text = "2 из 2 · Проверьте место создания и параметры";
            deferral.Complete();
        }
    }
    private void Back_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (busy || ResultUncertain) return;
        prepared = null;
        viewModel.Error = null;
        FormPanel.Visibility = Visibility.Visible;
        ReviewPanel.Visibility = Visibility.Collapsed;
        StepTitle.Text = "1 из 2 · Параметры пустой серверной базы";
        PrimaryButtonText = "Далее";
        SecondaryButtonText = "";
        FormScroll.ChangeView(null, 0, null);
    }
    private void Dialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args) => args.Cancel = busy;
}
