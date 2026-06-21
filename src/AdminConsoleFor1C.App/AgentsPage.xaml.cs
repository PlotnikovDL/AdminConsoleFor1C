using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace AdminConsoleFor1C.App;

public sealed partial class AgentsPage : Page
{
    private readonly AgentsPageViewModel viewModel;

    public AgentsPage()
    {
        InitializeComponent();

        viewModel = AdminConsoleViewModelFactory.CreateAgentsPageViewModel();
        DataContext = viewModel;

        ContentFrame.Navigate(
            typeof(AgentsOverviewPage),
            viewModel,
            new SuppressNavigationTransitionInfo());

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

    private void HeaderBreadcrumb_ItemClicked(
        BreadcrumbBar sender,
        BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Item is not AgentsBreadcrumbItem item)
        {
            return;
        }

        if (!viewModel.TryNavigateToBreadcrumb(item, out var route))
        {
            return;
        }

        NavigateToRoute(route);
    }

    private void NavigateToRoute(AgentsPageRoute route)
    {
        var pageType = route switch
        {
            AgentsPageRoute.Overview => typeof(AgentsOverviewPage),
            AgentsPageRoute.ComponentDetails => typeof(AgentComponentDetailsPage),
            _ => typeof(AgentsOverviewPage)
        };

        if (ContentFrame.Content?.GetType() == pageType)
        {
            return;
        }

        ContentFrame.Navigate(
            pageType,
            viewModel,
            new SlideNavigationTransitionInfo
            {
                Effect = SlideNavigationTransitionEffect.FromLeft
            });
    }
}
