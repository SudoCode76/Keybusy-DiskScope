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
    [ObservableProperty] private string _scanTitle = "Analisis";
    [ObservableProperty] private string _scanSummary = string.Empty;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private string _resultsSummary = string.Empty;
    [ObservableProperty] private string _rootDisplaySize = string.Empty;
    [ObservableProperty] private string _rootDisplayLabel = string.Empty;
    [ObservableProperty] private int _selectedSortIndex;
    [ObservableProperty] private bool? _filterDirectoriesOnly;
    [ObservableProperty] private bool? _filterLargeOnly;

    public bool HasError => ErrorMessage is not null;

    public ObservableCollection<string> AvailableDrives { get; } = new();
    public ObservableCollection<DiskNode> DisplayNodes { get; } = new();
    public ObservableCollection<string> SortOptions { get; } = new()
    {
        "Nombre",
        "Tamano",
        "Ocupacion",
        "Archivos",
        "Ultima modificacion"
    };

    public ScanViewModel(
        IScanService scanService,
        ISnapshotService snapshotService,
        ILogger<ScanViewModel> logger)
    {
        _scanService = scanService;
        _snapshotService = snapshotService;
        _logger = logger;

        StatusText = "Seleccione una unidad y pulse Escanear.";
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
        ScanSummary = string.Empty;

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            StatusText = "Preparando vista previa...";

            RootNode = await _scanService.ScanPreviewAsync(SelectedDrive, ct);
            ApplySizePercentages(RootNode);

            StatusText = "Analizando en profundidad...";
            var progress = new Progress<(long BytesScanned, string CurrentPath)>(report =>
            {
                StatusText = report.CurrentPath;
            });

            RootNode = await _scanService.ScanFullAsync(SelectedDrive, progress, ct);
            ApplySizePercentages(RootNode);

            stopwatch.Stop();
            StatusText = $"Escaneo completado: {RootNode.DisplaySize} en {SelectedDrive}";
            ScanSummary = $"Escaneo completado en {stopwatch.Elapsed.TotalSeconds:0} segundos. {RootNode.DisplaySize} analizados.";

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

    partial void OnScanSummaryChanged(string value)
        => OnPropertyChanged(nameof(HasSummary));

    partial void OnSelectedDriveChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ScanTitle = "Analisis";
            return;
        }

        var driveLabel = value.TrimEnd('\\');
        ScanTitle = $"Analisis: {driveLabel}";
    }

    partial void OnRootNodeChanged(DiskNode? value)
    {
        ApplyDisplayNodes();
    }

    partial void OnSelectedSortIndexChanged(int value)
        => ApplyDisplayNodes();

    partial void OnFilterDirectoriesOnlyChanged(bool? value)
        => ApplyDisplayNodes();

    partial void OnFilterLargeOnlyChanged(bool? value)
        => ApplyDisplayNodes();

    public bool HasSummary => !string.IsNullOrWhiteSpace(ScanSummary);

    private IRelayCommand? _cancelStartScanCommand;

    public IRelayCommand CancelStartScanCommand
        => _cancelStartScanCommand ??= new RelayCommand(() => StartScanCommand.Cancel());

    private static void ApplySizePercentages(DiskNode root)
    {
        var total = root.SizeBytes <= 0 ? 1 : root.SizeBytes;
        ApplySizePercentages(root, total);
    }

    private static void ApplySizePercentages(DiskNode node, long totalBytes)
    {
        if (node.IsPlaceholder)
        {
            return;
        }

        node.SizePercent = Math.Clamp(node.SizeBytes * 100d / totalBytes, 0, 100);
        foreach (var child in node.Children)
        {
            ApplySizePercentages(child, totalBytes);
        }
    }

    public Task<IReadOnlyList<DiskNode>> LoadChildrenAsync(DiskNode node, CancellationToken ct)
        => _scanService.LoadChildrenAsync(node.FullPath, ct);

    private void ApplyDisplayNodes()
    {
        DisplayNodes.Clear();

        if (RootNode is null)
        {
            HasResults = false;
            ResultsSummary = string.Empty;
            RootDisplaySize = string.Empty;
            RootDisplayLabel = string.Empty;
            return;
        }

        IEnumerable<DiskNode> nodes = RootNode.Children;

        if (FilterDirectoriesOnly == true)
        {
            nodes = nodes.Where(n => n.IsDirectory);
        }

        if (FilterLargeOnly == true)
        {
            nodes = nodes.Where(n => n.SizeBytes >= 1_073_741_824);
        }

        nodes = SelectedSortIndex switch
        {
            1 => nodes.OrderByDescending(n => n.SizeBytes),
            2 => nodes.OrderByDescending(n => n.SizePercent),
            3 => nodes.OrderByDescending(n => n.FileCount),
            4 => nodes.OrderByDescending(n => n.LastModified),
            _ => nodes.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
        };

        foreach (var node in nodes)
        {
            DisplayNodes.Add(node);
        }

        HasResults = DisplayNodes.Count > 0;
        ResultsSummary = $"{DisplayNodes.Count:N0} elementos mostrados";
        RootDisplaySize = RootNode.DisplaySize;
        RootDisplayLabel = $"Espacio total analizado: {RootDisplaySize}";
    }
}
