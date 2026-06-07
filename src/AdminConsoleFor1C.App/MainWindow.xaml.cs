using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace AdminConsoleFor1C.App;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to AgentsPage.xaml / AgentsPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Navigate the root frame to the application shell on startup.
        RootFrame.Navigate(typeof(ShellPage));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }
}
