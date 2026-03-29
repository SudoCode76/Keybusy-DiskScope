using Keybusy_DiskScope.Models;
using Keybusy_DiskScope.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Input;

namespace Keybusy_DiskScope.Views;

public sealed partial class ScanPage : Page
{
    public ScanViewModel ViewModel { get; }
    private string? _pendingDrive;
    private bool _pendingAutoScan;

    public ScanPage()
    {
        ViewModel = App.Services.GetRequiredService<ScanViewModel>();
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ViewModel.LoadDrives();
            ResultsScroll.Focus(FocusState.Programmatic);

            if (_pendingAutoScan && !string.IsNullOrWhiteSpace(_pendingDrive))
            {
                ViewModel.SelectedDrive = _pendingDrive;
                if (!ViewModel.IsScanning)
                {
                    _ = ViewModel.StartScanCommand.ExecuteAsync(null);
                }

                _pendingAutoScan = false;
                _pendingDrive = null;
            }
        };
    }


    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is string drive && !string.IsNullOrWhiteSpace(drive))
        {
            ViewModel.SelectedDrive = drive;
            _pendingDrive = drive;
            _pendingAutoScan = true;
        }

        if (ViewModel.AvailableDrives.Count == 0)
        {
            ViewModel.LoadDrives();
        }
    }

    private void Row_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is DiskNode node)
        {
            ViewModel.SelectNodeCommand.Execute(node);
        }
    }

    private void Row_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is DiskNode node)
        {
            ViewModel.SelectNodeCommand.Execute(node);
        }
    }

    private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is DiskNode node)
        {
            ViewModel.DeleteNodeCommand.Execute(node);
        }
    }

    private void DeleteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.DeleteSelectedCommand.Execute(null);
        args.Handled = true;
    }

    private void DeletePermanentAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.DeleteSelectedPermanentCommand.Execute(null);
        args.Handled = true;
    }

    private void SaveTip_ActionButtonClick(TeachingTip sender, object args)
    {
        ViewModel.IsSaveTipOpen = false;
    }

    private void SaveTip_CloseButtonClick(TeachingTip sender, object args)
    {
        ViewModel.IsSaveTipOpen = false;
    }

}
