using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace AdminConsoleFor1C.App;

public sealed partial class AgentsOverviewPage : Page
{
    private AgentsPageViewModel viewModel = null!;

    public AgentsOverviewPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        viewModel = (AgentsPageViewModel)e.Parameter;
        DataContext = viewModel;
    }

    private void ComponentCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AgentComponentItemViewModel component })
        {
            return;
        }

        viewModel.OpenComponentDetailsCommand.Execute(component);

        Frame.Navigate(
            typeof(AgentComponentDetailsPage),
            viewModel,
            new SlideNavigationTransitionInfo
            {
                Effect = SlideNavigationTransitionEffect.FromRight
            });
    }
}
