using Keybusy_DiskScope.Models;
using Keybusy_DiskScope.ViewModels;
using System.Diagnostics;
using System.IO;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

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
            var isCtrlPressed = (e.KeyModifiers & VirtualKeyModifiers.Control) != 0;
            var isShiftPressed = (e.KeyModifiers & VirtualKeyModifiers.Shift) != 0;
            ViewModel.SelectNodeWithModifiers(node, isCtrlPressed, isShiftPressed);
        }
    }

    private void Row_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is DiskNode node)
        {
            if (!ViewModel.IsNodeSelected(node))
            {
                ViewModel.SelectNodeWithModifiers(node, isCtrlPressed: false, isShiftPressed: false);
            }
        }
    }

    private void RowToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not DiskNode node)
        {
            return;
        }

        var isCtrlPressed = IsModifierPressed(VirtualKey.Control);
        var isShiftPressed = IsModifierPressed(VirtualKey.Shift);
        ViewModel.SelectNodeWithModifiers(node, isCtrlPressed, isShiftPressed);

        if (isCtrlPressed || isShiftPressed)
        {
            return;
        }

        ViewModel.ToggleExpandAndSelectCommand.Execute(node);
    }

    private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is DiskNode node)
        {
            ViewModel.DeleteNodeCommand.Execute(node);
        }
    }

    private void OpenInExplorerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not DiskNode node)
        {
            return;
        }

        try
        {
            OpenInExplorer(node.FullPath, node.IsDirectory || node.IsFileGroup);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = ex.Message;
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

    private void ExtendSelectionUpAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.ExtendSelectionByOffset(-1);
        args.Handled = true;
    }

    private void ExtendSelectionDownAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.ExtendSelectionByOffset(1);
        args.Handled = true;
    }

    private void ResultsScroll_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        HandleSelectionKey(e);
    }

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        HandleSelectionKey(e);
    }

    private void Page_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        HandleSelectionKey(e);
    }

    private void HandleSelectionKey(KeyRoutedEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (!IsModifierPressed(VirtualKey.Shift))
        {
            return;
        }

        if (e.Key == VirtualKey.Up)
        {
            ViewModel.ExtendSelectionByOffset(-1);
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.Down)
        {
            ViewModel.ExtendSelectionByOffset(1);
            e.Handled = true;
        }
    }

    private static bool IsModifierPressed(VirtualKey key)
        => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;

    private static void OpenInExplorer(string fullPath, bool openAsFolder)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            throw new InvalidOperationException("No se pudo abrir la ruta seleccionada.");
        }

        if (openAsFolder)
        {
            var folderPath = Directory.Exists(fullPath)
                ? fullPath
                : Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folderPath}\"")
                {
                    UseShellExecute = true
                });
                return;
            }
        }

        if (File.Exists(fullPath))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{fullPath}\"")
            {
                UseShellExecute = true
            });
            return;
        }

        var parentPath = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(parentPath) && Directory.Exists(parentPath))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{parentPath}\"")
            {
                UseShellExecute = true
            });
            return;
        }

        throw new DirectoryNotFoundException("La ruta ya no existe en disco.");
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
