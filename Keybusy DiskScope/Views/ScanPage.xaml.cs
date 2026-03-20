using Keybusy_DiskScope.Models;
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

    private async void ScanTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Node?.Content is not DiskNode node)
        {
            return;
        }

        if (!node.IsDirectory || node.ChildrenLoaded)
        {
            return;
        }

        try
        {
            var children = await ViewModel.LoadChildrenAsync(node, CancellationToken.None);
            node.Children.Clear();
            foreach (var child in children)
            {
                node.Children.Add(child);
            }

            node.ChildrenLoaded = true;
            node.HasChildren = node.Children.Count > 0;
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = ex.Message;
        }
    }
}
