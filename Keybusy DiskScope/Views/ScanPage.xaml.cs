using Keybusy_DiskScope.Models;
using Keybusy_DiskScope.ViewModels;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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

    private async void ExportCsvButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ErrorMessage = null;
        try
        {
            var csv = ViewModel.BuildCurrentScanCsv();
            var fileName = BuildExportFileName(ViewModel.SelectedDrive);
            var window = ((App)Application.Current).MainWindow;
            if (window is null)
            {
                throw new InvalidOperationException("No se pudo obtener la ventana principal para exportar.");
            }

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var selectedPath = ShowSaveCsvDialog(hwnd, fileName);
            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                return;
            }

            await File.WriteAllTextAsync(selectedPath, csv);
            ViewModel.StatusText = $"CSV exportado: {selectedPath}";
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = string.IsNullOrWhiteSpace(ex.Message)
                ? "No se pudo abrir el selector de guardado para exportar CSV."
                : ex.Message;
        }
    }

    private static string BuildExportFileName(string? selectedDrive)
    {
        var driveLabel = string.IsNullOrWhiteSpace(selectedDrive)
            ? "Drive"
            : selectedDrive.Trim().TrimEnd('\\').Replace(':', '_').Replace('\\', '_');

        return $"disk-scan-{driveLabel}-{DateTime.Now:yyyyMMdd-HHmmss}";
    }

    private static string? ShowSaveCsvDialog(IntPtr ownerHandle, string suggestedFileName)
    {
        var initialPath = suggestedFileName + ".csv";
        var maxChars = 4096;
        var fileBuffer = initialPath.PadRight(maxChars, '\0');

        var openFileName = new OpenFileName
        {
            lStructSize = Marshal.SizeOf<OpenFileName>(),
            hwndOwner = ownerHandle,
            lpstrFilter = "CSV files (*.csv)\0*.csv\0All files (*.*)\0*.*\0\0",
            lpstrFile = fileBuffer,
            nMaxFile = maxChars,
            lpstrDefExt = "csv",
            lpstrTitle = "Exportar analisis a CSV",
            Flags = OfnExplorer | OfnPathMustExist | OfnOverwritePrompt | OfnHideReadOnly
        };

        var accepted = GetSaveFileName(ref openFileName);
        if (accepted)
        {
            var rawPath = openFileName.lpstrFile;
            var end = rawPath.IndexOf('\0');
            return end >= 0 ? rawPath[..end] : rawPath;
        }

        var errorCode = CommDlgExtendedError();
        if (errorCode == 0)
        {
            return null;
        }

        throw new InvalidOperationException($"No se pudo abrir el selector de guardado (codigo {errorCode}).");
    }

    private const int OfnOverwritePrompt = 0x00000002;
    private const int OfnHideReadOnly = 0x00000004;
    private const int OfnPathMustExist = 0x00000800;
    private const int OfnExplorer = 0x00080000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpstrFilter;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpstrFile;
        public int nMaxFile;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpstrFileTitle;
        public int nMaxFileTitle;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpstrInitialDir;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetSaveFileName([In, Out] ref OpenFileName openFileName);

    [DllImport("comdlg32.dll")]
    private static extern int CommDlgExtendedError();

}
