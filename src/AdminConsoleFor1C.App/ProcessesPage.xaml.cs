using AdminConsoleFor1C.Infrastructure.Services;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AdminConsoleFor1C.App;

public sealed partial class ProcessesPage : Page
{
    private readonly ProcessesPageViewModel viewModel;

    public ProcessesPage()
    {
        InitializeComponent();

        viewModel = new ProcessesPageViewModel(new WindowsOneCProcessInventory());
        DataContext = viewModel;

        Loaded += ProcessesPage_Loaded;
    }

    private async void ProcessesPage_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= ProcessesPage_Loaded;

        if (viewModel.RefreshCommand is IAsyncRelayCommand refreshCommand)
        {
            await refreshCommand.ExecuteAsync(null);
        }
    }
}
