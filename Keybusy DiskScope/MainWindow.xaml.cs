using Microsoft.UI.Xaml;

namespace Keybusy_DiskScope;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Allow ShellPage to designate the title bar drag region.
        // Mica backdrop is declared in MainWindow.xaml via <Window.SystemBackdrop>.
        ExtendsContentIntoTitleBar = true;
    }
}
