using Keybusy_DiskScope.ViewModels;
using System.Diagnostics;
using System.IO;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

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
            var isCtrlPressed = (e.KeyModifiers & VirtualKeyModifiers.Control) != 0;
            var isShiftPressed = (e.KeyModifiers & VirtualKeyModifiers.Shift) != 0;
            ViewModel.SelectRowWithModifiers(row, isCtrlPressed, isShiftPressed);
        }
    }

    private void Row_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is DiffRow row)
        {
            if (!ViewModel.IsRowSelected(row))
            {
                ViewModel.SelectRowWithModifiers(row, isCtrlPressed: false, isShiftPressed: false);
            }
        }
    }

    private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is DiffRow row)
        {
            ViewModel.DeleteRowCommand.Execute(row);
        }
    }

    private void OpenInExplorerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not DiffRow row)
        {
            return;
        }

        try
        {
            OpenInExplorer(row.Node.FullPath, row.IsDirectory);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = ex.Message;
        }
    }

    private void ToggleExpand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is DiffRow row)
        {
            var isCtrlPressed = IsModifierPressed(VirtualKey.Control);
            var isShiftPressed = IsModifierPressed(VirtualKey.Shift);
            ViewModel.SelectRowWithModifiers(row, isCtrlPressed, isShiftPressed);
            if (isCtrlPressed || isShiftPressed)
            {
                return;
            }

            ViewModel.ToggleExpandAndSelectCommand.Execute(row);
        }
    }

    private void DeleteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.DeleteSelectedRowsCommand.Execute(null);
        args.Handled = true;
    }

    private void DeletePermanentAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.DeleteSelectedRowsPermanentCommand.Execute(null);
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

    private void CompareResultsScroll_KeyDown(object sender, KeyRoutedEventArgs e)
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
}
