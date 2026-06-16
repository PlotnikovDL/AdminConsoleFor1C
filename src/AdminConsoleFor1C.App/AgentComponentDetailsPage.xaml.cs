using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace AdminConsoleFor1C.App;

public sealed partial class AgentComponentDetailsPage : Page
{
    private AgentsPageViewModel viewModel = null!;

    public AgentComponentDetailsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        viewModel = (AgentsPageViewModel)e.Parameter;
        DataContext = viewModel;
    }

    private void DetailCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AgentComponentDetailItemViewModel detail })
        {
            return;
        }

        viewModel.OpenProcessDetailsCommand.Execute(detail);

        if (viewModel.SelectedProcessGroup is null)
        {
            return;
        }

        Frame.Navigate(
            typeof(AgentProcessesPage),
            viewModel,
            new SlideNavigationTransitionInfo
            {
                Effect = SlideNavigationTransitionEffect.FromRight
            });
    }
}
