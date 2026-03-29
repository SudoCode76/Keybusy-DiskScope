using Keybusy_DiskScope.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Keybusy_DiskScope.Views;

public sealed partial class ComparePage : Page
{
    public CompareViewModel ViewModel { get; }

    public ComparePage()
    {
        ViewModel = App.Services.GetRequiredService<CompareViewModel>();
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (ViewModel.AvailableSnapshots.Count == 0)
            {
                await ViewModel.LoadSnapshotsCommand.ExecuteAsync(null);
            }
        };
    }

    private void Row_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is DiffRow row)
        {
            ViewModel.SelectRowCommand.Execute(row);
        }
    }

    private void Row_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is DiffRow row)
        {
            ViewModel.SelectRowCommand.Execute(row);
        }
    }

    private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is DiffRow row)
        {
            ViewModel.DeleteRowCommand.Execute(row);
        }
    }

    private void ToggleExpand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is DiffRow row)
        {
            ViewModel.ToggleExpandAndSelectCommand.Execute(row);
        }
    }
}
