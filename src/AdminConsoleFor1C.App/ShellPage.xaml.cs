using Microsoft.UI.Xaml.Controls;

namespace AdminConsoleFor1C.App;

/// <summary>
/// Hosts the main Windows Settings-style navigation shell.
/// </summary>
public sealed partial class ShellPage : Page
{
    private AdminConsoleSection? _currentSection;

    public ShellPage()
    {
        InitializeComponent();

        RootNavigation.SelectedItem = AgentsNavigationItem;
        NavigateToSection(AdminConsoleSection.Agents);
    }

    private void RootNavigation_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string sectionTag)
        {
            NavigateToSection(AdminConsoleSections.FromTag(sectionTag));
        }
    }

    private void NavigateToSection(AdminConsoleSection section)
    {
        if (_currentSection == section && ContentFrame.Content is AgentsPage)
        {
            return;
        }

        _currentSection = section;

        if (ContentFrame.Content is AgentsPage agentsPage)
        {
            agentsPage.NavigateToSection(section);
            return;
        }

        ContentFrame.Navigate(typeof(AgentsPage), section);
    }
}
