using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AdminConsoleFor1C.App;

public sealed partial class AgentComponentDetailsPage : Page
{
    public AgentComponentDetailsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        DataContext = (AgentsPageViewModel)e.Parameter;
    }
}
