using AdminConsoleFor1C.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AdminConsoleFor1C.App;

public sealed partial class ServiceDeletionDialog : ContentDialog
{
    private readonly AgentsPageViewModel owner;
    private readonly OneCServiceInfo service;
    private bool isBusy;

    public ServiceDeletionDialog(AgentsPageViewModel owner, OneCServiceInfo service, XamlRoot root)
    {
        this.owner = owner;
        this.service = service;
        InitializeComponent();
        XamlRoot = root;
        DataContext = service;
        DeleteData.IsEnabled = service.Kind == OneCServiceKind.ServerAgent && !string.IsNullOrWhiteSpace(service.DataDirectory);
        DeleteData.IsChecked = DeleteData.IsEnabled;
        if (!DeleteData.IsEnabled)
            DataWarning.Text = "У службы нет явного каталога агента в параметре /d. Будет удалена только служба.";
    }

    private async void Delete_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        var deferral = args.GetDeferral();
        isBusy = true;
        IsPrimaryButtonEnabled = false;
        DeleteData.IsEnabled = false;
        ErrorBar.IsOpen = false;
        Progress.Visibility = Visibility.Visible;
        try
        {
            await owner.DeleteServiceAsync(new OneCServiceDeletionRequest
            {
                ServiceName = service.Name,
                ExpectedCommandLine = service.RawCommandLine ?? string.Empty,
                DataDirectory = DeleteData.IsChecked == true ? service.DataDirectory : null
            });
            args.Cancel = false;
        }
        catch (Exception exception)
        {
            ErrorBar.Message = exception.Message;
            ErrorBar.IsOpen = true;
        }
        finally
        {
            isBusy = false;
            IsPrimaryButtonEnabled = owner.SelectedService?.Name == service.Name;
            DeleteData.IsEnabled = IsPrimaryButtonEnabled && service.Kind == OneCServiceKind.ServerAgent
                && !string.IsNullOrWhiteSpace(service.DataDirectory);
            Progress.Visibility = Visibility.Collapsed;
            deferral.Complete();
        }
    }

    private void Dialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args) => args.Cancel = isBusy;

    private void DeleteData_Changed(object sender, RoutedEventArgs args)
    {
        DataWarning.Text = DeleteData.IsChecked == true
            ? "Будут безвозвратно удалены файлы кластера, его настройки и все остальные файлы в указанном каталоге."
            : "Каталог данных и его содержимое будут сохранены.";
    }
}
