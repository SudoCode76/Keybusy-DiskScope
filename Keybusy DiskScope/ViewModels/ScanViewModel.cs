using System.Text.Json;

using Keybusy_DiskScope.Services.Implementation;

namespace Keybusy_DiskScope.ViewModels;

public partial class ScanViewModel : ObservableObject
{
    private readonly IScanService _scanService;
    private readonly ISnapshotService _snapshotService;
    private readonly ILogger<ScanViewModel> _logger;

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string? _selectedDrive;
    [ObservableProperty] private DiskNode? _rootNode;
    [ObservableProperty] private string _snapshotName = string.Empty;

    public bool HasError => ErrorMessage is not null;

    public ObservableCollection<string> AvailableDrives { get; } = new();

    public ScanViewModel(
        IScanService scanService,
        ISnapshotService snapshotService,
        ILogger<ScanViewModel> logger)
    {
        _scanService = scanService;
        _snapshotService = snapshotService;
        _logger = logger;
    }

    public void LoadDrives()
    {
        AvailableDrives.Clear();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            AvailableDrives.Add(drive.RootDirectory.FullName);

        SelectedDrive = AvailableDrives.FirstOrDefault();
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task StartScanAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(SelectedDrive)) return;

        IsScanning = true;
        ErrorMessage = null;
        RootNode = null;
        ProgressValue = 0;

        try
        {
            var progress = new Progress<(long BytesScanned, string CurrentPath)>(report =>
            {
                StatusText = report.CurrentPath;
            });

            RootNode = await _scanService.ScanAsync(SelectedDrive, progress, ct);
            StatusText = $"Escaneo completado: {RootNode.DisplaySize} en {SelectedDrive}";

            SnapshotName = $"Escaneo {DateTime.Now:dd/MM/yyyy HH:mm}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Escaneo cancelado.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scan failed for {Drive}", SelectedDrive);
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task SaveSnapshotAsync()
    {
        if (RootNode is null || string.IsNullOrWhiteSpace(SnapshotName)) return;

        try
        {
            var record = new SnapshotRecord
            {
                Name          = SnapshotName,
                DrivePath     = SelectedDrive ?? string.Empty,
                CreatedAt     = DateTime.Now,
                TotalSizeBytes = RootNode.SizeBytes,
                TreeJson      = SnapshotService.SerializeTree(RootNode)
            };

            await _snapshotService.SaveAsync(record);
            StatusText = $"Snapshot '{SnapshotName}' guardado.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save snapshot");
            ErrorMessage = ex.Message;
        }
    }

    partial void OnErrorMessageChanged(string? value)
        => OnPropertyChanged(nameof(HasError));
}
