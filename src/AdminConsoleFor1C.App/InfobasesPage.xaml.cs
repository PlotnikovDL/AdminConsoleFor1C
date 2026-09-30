using AdminConsoleFor1C.Core.Administration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AdminConsoleFor1C.App;

public sealed partial class InfobasesPage : Page
{
    private readonly InfobasesPageViewModel viewModel = AdminConsoleViewModelFactory.CreateInfobasesPageViewModel();
    private bool dialogOpen;
    public InfobasesPage()
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => { await viewModel.LoadAsync(); UpdateEmptyState(); };
        viewModel.PropertyChanged += (_, _) => UpdateEmptyState();
        viewModel.Infobases.CollectionChanged += (_, _) => UpdateEmptyState();
    }
    private void UpdateEmptyState()
    {
        if (EmptyState is null) return;
        LoadingProgress.Visibility = viewModel.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = viewModel.Infobases.Count == 0 && !viewModel.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Text = viewModel.Connections.Count == 0 ? "Добавьте сервер 1С, на котором нужно создать базу."
            : viewModel.SelectedCluster is null ? "Выберите сервер и кластер."
            : viewModel.SelectedCluster.Cluster.DetailsMessage is { Length: > 0 } ? "Список баз недоступен. Проверьте подключение."
            : viewModel.SearchText.Length > 0 ? "По вашему запросу базы не найдены." : "В этом кластере пока нет баз. Нажмите «Создать базу».";
    }
    private async void Server_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        viewModel.SelectedServer = (sender as ComboBox)?.SelectedItem as ServerConnectionItemViewModel;
        viewModel.RefreshCommand.NotifyCanExecuteChanged();
        if (viewModel.CanRefresh) await viewModel.RefreshSelectedServerAsync();
    }
    private async void Server_DropDownOpened(object sender, object e) => await viewModel.RefreshServersAsync();
    private async void AddServer_Click(object sender, RoutedEventArgs e) => await EditServerAsync(null);
    private async void EditServer_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedServer is { } server) await EditServerAsync(server.Profile);
    }
    private async Task EditServerAsync(OneCServerConnectionProfile? profile)
    {
        if (dialogOpen || !viewModel.CanEdit) return;
        dialogOpen = true;
        try
        {
            var dialog = new ServerConnectionDialog(viewModel.ConnectionOwner, XamlRoot, profile);
            await dialog.ShowAsync();
            if (dialog.Saved) viewModel.SelectedServer = viewModel.ConnectionOwner.SelectedConnection;
        }
        finally { dialogOpen = false; }
    }
    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (dialogOpen || !viewModel.CanCreate || viewModel.SelectedServer is not { } server || viewModel.SelectedCluster is not { } cluster) return;
        dialogOpen = true;
        try
        {
            var dialog = new CreateInfobaseDialog(server.Profile, cluster.Cluster,
                viewModel.ConnectionOwner.GetPassword(server.Profile.Id), XamlRoot);
            await dialog.ShowAsync();
            if (dialog.CreatedName is { } name)
            {
                await viewModel.RefreshAsync();
                var refreshError = viewModel.Message;
                viewModel.ShowMessage($"База «{name}» создана на {cluster.Cluster.AddressText}, платформа {server.Profile.PlatformVersion}."
                    + (refreshError is null ? "" : "\nНе удалось обновить список: " + refreshError),
                    refreshError is null ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
            }
            else if (dialog.ResultUncertain) await viewModel.RefreshAsync();
        }
        finally { dialogOpen = false; }
    }
}
