using System.Collections.ObjectModel;
using AdminConsoleFor1C.Application.Services;
using AdminConsoleFor1C.Core.Services;
using AdminConsoleFor1C.Infrastructure.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AdminConsoleFor1C.App;

/// <summary>
/// The main content page displayed inside the application window.
/// </summary>
public sealed partial class MainPage : Page
{
    private readonly IOneCServiceInventory _serviceInventory = new WindowsOneCServiceInventory();
    private readonly ObservableCollection<OneCServiceInfo> _services = [];

    public MainPage()
    {
        InitializeComponent();
        ServicesList.ItemsSource = _services;
        Loaded += MainPage_Loaded;
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainPage_Loaded;
        await RefreshServicesAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshServicesAsync();
    }

    private async Task RefreshServicesAsync()
    {
        RefreshButton.IsEnabled = false;
        RefreshProgress.IsActive = true;
        RefreshProgress.Visibility = Visibility.Visible;
        ErrorInfoBar.IsOpen = false;
        StatusText.Text = "Обновление...";

        try
        {
            var services = await _serviceInventory.GetServicesAsync();

            _services.Clear();
            foreach (var service in services)
            {
                _services.Add(service);
            }

            StatusText.Text = $"Найдено служб: {_services.Count}";
        }
        catch (Exception exception)
        {
            ErrorInfoBar.Message = exception.Message;
            ErrorInfoBar.IsOpen = true;
            StatusText.Text = "Ошибка обновления";
        }
        finally
        {
            RefreshProgress.IsActive = false;
            RefreshProgress.Visibility = Visibility.Collapsed;
            RefreshButton.IsEnabled = true;
        }
    }
}
