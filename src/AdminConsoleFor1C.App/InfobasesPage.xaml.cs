using AdminConsoleFor1C.Core.Administration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AdminConsoleFor1C.App;

public sealed partial class InfobasesPage : Page
{
    private readonly InfobasesPageViewModel viewModel = AdminConsoleViewModelFactory.GetInfobasesPageViewModel();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool dialogOpen;
    private int? filterColumns;
    private bool isPageActive;
    private int activationGeneration;
    public InfobasesPage()
    {
        InitializeComponent();
        DataContext = viewModel;
        InfobaseWorkspace.SizeChanged += (_, _) => UpdateFilterLayout();
        Loaded += async (_, _) =>
        {
            if (isPageActive) return;
            isPageActive = true;
            var generation = ++activationGeneration;
            viewModel.PropertyChanged += ViewModel_Changed;
            viewModel.Infobases.CollectionChanged += Infobases_Changed;
            await viewModel.ActivateAsync();
            if (!isPageActive || generation != activationGeneration) return;
            timer.Start();
            UpdateEmptyState();
            UpdateFilterLayout();
        };
        Unloaded += (_, _) =>
        {
            isPageActive = false;
            activationGeneration++;
            timer.Stop();
            viewModel.PropertyChanged -= ViewModel_Changed;
            viewModel.Infobases.CollectionChanged -= Infobases_Changed;
        };
        timer.Tick += async (_, _) =>
        {
            if (isPageActive && viewModel.IsAutoRefreshEnabled && !dialogOpen && viewModel.CanEdit)
                await viewModel.RefreshAsync();
        };
    }
    private void ViewModel_Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateEmptyState();
    private void Infobases_Changed(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => UpdateEmptyState();
    private void UpdateEmptyState()
    {
        if (EmptyState is null) return;
        EmptyState.Visibility = viewModel.Infobases.Count == 0 && !viewModel.IsUpdating ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Text = viewModel.EmptyStateText;
    }
    private void UpdateFilterLayout()
    {
        var width = Math.Max(0, Math.Min(1320, InfobaseWorkspace.ActualWidth - 32));
        InfobaseFilters.Width = width;
        var columns = width < 600 ? 1 : width < 1000 ? 2 : 4;
        if (filterColumns == columns) return;
        filterColumns = columns;
        InfobaseFilters.RowDefinitions.Clear();
        for (var i = 0; i < 4 / columns; i++) InfobaseFilters.RowDefinitions.Add(new() { Height = GridLength.Auto });
        FrameworkElement[] filters = [ServerFilter, ClusterFilter, BaseFilter, SearchFilter];
        for (var i = 0; i < filters.Length; i++)
        {
            Grid.SetColumn(filters[i], i % columns * (4 / columns));
            Grid.SetColumnSpan(filters[i], 4 / columns);
            Grid.SetRow(filters[i], i / columns);
        }
    }
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
            if (dialog.Saved && viewModel.ConnectionOwner.SelectedConnection is { } selected)
            {
                await viewModel.RefreshServersAsync();
                viewModel.SelectServer(selected.Profile.Id);
                await viewModel.RefreshAsync();
            }
        }
        finally { dialogOpen = false; }
    }
    private async void RemoveConnection_Click(object sender, RoutedEventArgs e)
    {
        if (dialogOpen || !viewModel.CanRemoveSelected || viewModel.SelectedServer is not { } selected) return;
        dialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = "Удалить подключение?",
                Content = $"{selected.Name}\n{selected.AddressText}\n\nБудет удалена только запись в этой консоли."
                    + (selected.LocalService is null ? "" : " Локальная служба продолжит отображаться автоматически."),
                PrimaryButtonText = "Удалить подключение", CloseButtonText = "Отмена", DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) await viewModel.RemoveSelectedServerAsync();
        }
        finally { dialogOpen = false; }
    }
    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (dialogOpen || !viewModel.CanCreate || viewModel.SelectedServer is not { } server || viewModel.CreationCluster is not { } cluster) return;
        dialogOpen = true;
        try
        {
            var dialog = new CreateInfobaseDialog(server.Profile, cluster,
                viewModel.ConnectionOwner.GetPassword(server.Profile.Id), XamlRoot);
            await dialog.ShowAsync();
            if (dialog.CreatedName is { } name)
            {
                await viewModel.RefreshAsync();
                var refreshError = viewModel.HasConnectionError ? viewModel.ConnectionErrors : viewModel.Message;
                viewModel.ShowMessage($"База «{name}» создана на {cluster.AddressText}, платформа {server.Profile.PlatformVersion}."
                    + (refreshError is null or "" ? "" : "\nНе удалось обновить список: " + refreshError),
                    refreshError is null or "" ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
            }
            else if (dialog.ResultUncertain) await viewModel.RefreshAsync();
        }
        finally { dialogOpen = false; }
    }
}
