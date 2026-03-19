using Keybusy_DiskScope.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Keybusy_DiskScope.Views;

public sealed partial class ScanPage : Page
{
    public ScanViewModel ViewModel { get; }

    public ScanPage()
    {
        ViewModel = App.Services.GetRequiredService<ScanViewModel>();
        InitializeComponent();
        Loaded += (_, _) => ViewModel.LoadDrives();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (ViewModel.AvailableDrives.Count == 0)
        {
            ViewModel.LoadDrives();
        }

        if (e.Parameter is string drive && !string.IsNullOrWhiteSpace(drive))
        {
            ViewModel.SelectedDrive = drive;
            if (!ViewModel.IsScanning)
            {
                _ = ViewModel.StartScanCommand.ExecuteAsync(null);
            }
        }
    }

    private void ExpandAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var node in ScanTree.RootNodes)
        {
            SetExpanded(node, true);
        }
    }

    private void CollapseAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var node in ScanTree.RootNodes)
        {
            SetExpanded(node, false);
        }
    }

    private static void SetExpanded(TreeViewNode node, bool expanded)
    {
        node.IsExpanded = expanded;
        foreach (var child in node.Children)
        {
            SetExpanded(child, expanded);
        }
    }
}
