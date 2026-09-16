using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media.Animation;

namespace AdminConsoleFor1C.App;

public sealed partial class AgentComponentDetailsPage : Page
{
    private bool deletionDialogOpen;
    public AgentComponentDetailsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        DataContext = (AgentsPageViewModel)e.Parameter;
    }

    private async void DeleteService_Click(object sender, RoutedEventArgs e)
    {
        if (deletionDialogOpen || DataContext is not AgentsPageViewModel { CanDeleteSelectedService: true } viewModel
            || viewModel.SelectedService is not { } service)
            return;
        deletionDialogOpen = true;
        try
        {
            var dialog = new ServiceDeletionDialog(viewModel, service, XamlRoot);
            await dialog.ShowAsync();
            if (viewModel.SelectedService is null)
            {
                viewModel.TryNavigateToBreadcrumb(new AgentsBreadcrumbItem("Службы", AgentsPageRoute.Overview), out _);
                Frame.Navigate(typeof(AgentsOverviewPage), viewModel, new SuppressNavigationTransitionInfo());
            }
        }
        finally { deletionDialogOpen = false; }
    }
}
