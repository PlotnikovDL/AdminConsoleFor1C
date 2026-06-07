using Microsoft.UI.Xaml.Controls;

namespace AdminConsoleFor1C.App;

/// <summary>
/// Hosts the main Windows Settings-style navigation shell.
/// </summary>
public sealed partial class ShellPage : Page
{
    private string? _currentSectionTag;

    public ShellPage()
    {
        InitializeComponent();

        RootNavigation.SelectedItem = AgentsNavigationItem;
        NavigateToSection("agents");
    }

    private void RootNavigation_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string sectionTag)
        {
            NavigateToSection(sectionTag);
        }
    }

    private void NavigateToSection(string sectionTag)
    {
        if (_currentSectionTag == sectionTag && ContentFrame.Content is AgentsPage)
        {
            return;
        }

        _currentSectionTag = sectionTag;

        if (ContentFrame.Content is AgentsPage agentsPage)
        {
            agentsPage.NavigateToSection(sectionTag);
            return;
        }

        ContentFrame.Navigate(typeof(AgentsPage), sectionTag);
    }
}
