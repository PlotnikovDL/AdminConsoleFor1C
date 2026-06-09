using AdminConsoleFor1C.Infrastructure.Services;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AdminConsoleFor1C.App;

public sealed partial class AgentsPage : Page
{
    private readonly AgentsPageViewModel viewModel;

    public AgentsPage()
    {
        InitializeComponent();

        viewModel = new AgentsPageViewModel(
            new WindowsOneCServiceInventory(),
            new WindowsOneCProcessInventory());

        DataContext = viewModel;
        Loaded += AgentsPage_Loaded;
    }

    private async void AgentsPage_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= AgentsPage_Loaded;

        if (viewModel.RefreshCommand is IAsyncRelayCommand refreshCommand)
        {
            await refreshCommand.ExecuteAsync(null);
        }
    }

    private void DetailCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AgentComponentDetailItemViewModel detail })
        {
            viewModel.OpenProcessDetailsCommand.Execute(detail);
        }
    }

    private void BreadcrumbBar_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        viewModel.NavigateToBreadcrumb(args.Index);
    }
}
