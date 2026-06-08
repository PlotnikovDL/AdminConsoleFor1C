using Microsoft.UI.Xaml.Controls;

namespace AdminConsoleFor1C.App;

public sealed partial class AgentsPage : Page
{
    public AgentsPage()
    {
        InitializeComponent();
        DataContext = new AgentsPageViewModel();
    }
}
