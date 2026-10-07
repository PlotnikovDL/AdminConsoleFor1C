using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AdminConsoleFor1C.Core.Administration;

namespace AdminConsoleFor1C.App;

public sealed partial class SessionsPage : Page
{
    private readonly SessionsPageViewModel viewModel = AdminConsoleViewModelFactory.GetSessionsPageViewModel();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool dialogOpen;
    private int? filterColumns;
    private bool isPageActive;
    private int activationGeneration;

    public SessionsPage()
    {
        InitializeComponent();
        DataContext = viewModel;
        SessionWorkspace.SizeChanged += (_, e) => UpdateFilterLayout(e.NewSize.Width);
        Loaded += async (_, _) =>
        {
            if (isPageActive) return;
            isPageActive = true;
            var generation = ++activationGeneration;
            viewModel.PropertyChanged += ViewModel_Changed;
            await viewModel.ActivateAsync();
            if (!isPageActive || generation != activationGeneration) return;
            timer.Start();
            UpdateFilterLayout(SessionWorkspace.ActualWidth);
            UpdateSelection();
        };
        Unloaded += (_, _) =>
        {
            isPageActive = false;
            activationGeneration++;
            timer.Stop();
            viewModel.PropertyChanged -= ViewModel_Changed;
        };
        timer.Tick += async (_, _) =>
        {
            if (isPageActive && viewModel.IsAutoRefreshEnabled && !dialogOpen && viewModel.CanEdit)
                await viewModel.RefreshScopeCommand.ExecuteAsync(null);
        };
    }

    private void UpdateFilterLayout(double workspaceWidth)
    {
        var width = Math.Max(0, Math.Min(1320, workspaceWidth - 32));
        SessionFilters.Width = width;
        var columns = width < 600 ? 1 : width < 1000 ? 2 : 4;
        if (filterColumns == columns) return;
        filterColumns = columns;
        SessionFilters.RowDefinitions.Clear();
        for (var i = 0; i < 4 / columns; i++) SessionFilters.RowDefinitions.Add(new() { Height = GridLength.Auto });
        FrameworkElement[] filters = [SessionServerFilter, SessionClusterFilter, SessionInfobaseFilter, SessionSearchBox];
        for (var i = 0; i < filters.Length; i++)
        {
            Grid.SetColumn(filters[i], i % columns * (4 / columns));
            Grid.SetColumnSpan(filters[i], 4 / columns);
            Grid.SetRow(filters[i], i / columns);
        }
    }

    private void Connections_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SessionsList is not null) SessionsList.SelectedItems.Clear();
        UpdateSelection();
    }
    private void ViewModel_Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateSelection();
    private void Sessions_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelection();
    private void UpdateSelection()
    {
        if (TerminateButton is null || SessionsList is null || SelectionStatus is null || EmptyState is null) return;
        var count = SessionsList.SelectedItems.Count;
        TerminateButton.IsEnabled = viewModel.CanEdit && count > 0;
        TerminateButton.Label = count == 0 ? "Завершить" : $"Завершить ({count})";
        SelectionStatus.Text = count == 0 ? "Выберите сеансы для завершения" : $"Выбрано сеансов: {count}";
        EmptyState.Visibility = viewModel.Sessions.Count == 0 && !viewModel.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Text = viewModel.EmptyStateText;
    }

    private async void AddConnection_Click(object sender, RoutedEventArgs e) => await EditConnectionAsync(null);
    private async void EditConnection_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedConnection is { } selected) await EditConnectionAsync(selected.Profile);
    }

    private async Task EditConnectionAsync(OneCServerConnectionProfile? profile)
    {
        if (dialogOpen || !viewModel.CanEdit) return;
        dialogOpen = true;
        try
        {
            var dialog = new ServerConnectionDialog(viewModel, XamlRoot, profile);
            await dialog.ShowAsync();
            if (dialog.Saved) await viewModel.RefreshScopeCommand.ExecuteAsync(null);
        }
        finally { dialogOpen = false; }
    }

    private async void RemoveConnection_Click(object sender, RoutedEventArgs e)
    {
        if (dialogOpen || !viewModel.CanRemoveSelected || viewModel.SelectedConnection is not { } selected) return;
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
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) await viewModel.RemoveSelectedAsync();
        }
        finally { dialogOpen = false; }
    }

    private async void Terminate_Click(object sender, RoutedEventArgs e)
    {
        if (dialogOpen || !viewModel.CanEdit) return;
        var rows = SessionsList.SelectedItems.OfType<ServerSessionRow>().ToArray();
        if (rows.Length == 0) return;
        dialogOpen = true;
        try
        {
            var reason = new TextBox { Header = "Сообщение пользователям", Text = "Сеанс завершён администратором.", TextWrapping = TextWrapping.Wrap };
            var content = new StackPanel { Spacing = 16, MaxWidth = 520 };
            content.Children.Add(new TextBlock { Text = $"Будут завершены выбранные сеансы: {rows.Length}. Несохранённые данные пользователей могут быть потеряны.", TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new ScrollViewer { MaxHeight = 220, Content = new TextBlock { Text = string.Join("\n\n", rows.Select(r => r.ConfirmationText)), TextWrapping = TextWrapping.Wrap } });
            content.Children.Add(reason);
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = "Завершить выбранные сеансы?", Content = content,
                PrimaryButtonText = "Завершить", CloseButtonText = "Отмена", DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                await viewModel.TerminateAsync(rows, reason.Text);
        }
        finally { dialogOpen = false; }
    }
}
