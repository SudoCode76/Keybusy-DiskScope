using Keybusy_DiskScope.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Keybusy_DiskScope.Views;

public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; }

    public HomePage()
    {
        ViewModel = App.Services.GetRequiredService<HomeViewModel>();
        InitializeComponent();
        DataContext = ViewModel;
    }

    private void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.DataContext is Models.DriveModel drive)
        {
            _ = ViewModel.AnalyzeCommand.ExecuteAsync(drive);
        }
    }
}
