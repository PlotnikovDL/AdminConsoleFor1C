using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;

namespace AdminConsoleFor1C.App;

public sealed partial class AgentsPageViewModel : ObservableObject
{
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(RefreshProgressVisibility))]
    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    public string PageTitle => "Компоненты сервера";

    public string PageSubtitle => "Службы Windows и процессы 1С на этом компьютере";

    public string StatusText => IsRefreshing
        ? "Обновление списка компонентов"
        : "Нет данных о компонентах сервера";

    public string ComponentsStatusText => "Нет данных";

    public Visibility RefreshProgressVisibility => IsRefreshing
        ? Visibility.Visible
        : Visibility.Collapsed;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;

        try
        {
            await Task.Delay(250);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private bool CanRefresh() => !IsRefreshing;

    partial void OnIsRefreshingChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
    }
}
