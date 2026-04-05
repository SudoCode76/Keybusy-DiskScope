using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;

using Keybusy_DiskScope.Services;
using Keybusy_DiskScope.Services.Implementation;

namespace Keybusy_DiskScope.ViewModels;

public partial class ScanViewModel : ObservableObject
{
    private readonly IScanService _scanService;
    private readonly ISnapshotService _snapshotService;
    private readonly IFileDeleteService _fileDeleteService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<ScanViewModel> _logger;
    private CancellationTokenSource? _scanCts;
    private bool _suppressDefaultSort;
    private readonly HashSet<string> _expandedFileGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<DiskNode> _selectedNodes = new();
    private int _selectionAnchorIndex = -1;
    private int _selectionActiveIndex = -1;

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isSavingSnapshot;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string? _selectedDrive;
    [ObservableProperty] private DiskNode? _rootNode;
    [ObservableProperty] private DiskNode? _selectedNode;
    [ObservableProperty] private string _snapshotName = string.Empty;
    [ObservableProperty] private string _savedSnapshotMessage = string.Empty;
    [ObservableProperty] private bool _isSaveTipOpen;
    [ObservableProperty] private string _scanTitle = "Analisis";
    [ObservableProperty] private string _scanSummary = string.Empty;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private string _resultsSummary = string.Empty;
    [ObservableProperty] private string _rootDisplaySize = string.Empty;
    [ObservableProperty] private string _rootDisplayLabel = string.Empty;
    [ObservableProperty] private int _selectedSortIndex;
    [ObservableProperty] private bool _sortDescending;
    [ObservableProperty] private bool _showSizeColumn = true;
    [ObservableProperty] private bool _showPercentColumn = true;
    [ObservableProperty] private bool _showFilesColumn = true;
    [ObservableProperty] private string _scanEngineLabel = "Motor: pendiente";
    [ObservableProperty] private string _scanEngineDetail = string.Empty;

    public bool HasError => ErrorMessage is not null;
    public bool IsNotScanning => !IsScanning;

    public ObservableCollection<string> AvailableDrives { get; } = new();
    public ObservableCollection<DiskNode> DisplayNodes { get; } = new();
    public ObservableCollection<string> SortOptions { get; } = new()
    {
        "Nombre",
        "Tamano",
        "Ocupacion",
        "Archivos"
    };

    public ScanViewModel(
        IScanService scanService,
        ISnapshotService snapshotService,
        IFileDeleteService fileDeleteService,
        ISettingsService settingsService,
        ILogger<ScanViewModel> logger)
    {
        _scanService = scanService;
        _snapshotService = snapshotService;
        _fileDeleteService = fileDeleteService;
        _settingsService = settingsService;
        _logger = logger;

        StatusText = "Seleccione una unidad y pulse Escanear.";
        ScanEngineLabel = "Motor: pendiente";
        ApplyDefaultSort();
        if (_settingsService is INotifyPropertyChanged notifier)
        {
            notifier.PropertyChanged += OnSettingsChanged;
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(ISettingsService.DefaultSortIndex), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(ISettingsService.DefaultSortDescending), StringComparison.Ordinal))
        {
            ApplyDefaultSort();
        }
    }

    private void ApplyDefaultSort()
    {
        var index = _settingsService.DefaultSortIndex;
        if (index < 0 || index >= SortOptions.Count)
        {
            index = 0;
        }

        _suppressDefaultSort = true;
        SelectedSortIndex = index;
        SortDescending = _settingsService.DefaultSortDescending;
        _suppressDefaultSort = false;
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
        ScanEngineLabel = "Motor: detectando...";
        ScanEngineDetail = string.Empty;

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            StatusText = "Analizando...";
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
            _expandedFileGroups.Clear();
            ApplySizePercentages(RootNode);
            UpdateScanEngineStatus();

            stopwatch.Stop();
            StatusText = $"Escaneo completado: {RootNode.DisplaySize} en {SelectedDrive} ({ScanEngineLabel})";
            ScanSummary = $"Escaneo completado en {stopwatch.Elapsed.TotalSeconds:0} segundos. {RootNode.DisplaySize} analizados. {ScanEngineLabel}.";

            SnapshotName = $"Escaneo {DateTime.Now:dd/MM/yyyy HH:mm}";
        }
        catch (OperationCanceledException)
        {
            UpdateScanEngineStatus();
            StatusText = "Escaneo cancelado.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scan failed for {Drive}", SelectedDrive);
            UpdateScanEngineStatus();
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

        IsSavingSnapshot = true;
        await Task.Yield();
        try
        {
            var treeJson = await Task.Run(() => SnapshotService.SerializeTree(RootNode));
            var record = new SnapshotRecord
            {
                Name          = SnapshotName,
                DrivePath     = SelectedDrive ?? string.Empty,
                CreatedAt     = DateTime.Now,
                TotalSizeBytes = RootNode.SizeBytes,
                TreeJson      = treeJson
            };

            await _snapshotService.SaveAsync(record);
            SavedSnapshotMessage = $"Snapshot '{SnapshotName}' guardado.";
            StatusText = SavedSnapshotMessage;
            IsSaveTipOpen = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save snapshot");
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsSavingSnapshot = false;
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
        if (!_suppressDefaultSort)
        {
            SortDescending = true;
        }
        ApplyDisplayNodes();
        NotifySortIndicators();
    }

    partial void OnSortDescendingChanged(bool value)
    {
        ApplyDisplayNodes();
        NotifySortIndicators();
    }

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

    public int NameSortIndex => 0;
    public int SizeSortIndex => 1;
    public int PercentSortIndex => 2;
    public int FilesSortIndex => 3;

    public string NameSortGlyph => IsNameSort ? (SortDescending ? "\uE70E" : "\uE70D") : string.Empty;
    public string SizeSortGlyph => IsSizeSort ? (SortDescending ? "\uE70E" : "\uE70D") : string.Empty;
    public string PercentSortGlyph => IsPercentSort ? (SortDescending ? "\uE70E" : "\uE70D") : string.Empty;
    public string FilesSortGlyph => IsFilesSort ? (SortDescending ? "\uE70E" : "\uE70D") : string.Empty;

    private void NotifySortIndicators()
    {
        OnPropertyChanged(nameof(IsNameSort));
        OnPropertyChanged(nameof(IsSizeSort));
        OnPropertyChanged(nameof(IsPercentSort));
        OnPropertyChanged(nameof(IsFilesSort));
        OnPropertyChanged(nameof(NameSortGlyph));
        OnPropertyChanged(nameof(SizeSortGlyph));
        OnPropertyChanged(nameof(PercentSortGlyph));
        OnPropertyChanged(nameof(FilesSortGlyph));
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
        return FilterAndSortNodes(children, allowFileGrouping: true).ToList();
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
        => SetSingleSelection(node);

    public bool IsNodeSelected(DiskNode? node)
        => node is not null && _selectedNodes.Contains(node);

    public void SelectNodeWithModifiers(DiskNode? node, bool isCtrlPressed, bool isShiftPressed)
    {
        if (node is null)
        {
            if (!isCtrlPressed && !isShiftPressed)
            {
                ClearSelection();
            }

            return;
        }

        var nodeIndex = FindNodeIndex(node);
        if (nodeIndex < 0)
        {
            SetSingleSelection(node);
            return;
        }

        if (isShiftPressed)
        {
            if (_selectionAnchorIndex < 0)
            {
                _selectionAnchorIndex = _selectionActiveIndex >= 0 ? _selectionActiveIndex : nodeIndex;
            }

            SelectRange(_selectionAnchorIndex, nodeIndex);
            _selectionActiveIndex = nodeIndex;
            return;
        }

        if (isCtrlPressed)
        {
            ToggleSelection(node, nodeIndex);
            return;
        }

        SetSingleSelection(node, nodeIndex);
    }

    public void ExtendSelectionByOffset(int offset)
    {
        if (DisplayNodes.Count == 0 || offset == 0)
        {
            return;
        }

        if (_selectionAnchorIndex < 0)
        {
            var start = offset > 0 ? 0 : DisplayNodes.Count - 1;
            SetSingleSelection(DisplayNodes[start], start);
            return;
        }

        if (_selectionActiveIndex < 0)
        {
            _selectionActiveIndex = _selectionAnchorIndex;
        }

        var targetIndex = Math.Clamp(_selectionActiveIndex + offset, 0, DisplayNodes.Count - 1);
        SelectRange(_selectionAnchorIndex, targetIndex);
        _selectionActiveIndex = targetIndex;
    }

    private void SetSingleSelection(DiskNode? node, int nodeIndex = -1)
    {
        ClearSelection();

        if (node is null)
        {
            return;
        }

        node.IsSelected = true;
        _selectedNodes.Add(node);
        SelectedNode = node;

        if (nodeIndex < 0)
        {
            nodeIndex = FindNodeIndex(node);
        }

        _selectionAnchorIndex = nodeIndex;
        _selectionActiveIndex = nodeIndex;
        ApplyDisplayNodes();
    }

    private void ToggleSelection(DiskNode node, int nodeIndex)
    {
        if (_selectedNodes.Remove(node))
        {
            node.IsSelected = false;
            if (_selectedNodes.Count == 0)
            {
                SelectedNode = null;
                _selectionAnchorIndex = -1;
                _selectionActiveIndex = -1;
            }
            else if (ReferenceEquals(SelectedNode, node))
            {
                SelectedNode = _selectedNodes.Last();
            }
        }
        else
        {
            node.IsSelected = true;
            _selectedNodes.Add(node);
            SelectedNode = node;
            if (_selectionAnchorIndex < 0)
            {
                _selectionAnchorIndex = nodeIndex;
            }
            _selectionActiveIndex = nodeIndex;
        }

        ApplyDisplayNodes();
    }

    private void SelectRange(int startIndex, int endIndex)
    {
        if (DisplayNodes.Count == 0)
        {
            return;
        }

        var from = Math.Min(startIndex, endIndex);
        var to = Math.Max(startIndex, endIndex);

        ClearSelection();
        for (var i = from; i <= to; i += 1)
        {
            var node = DisplayNodes[i];
            node.IsSelected = true;
            _selectedNodes.Add(node);
        }

        SelectedNode = DisplayNodes[endIndex];
        ApplyDisplayNodes();
    }

    private void ClearSelection()
    {
        foreach (var selected in _selectedNodes)
        {
            selected.IsSelected = false;
        }

        _selectedNodes.Clear();
        SelectedNode = null;
        _selectionAnchorIndex = -1;
        _selectionActiveIndex = -1;
        ApplyDisplayNodes();
    }

    [RelayCommand]
    private async Task ToggleExpandAndSelectAsync(DiskNode? node)
    {
        if (node is null)
        {
            return;
        }
        if (!node.IsDirectory && !node.IsFileGroup)
        {
            return;
        }

        await ToggleExpandAsync(node);
    }

    [RelayCommand]
    private async Task DeleteNodeAsync(DiskNode? node)
    {
        if (node is null || node.IsFileGroup)
        {
            return;
        }

        if (_selectedNodes.Contains(node) && _selectedNodes.Count > 1)
        {
            await DeleteNodesInternalAsync(GetSelectedActionableNodes(), permanent: false);
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
        var selectedNodes = GetSelectedActionableNodes();
        if (selectedNodes.Count > 0)
        {
            return DeleteNodesInternalAsync(selectedNodes, permanent);
        }

        if (SelectedNode is null || SelectedNode.IsFileGroup)
        {
            return Task.CompletedTask;
        }

        return DeleteNodeInternalAsync(SelectedNode, permanent);
    }

    private async Task DeleteNodesInternalAsync(IReadOnlyList<DiskNode> nodes, bool permanent)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        var deletedCount = 0;
        Exception? lastError = null;
        foreach (var node in nodes.OrderByDescending(n => n.Depth))
        {
            try
            {
                await _fileDeleteService.DeleteAsync(node.FullPath, permanent, CancellationToken.None);
                RemoveNodeFromTree(node);
                _selectedNodes.Remove(node);
                deletedCount += 1;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete {Path}", node.FullPath);
                lastError = ex;
            }
        }

        SelectedNode = _selectedNodes.Count > 0 ? _selectedNodes.Last() : null;
        ApplyDisplayNodes();

        if (lastError is not null)
        {
            ErrorMessage = lastError.Message;
        }

        if (deletedCount > 0)
        {
            StatusText = permanent
                ? $"Eliminados permanentemente: {deletedCount:N0} elemento(s)"
                : $"Enviados a la papelera: {deletedCount:N0} elemento(s)";
        }
    }

    public string BuildCurrentScanCsv()
    {
        if (RootNode is null)
        {
            throw new InvalidOperationException("No hay resultados para exportar.");
        }

        var builder = new StringBuilder(capacity: 1_024 * 1_024);
        builder.AppendLine("Tipo,Profundidad,TamanoBytes,Tamano,Archivos,Carpetas,UltimaModificacion,Ruta,Nombre");

        foreach (var node in EnumerateNodes(RootNode))
        {
            if (node.IsPlaceholder || node.IsFileGroup)
            {
                continue;
            }

            var type = node.IsDirectory ? "Carpeta" : "Archivo";
            var lastModified = node.LastModified == DateTime.MinValue
                ? string.Empty
                : node.LastModified.ToString("yyyy-MM-dd HH:mm:ss");

            AppendCsvField(builder, type);
            builder.Append(',');
            AppendCsvField(builder, node.Depth.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            AppendCsvField(builder, node.SizeBytes.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            AppendCsvField(builder, node.DisplaySize);
            builder.Append(',');
            AppendCsvField(builder, node.FileCount.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            AppendCsvField(builder, node.FolderCount.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            AppendCsvField(builder, lastModified);
            builder.Append(',');
            AppendCsvField(builder, node.FullPath);
            builder.Append(',');
            AppendCsvField(builder, node.Name);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static IEnumerable<DiskNode> EnumerateNodes(DiskNode root)
    {
        var stack = new Stack<DiskNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;

            for (var i = node.Children.Count - 1; i >= 0; i -= 1)
            {
                stack.Push(node.Children[i]);
            }
        }
    }

    private static void AppendCsvField(StringBuilder builder, string? value)
    {
        var normalized = value ?? string.Empty;
        var escaped = normalized.Replace("\"", "\"\"");
        builder.Append('"');
        builder.Append(escaped);
        builder.Append('"');
    }

    private IReadOnlyList<DiskNode> GetSelectedActionableNodes()
    {
        var candidates = _selectedNodes
            .Where(node => node.IsActionable)
            .ToList();

        if (candidates.Count <= 1)
        {
            return candidates;
        }

        var selectedSet = new HashSet<DiskNode>(candidates);
        return candidates
            .Where(node => !HasSelectedAncestor(node, selectedSet))
            .ToList();
    }

    private static bool HasSelectedAncestor(DiskNode node, IReadOnlySet<DiskNode> selectedSet)
    {
        var parent = node.Parent;
        while (parent is not null)
        {
            if (selectedSet.Contains(parent))
            {
                return true;
            }

            parent = parent.Parent;
        }

        return false;
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
        if (node is null || (!node.IsDirectory && !node.IsFileGroup))
        {
            return;
        }

        if (node.IsExpanded)
        {
            node.IsExpanded = false;
            if (node.IsFileGroup)
            {
                _expandedFileGroups.Remove(node.FullPath);
            }
            RemoveDescendantsFromDisplay(node);
            if (node.IsFileGroup)
            {
                ResetGroupedChildrenDepth(node);
            }
            return;
        }

        node.IsExpanded = true;
        if (node.IsFileGroup)
        {
            _expandedFileGroups.Add(node.FullPath);
        }
        if (!node.ChildrenLoaded)
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
                return;
            }
        }

        InsertChildrenIntoDisplay(node);
    }

    private void ApplyDisplayNodes()
    {
        var selectedKeys = _selectedNodes.Select(GetSelectionKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedNodeKey = SelectedNode is null ? null : GetSelectionKey(SelectedNode);

        DisplayNodes.Clear();

        if (RootNode is null)
        {
            HasResults = false;
            ResultsSummary = string.Empty;
            RootDisplaySize = string.Empty;
            RootDisplayLabel = string.Empty;
            return;
        }

        var nodes = FilterAndSortNodes(RootNode.Children, allowFileGrouping: true).ToList();
        foreach (var node in nodes)
        {
            AddFlattenedNode(node);
        }

        RestoreSelectionAfterRefresh(selectedKeys, selectedNodeKey);

        HasResults = DisplayNodes.Count > 0;
        ResultsSummary = $"{DisplayNodes.Count:N0} elementos mostrados";
        RootDisplaySize = RootNode.DisplaySize;
        RootDisplayLabel = $"Espacio total analizado: {RootDisplaySize}";

        SortLoadedChildren(RootNode);
    }

    private void RestoreSelectionAfterRefresh(IReadOnlySet<string> selectedKeys, string? selectedNodeKey)
    {
        _selectedNodes.Clear();
        SelectedNode = null;

        if (selectedKeys.Count == 0)
        {
            _selectionAnchorIndex = -1;
            _selectionActiveIndex = -1;
            return;
        }

        for (var i = 0; i < DisplayNodes.Count; i += 1)
        {
            var node = DisplayNodes[i];
            var key = GetSelectionKey(node);
            var isSelected = selectedKeys.Contains(key);
            node.IsSelected = isSelected;
            if (!isSelected)
            {
                continue;
            }

            _selectedNodes.Add(node);
            if (selectedNodeKey is not null
                && string.Equals(selectedNodeKey, key, StringComparison.OrdinalIgnoreCase))
            {
                SelectedNode = node;
                _selectionActiveIndex = i;
            }
        }

        if (_selectedNodes.Count == 0)
        {
            _selectionAnchorIndex = -1;
            _selectionActiveIndex = -1;
            return;
        }

        if (SelectedNode is null)
        {
            SelectedNode = _selectedNodes.First();
            _selectionActiveIndex = FindNodeIndex(SelectedNode);
        }

        _selectionAnchorIndex = _selectionActiveIndex;
    }

    private static string GetSelectionKey(DiskNode node)
        => $"{node.FullPath}|{node.Name}|{node.IsFileGroup}";

    private void InsertChildrenIntoDisplay(DiskNode node)
    {
        var parentIndex = FindNodeIndex(node);
        if (parentIndex < 0)
        {
            ApplyDisplayNodes();
            return;
        }

        var children = FilterAndSortNodes(node.Children, allowFileGrouping: !node.IsFileGroup).ToList();
        if (node.IsFileGroup)
        {
            foreach (var child in children)
            {
                child.Depth = node.Depth + 1;
            }
        }
        var insertIndex = parentIndex + 1;
        InsertFlattenedNodes(children, ref insertIndex);
    }

    private void InsertFlattenedNodes(IEnumerable<DiskNode> nodes, ref int insertIndex)
    {
        foreach (var child in nodes)
        {
            DisplayNodes.Insert(insertIndex, child);
            insertIndex += 1;

            if ((child.IsDirectory || child.IsFileGroup) && child.IsExpanded && child.ChildrenLoaded)
            {
                var grandchildren = FilterAndSortNodes(child.Children, allowFileGrouping: !child.IsFileGroup).ToList();
                if (child.IsFileGroup)
                {
                    foreach (var grandchild in grandchildren)
                    {
                        grandchild.Depth = child.Depth + 1;
                    }
                }
                InsertFlattenedNodes(grandchildren, ref insertIndex);
            }
        }
    }

    private void RemoveDescendantsFromDisplay(DiskNode node)
    {
        var parentIndex = FindNodeIndex(node);
        if (parentIndex < 0)
        {
            ApplyDisplayNodes();
            return;
        }

        var index = parentIndex + 1;
        while (index < DisplayNodes.Count && DisplayNodes[index].Depth > node.Depth)
        {
            DisplayNodes.RemoveAt(index);
        }
    }

    private int FindNodeIndex(DiskNode node)
    {
        for (var i = 0; i < DisplayNodes.Count; i += 1)
        {
            if (ReferenceEquals(DisplayNodes[i], node))
            {
                return i;
            }
        }

        return -1;
    }

    private void AddFlattenedNode(DiskNode node)
    {
        DisplayNodes.Add(node);
        if ((!node.IsDirectory && !node.IsFileGroup) || !node.IsExpanded || !node.ChildrenLoaded)
        {
            return;
        }

        var children = FilterAndSortNodes(node.Children, allowFileGrouping: !node.IsFileGroup).ToList();
        if (node.IsFileGroup)
        {
            foreach (var child in children)
            {
                child.Depth = node.Depth + 1;
            }
        }
        foreach (var child in children)
        {
            AddFlattenedNode(child);
        }
    }

    private IEnumerable<DiskNode> FilterAndSortNodes(IEnumerable<DiskNode> nodes, bool allowFileGrouping)
        => SortNodes(allowFileGrouping ? GroupFilesForDisplay(nodes) : nodes);

    private IReadOnlyList<DiskNode> GroupFilesForDisplay(IEnumerable<DiskNode> nodes)
    {
        var siblingDepth = 0;
        var hasDepth = false;
        var placeholders = new List<DiskNode>();
        var directories = new List<DiskNode>();
        var files = new List<DiskNode>();

        foreach (var node in nodes)
        {
            if (node.IsPlaceholder)
            {
                placeholders.Add(node);
                continue;
            }

            if (!hasDepth)
            {
                siblingDepth = node.Depth;
                hasDepth = true;
            }

            if (node.IsDirectory || node.IsFileGroup)
            {
                directories.Add(node);
                continue;
            }

            files.Add(node);
        }

        var result = new List<DiskNode>(directories.Count + placeholders.Count + 1);
        result.AddRange(directories);

        if (files.Count > 1)
        {
            result.Add(CreateGroupedFilesNode(files, siblingDepth));
        }
        else if (files.Count == 1)
        {
            result.Add(files[0]);
        }

        result.AddRange(placeholders);
        return result;
    }

    private static void ResetGroupedChildrenDepth(DiskNode groupedNode)
    {
        foreach (var child in groupedNode.Children)
        {
            child.Depth = groupedNode.Depth;
        }
    }

    private DiskNode CreateGroupedFilesNode(IReadOnlyList<DiskNode> files, int siblingDepth)
    {
        long totalSize = 0;
        double totalPercent = 0;
        var lastModified = DateTime.MinValue;

        foreach (var file in files)
        {
            totalSize += file.SizeBytes;
            totalPercent += file.SizePercent;
            if (file.LastModified > lastModified)
            {
                lastModified = file.LastModified;
            }
        }

        var representativePath = files[0].FullPath;
        var parentPath = Path.GetDirectoryName(representativePath) ?? representativePath;

        var groupedNode = new DiskNode
        {
            Name = $"[{files.Count:N0} Archivos]",
            FullPath = parentPath,
            IsDirectory = false,
            IsFileGroup = true,
            SizeBytes = totalSize,
            SizePercent = Math.Clamp(totalPercent, 0, 100),
            FileCount = files.Count,
            LastModified = lastModified,
            Depth = siblingDepth,
            HasChildren = files.Count > 0,
            ChildrenLoaded = true,
            IsExpanded = _expandedFileGroups.Contains(parentPath)
        };

        foreach (var file in files)
        {
            groupedNode.Children.Add(file);
        }

        return groupedNode;
    }

    private IEnumerable<DiskNode> SortNodes(IEnumerable<DiskNode> nodes)
    {
        return SelectedSortIndex switch
        {
            1 => SortDescending ? nodes.OrderByDescending(n => n.SizeBytes) : nodes.OrderBy(n => n.SizeBytes),
            2 => SortDescending ? nodes.OrderByDescending(n => n.SizePercent) : nodes.OrderBy(n => n.SizePercent),
            3 => SortDescending ? nodes.OrderByDescending(n => n.FileCount) : nodes.OrderBy(n => n.FileCount),
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

    private void UpdateScanEngineStatus()
    {
        ScanEngineLabel = _scanService.LastScanEngineType switch
        {
            ScanEngineType.FastNtfs => "Motor: NTFS rapido",
            ScanEngineType.FastNtfsFallbackClassic => "Motor: Clasico (fallback)",
            ScanEngineType.Classic => "Motor: Clasico",
            _ => "Motor: desconocido"
        };

        ScanEngineDetail = _scanService.LastScanEngineDetail;
    }
}
