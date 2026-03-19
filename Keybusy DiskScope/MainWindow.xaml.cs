using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Keybusy_DiskScope;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Allow ShellPage to designate the title bar drag region.
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new DesktopAcrylicBackdrop();
    }
}
