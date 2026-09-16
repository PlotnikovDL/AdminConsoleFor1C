using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AdminConsoleFor1C.Core.Administration;

namespace AdminConsoleFor1C.App;

public sealed partial class SessionsPage : Page
{
    private readonly SessionsPageViewModel viewModel = AdminConsoleViewModelFactory.GetSessionsPageViewModel();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool dialogOpen;
    private bool? compactLayout;

    public SessionsPage()
    {
        InitializeComponent();
        DataContext = viewModel;
        SizeChanged += (_, _) => UpdateLayoutMode();
        SessionWorkspace.SizeChanged += (_, e) => SessionFilters.Width = Math.Max(0, Math.Min(760, e.NewSize.Width - 32));
        Loaded += async (_, _) => { await viewModel.LoadAsync(); timer.Start(); viewModel.PropertyChanged += ViewModel_Changed; UpdateSelection(); };
        Unloaded += (_, _) => { timer.Stop(); viewModel.PropertyChanged -= ViewModel_Changed; };
        timer.Tick += async (_, _) =>
        {
            if (AutoRefresh.IsChecked == true && !dialogOpen && viewModel.CanEdit)
                await viewModel.RefreshAllCommand.ExecuteAsync(null);
        };
    }

    private void UpdateLayoutMode()
    {
        var compact = ActualWidth < 1000;
        if (compactLayout == compact) return;
        compactLayout = compact;
        ServerSplitView.DisplayMode = compact ? SplitViewDisplayMode.Overlay : SplitViewDisplayMode.Inline;
        ServerSplitView.IsPaneOpen = !compact;
        ShowServersButton.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        SessionWorkspace.Margin = new Thickness(compact ? 0 : 16, 0, 0, 0);
    }

    private void ShowServers_Click(object sender, RoutedEventArgs e) => ServerSplitView.IsPaneOpen = !ServerSplitView.IsPaneOpen;
    private void Connections_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (compactLayout == true && e.AddedItems.Count > 0) ServerSplitView.IsPaneOpen = false;
    }
    private void Connections_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (compactLayout == true) ServerSplitView.IsPaneOpen = false;
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
        EmptyState.Text = viewModel.Connections.Count == 0 ? "Добавьте сервер, чтобы просматривать сеансы."
            : !viewModel.Connections.Any(c => c.IsConnected) || viewModel.SelectedConnection is { IsConnected: false }
                ? "Нажмите «Обновить», чтобы подключиться к серверу."
                : "Сеансы не найдены. Проверьте выбранную базу и поиск.";
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
            if (dialog.Saved) await viewModel.ConnectSelectedCommand.ExecuteAsync(null);
        }
        finally { dialogOpen = false; }
    }

    private async void RemoveConnection_Click(object sender, RoutedEventArgs e)
    {
        if (dialogOpen || !viewModel.CanManageSelected || viewModel.SelectedConnection is not { } selected) return;
        dialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = "Удалить подключение?",
                Content = $"{selected.Name}\n{selected.AddressText}\n\nБудет удалена только запись в этой консоли.",
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
