using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AdminConsoleFor1C.App;

public sealed partial class AgentProcessesPage : Page
{
    private AgentsPageViewModel viewModel = null!;

    public AgentProcessesPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        viewModel = (AgentsPageViewModel)e.Parameter;
        DataContext = viewModel;
    }
}
