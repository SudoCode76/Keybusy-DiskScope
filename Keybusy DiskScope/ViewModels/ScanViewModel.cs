using System.Text.Json;

using Keybusy_DiskScope.Services;

namespace Keybusy_DiskScope.ViewModels;

public partial class ScanViewModel : ObservableObject
{
    private readonly IScanService _scanService;
    private readonly ISnapshotService _snapshotService;
    private readonly IFileDeleteService _fileDeleteService;
    private readonly ILogger<ScanViewModel> _logger;
    private CancellationTokenSource? _scanCts;

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string? _selectedDrive;
    [ObservableProperty] private DiskNode? _rootNode;
    [ObservableProperty] private DiskNode? _selectedNode;
    [ObservableProperty] private string _snapshotName = string.Empty;
    [ObservableProperty] private string _scanTitle = "Analisis";
    [ObservableProperty] private string _scanSummary = string.Empty;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private string _resultsSummary = string.Empty;
    [ObservableProperty] private string _rootDisplaySize = string.Empty;
    [ObservableProperty] private string _rootDisplayLabel = string.Empty;
    [ObservableProperty] private int _selectedSortIndex;
    [ObservableProperty] private bool _sortDescending;
    [ObservableProperty] private bool? _filterDirectoriesOnly;
    [ObservableProperty] private bool? _filterLargeOnly;
    [ObservableProperty] private bool _showSizeColumn = true;
    [ObservableProperty] private bool _showPercentColumn = true;
    [ObservableProperty] private bool _showFilesColumn = true;
    [ObservableProperty] private bool _showModifiedColumn = true;

    public bool HasError => ErrorMessage is not null;
    public bool IsNotScanning => !IsScanning;

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
        IFileDeleteService fileDeleteService,
        ILogger<ScanViewModel> logger)
    {
        _scanService = scanService;
        _snapshotService = snapshotService;
        _fileDeleteService = fileDeleteService;
        _logger = logger;

        StatusText = "Seleccione una unidad y pulse Escanear.";
        SortDescending = true;
    }

    public void LoadDrives()
    {
        AvailableDrives.Clear();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            AvailableDrives.Add(drive.RootDirectory.FullName);

        if (!string.IsNullOrWhiteSpace(SelectedDrive)
            && AvailableDrives.Contains(SelectedDrive))
        {
            return;
        }

        SelectedDrive = AvailableDrives.FirstOrDefault();
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task StartScanAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(SelectedDrive)) return;

        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _scanCts.Token);
        var scanToken = linkedCts.Token;

        IsScanning = true;
        ErrorMessage = null;
        RootNode = null;
        ProgressValue = 0;
        ScanSummary = string.Empty;

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            StatusText = "Preparando vista previa...";

            RootNode = await _scanService.ScanPreviewAsync(SelectedDrive, scanToken);
            scanToken.ThrowIfCancellationRequested();
            ApplySizePercentages(RootNode);

            StatusText = "Analizando en profundidad...";
            var lastUiUpdate = DateTime.MinValue;
            string? pendingPath = null;
            var progress = new Progress<(long BytesScanned, string CurrentPath)>(report =>
            {
                pendingPath = report.CurrentPath;
                var now = DateTime.UtcNow;
                if ((now - lastUiUpdate).TotalMilliseconds < 150)
                {
                    return;
                }

                lastUiUpdate = now;
                StatusText = pendingPath;
            });

            RootNode = await _scanService.ScanFullAsync(SelectedDrive, progress, scanToken);
            scanToken.ThrowIfCancellationRequested();
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
            _scanCts?.Dispose();
            _scanCts = null;
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
                TreeJson      = JsonSerializer.Serialize(RootNode)
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
    {
        SortDescending = true;
        ApplyDisplayNodes();
        NotifySortIndicators();
    }

    partial void OnSortDescendingChanged(bool value)
    {
        ApplyDisplayNodes();
        NotifySortIndicators();
    }

    partial void OnFilterDirectoriesOnlyChanged(bool? value)
        => ApplyDisplayNodes();

    partial void OnFilterLargeOnlyChanged(bool? value)
        => ApplyDisplayNodes();

    partial void OnIsScanningChanged(bool value)
        => OnPropertyChanged(nameof(IsNotScanning));

    public bool HasSummary => !string.IsNullOrWhiteSpace(ScanSummary);

    private IRelayCommand? _cancelStartScanCommand;

    public IRelayCommand CancelStartScanCommand
        => _cancelStartScanCommand ??= new RelayCommand(() =>
        {
            _scanCts?.Cancel();
            StartScanCommand.Cancel();
            StatusText = "Cancelando...";
        });

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

    [RelayCommand]
    private void SortBy(int columnIndex)
    {
        if (SelectedSortIndex == columnIndex)
        {
            SortDescending = !SortDescending;
            return;
        }

        SelectedSortIndex = columnIndex;
        SortDescending = true;
    }

    public bool IsNameSort => SelectedSortIndex == 0;
    public bool IsSizeSort => SelectedSortIndex == 1;
    public bool IsPercentSort => SelectedSortIndex == 2;
    public bool IsFilesSort => SelectedSortIndex == 3;
    public bool IsModifiedSort => SelectedSortIndex == 4;

    public int NameSortIndex => 0;
    public int SizeSortIndex => 1;
    public int PercentSortIndex => 2;
    public int FilesSortIndex => 3;
    public int ModifiedSortIndex => 4;

    public string NameSortGlyph => IsNameSort ? (SortDescending ? "\uE70E" : "\uE70D") : string.Empty;
    public string SizeSortGlyph => IsSizeSort ? (SortDescending ? "\uE70E" : "\uE70D") : string.Empty;
    public string PercentSortGlyph => IsPercentSort ? (SortDescending ? "\uE70E" : "\uE70D") : string.Empty;
    public string FilesSortGlyph => IsFilesSort ? (SortDescending ? "\uE70E" : "\uE70D") : string.Empty;
    public string ModifiedSortGlyph => IsModifiedSort ? (SortDescending ? "\uE70E" : "\uE70D") : string.Empty;

    private void NotifySortIndicators()
    {
        OnPropertyChanged(nameof(IsNameSort));
        OnPropertyChanged(nameof(IsSizeSort));
        OnPropertyChanged(nameof(IsPercentSort));
        OnPropertyChanged(nameof(IsFilesSort));
        OnPropertyChanged(nameof(IsModifiedSort));
        OnPropertyChanged(nameof(NameSortGlyph));
        OnPropertyChanged(nameof(SizeSortGlyph));
        OnPropertyChanged(nameof(PercentSortGlyph));
        OnPropertyChanged(nameof(FilesSortGlyph));
        OnPropertyChanged(nameof(ModifiedSortGlyph));
    }

    public Task<IReadOnlyList<DiskNode>> LoadChildrenAsync(DiskNode node, CancellationToken ct)
        => LoadAndSortChildrenAsync(node, ct);

    private async Task<IReadOnlyList<DiskNode>> LoadAndSortChildrenAsync(DiskNode node, CancellationToken ct)
    {
        var children = await _scanService.LoadChildrenAsync(node.FullPath, ct);
        var depth = node.Depth + 1;
        foreach (var child in children)
        {
            ApplyDepth(child, depth);
            child.Parent = node;
        }
        return FilterAndSortNodes(children).ToList();
    }

    private static void ApplyDepth(DiskNode node, int depth)
    {
        node.Depth = depth;
        foreach (var child in node.Children)
        {
            ApplyDepth(child, depth + 1);
        }
    }

    [RelayCommand]
    private void SelectNode(DiskNode? node)
    {
        if (SelectedNode is not null)
        {
            SelectedNode.IsSelected = false;
        }

        SelectedNode = node;
        if (SelectedNode is not null)
        {
            SelectedNode.IsSelected = true;
        }

        ApplyDisplayNodes();
    }

    [RelayCommand]
    private async Task ToggleExpandAndSelectAsync(DiskNode? node)
    {
        if (node is null)
        {
            return;
        }

        SelectNode(node);
        if (!node.IsDirectory)
        {
            return;
        }

        await ToggleExpandAsync(node);
    }

    [RelayCommand]
    private async Task DeleteNodeAsync(DiskNode? node)
    {
        if (node is null)
        {
            return;
        }

        await DeleteNodeInternalAsync(node, permanent: false);
    }

    [RelayCommand]
    private Task DeleteSelectedAsync()
        => DeleteSelectedInternalAsync(permanent: false);

    [RelayCommand]
    private Task DeleteSelectedPermanentAsync()
        => DeleteSelectedInternalAsync(permanent: true);

    private Task DeleteSelectedInternalAsync(bool permanent)
    {
        if (SelectedNode is null)
        {
            return Task.CompletedTask;
        }

        return DeleteNodeInternalAsync(SelectedNode, permanent);
    }

    private async Task DeleteNodeInternalAsync(DiskNode node, bool permanent)
    {
        try
        {
            await _fileDeleteService.DeleteAsync(node.FullPath, permanent, CancellationToken.None);
            RemoveNodeFromTree(node);
            if (ReferenceEquals(SelectedNode, node))
            {
                SelectedNode = null;
            }
            ApplyDisplayNodes();
            StatusText = permanent
                ? $"Eliminado permanentemente: {node.Name}"
                : $"Enviado a la papelera: {node.Name}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete {Path}", node.FullPath);
            ErrorMessage = ex.Message;
        }
    }

    private void RemoveNodeFromTree(DiskNode node)
    {
        if (node.Parent is not null)
        {
            node.Parent.Children.Remove(node);
            return;
        }

        if (RootNode is not null)
        {
            RootNode.Children.Remove(node);
        }
    }

    [RelayCommand]
    private async Task ToggleExpandAsync(DiskNode? node)
    {
        if (node is null || !node.IsDirectory)
        {
            return;
        }

        node.IsExpanded = !node.IsExpanded;
        if (node.IsExpanded && !node.ChildrenLoaded)
        {
            try
            {
                var children = await LoadChildrenAsync(node, CancellationToken.None);
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
                ErrorMessage = ex.Message;
            }
        }

        ApplyDisplayNodes();
    }

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

        var nodes = FilterAndSortNodes(RootNode.Children).ToList();
        foreach (var node in nodes)
        {
            AddFlattenedNode(node);
        }

        HasResults = DisplayNodes.Count > 0;
        ResultsSummary = $"{DisplayNodes.Count:N0} elementos mostrados";
        RootDisplaySize = RootNode.DisplaySize;
        RootDisplayLabel = $"Espacio total analizado: {RootDisplaySize}";

        SortLoadedChildren(RootNode);
    }

    private void AddFlattenedNode(DiskNode node)
    {
        DisplayNodes.Add(node);
        if (!node.IsDirectory || !node.IsExpanded || !node.ChildrenLoaded)
        {
            return;
        }

        var children = FilterAndSortNodes(node.Children).ToList();
        foreach (var child in children)
        {
            AddFlattenedNode(child);
        }
    }

    private IEnumerable<DiskNode> FilterAndSortNodes(IEnumerable<DiskNode> nodes)
    {
        IEnumerable<DiskNode> filtered = nodes;

        if (FilterDirectoriesOnly == true)
        {
            filtered = filtered.Where(n => n.IsDirectory);
        }

        if (FilterLargeOnly == true)
        {
            filtered = filtered.Where(n => n.SizeBytes >= 1_073_741_824);
        }

        return SortNodes(filtered);
    }

    private IEnumerable<DiskNode> SortNodes(IEnumerable<DiskNode> nodes)
    {
        return SelectedSortIndex switch
        {
            1 => SortDescending ? nodes.OrderByDescending(n => n.SizeBytes) : nodes.OrderBy(n => n.SizeBytes),
            2 => SortDescending ? nodes.OrderByDescending(n => n.SizePercent) : nodes.OrderBy(n => n.SizePercent),
            3 => SortDescending ? nodes.OrderByDescending(n => n.FileCount) : nodes.OrderBy(n => n.FileCount),
            4 => SortDescending ? nodes.OrderByDescending(n => n.LastModified) : nodes.OrderBy(n => n.LastModified),
            _ => SortDescending
                ? nodes.OrderByDescending(n => n.Name, StringComparer.OrdinalIgnoreCase)
                : nodes.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
        };
    }

    private void SortLoadedChildren(DiskNode node)
    {
        if (node.ChildrenLoaded && node.Children.Count > 0)
        {
            var sorted = SortNodes(node.Children).ToList();
            node.Children.Clear();
            foreach (var child in sorted)
            {
                node.Children.Add(child);
            }
        }

        foreach (var child in node.Children)
        {
            SortLoadedChildren(child);
        }
    }
}
