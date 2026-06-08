using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AdminConsoleFor1C.App;

public sealed partial class AdminConsolePlaceholderPage : Page
{
    public AdminConsolePlaceholderPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        DataContext = e.Parameter as AdminConsoleSectionInfo
            ?? AdminConsoleSections.GetInfo(AdminConsoleSection.Settings);
    }
}
