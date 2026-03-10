using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Keybusy_DiskScope.ViewModels;

namespace Keybusy_DiskScope.Views;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        this.InitializeComponent();
    }

    private void AnalyzeButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        // Minimal wiring: the ViewModel command will be implemented next.
        if (DataContext is HomeViewModel vm && sender is Button b && b.DataContext is Models.DriveModel drive)
        {
            _ = vm.AnalyzeCommand.ExecuteAsync(drive);
        }
    }
}
