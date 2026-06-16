using Microsoft.UI.Xaml.Controls;

namespace AdminConsoleFor1C.App;

public sealed partial class ShellPage : Page
{
    private AdminConsoleSection? currentSection;

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
        if (args.SelectedItemContainer?.Tag is string tag)
        {
            NavigateToSection(AdminConsoleSections.FromTag(tag));
        }
    }

    private void NavigateToSection(AdminConsoleSection section)
    {
        if (currentSection == section)
        {
            return;
        }

        currentSection = section;

        if (section == AdminConsoleSection.Agents)
        {
            ContentFrame.Navigate(typeof(AgentsPage));
            return;
        }

        if (section == AdminConsoleSection.Processes)
        {
            ContentFrame.Navigate(typeof(ProcessesPage));
            return;
        }

        ContentFrame.Navigate(
            typeof(AdminConsolePlaceholderPage),
            AdminConsoleSections.GetInfo(section));
    }
}
