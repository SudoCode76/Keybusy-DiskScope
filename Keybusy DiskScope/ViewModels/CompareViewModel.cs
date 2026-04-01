using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using Keybusy_DiskScope.Models;
using Keybusy_DiskScope.Services;
using Keybusy_DiskScope.Services.Implementation;

namespace Keybusy_DiskScope.ViewModels;

public partial class CompareViewModel : ObservableObject
{
    private readonly ISnapshotService _snapshotService;
    private readonly IDiffService _diffService;
    private readonly IFileDeleteService _fileDeleteService;
    private readonly INavigationService _navigationService;
    private readonly IScanService _scanService;
    private readonly ILogger<CompareViewModel> _logger;

    private SnapshotRecord? _currentSnapshot;
    private readonly HashSet<string> _selectedRowPaths = new(StringComparer.OrdinalIgnoreCase);
    private string? _selectionAnchorPath;
    private int _selectionActiveIndex = -1;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private SnapshotRecord? _snapshotBefore;
    [ObservableProperty] private string? _selectedBaseDrive;
    [ObservableProperty] private bool _saveCurrentAsSnapshot;
    [ObservableProperty] private DiffNode? _diffRoot;
    [ObservableProperty] private DiffRow? _selectedRow;
    [ObservableProperty] private int _selectedSortIndex;

    public bool HasError => ErrorMessage is not null;
    public bool HasResult => DiffRoot is not null;
    public bool IsNotLoading => !IsLoading;
    public bool HasBaseSnapshots => FilteredSnapshots.Count > 0;
    public bool HasNoBaseSnapshots => !HasBaseSnapshots;

    public ObservableCollection<SnapshotRecord> AvailableSnapshots { get; } = new();
    public ObservableCollection<SnapshotRecord> FilteredSnapshots { get; } = new();
    public ObservableCollection<string> AvailableDrives { get; } = new();
    public ObservableCollection<DiffRow> DisplayRows { get; } = new();

    public IReadOnlyList<string> SortOptions { get; } = new[]
    {
        "Mayor diferencia",
        "Nombre",
        "Tamano actual",
        "Fecha de modificacion"
    };

    public CompareViewModel(
        ISnapshotService snapshotService,
        IDiffService diffService,
        IFileDeleteService fileDeleteService,
        INavigationService navigationService,
        IScanService scanService,
        ILogger<CompareViewModel> logger)
    {
        _snapshotService = snapshotService;
        _diffService = diffService;
        _fileDeleteService = fileDeleteService;
        _navigationService = navigationService;
        _scanService = scanService;
        _logger = logger;
    }

    [RelayCommand]
    private async Task LoadSnapshotsAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        await Task.Yield();
        try
        {
            var records = await _snapshotService.GetAllAsync();
            AvailableSnapshots.Clear();
            foreach (var r in records)
                AvailableSnapshots.Add(r);

            LoadDrivesFromSnapshots(records);
            if (AvailableDrives.Count > 0 && string.IsNullOrWhiteSpace(SelectedBaseDrive))
            {
                SelectedBaseDrive = AvailableDrives[0];
            }
            FilterSnapshots();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load snapshots for comparison");
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RunCompareAsync()
    {
        if (SnapshotBefore is null || string.IsNullOrWhiteSpace(SelectedBaseDrive))
        {
            return;
        }

        ErrorMessage = null;
        IsLoading = true;
        await Task.Yield();
        try
        {
            var currentSnapshot = await BuildCurrentSnapshotAsync(SelectedBaseDrive);
            _currentSnapshot = currentSnapshot;
            DiffRoot = await Task.Run(() => _diffService.Compare(SnapshotBefore, currentSnapshot));
            ResetExpansion(DiffRoot);
            BuildRows();

            if (SaveCurrentAsSnapshot)
            {
                await _snapshotService.SaveAsync(currentSnapshot);
                InsertSnapshotRecord(currentSnapshot);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Comparison failed");
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnErrorMessageChanged(string? value)
        => OnPropertyChanged(nameof(HasError));

    partial void OnDiffRootChanged(DiffNode? value)
        => OnPropertyChanged(nameof(HasResult));

    partial void OnIsLoadingChanged(bool value)
        => OnPropertyChanged(nameof(IsNotLoading));

    partial void OnSelectedSortIndexChanged(int value)
        => BuildRows();

    partial void OnSnapshotBeforeChanged(SnapshotRecord? value)
    {
        if (_currentSnapshot is null || value is null)
        {
            return;
        }

        _ = RecompareWithCurrentAsync();
    }

    partial void OnSelectedBaseDriveChanged(string? value)
    {
        FilterSnapshots();
        _currentSnapshot = null;
        DiffRoot = null;
        DisplayRows.Clear();
        _selectedRowPaths.Clear();
        _selectionAnchorPath = null;
        _selectionActiveIndex = -1;
        SelectedRow = null;
    }

    private void BuildRows()
    {
        if (SelectedRow is not null)
        {
            _selectedRowPaths.Add(SelectedRow.Node.FullPath);
        }

        DisplayRows.Clear();

        if (DiffRoot is null)
        {
            SelectedRow = null;
            _selectedRowPaths.Clear();
            _selectionAnchorPath = null;
            _selectionActiveIndex = -1;
            return;
        }

        var roots = CreateRowsFromNodes(DiffRoot.Children, depth: 0);
        foreach (var row in roots)
        {
            DisplayRows.Add(row);
            if (row.HasChildren && row.IsExpanded)
            {
                InsertChildren(row);
            }
        }

        RestoreSelectionAfterRefresh();
    }

    private void RestoreSelectionAfterRefresh()
    {
        if (_selectedRowPaths.Count == 0)
        {
            SelectedRow = null;
            _selectionActiveIndex = -1;
            return;
        }

        SelectedRow = null;
        var matchedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < DisplayRows.Count; i += 1)
        {
            var row = DisplayRows[i];
            var isMatch = _selectedRowPaths.Contains(row.Node.FullPath);
            row.IsSelected = isMatch;
            if (!isMatch)
            {
                continue;
            }

            matchedPaths.Add(row.Node.FullPath);
            if (_selectionAnchorPath is not null
                && string.Equals(_selectionAnchorPath, row.Node.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                _selectionActiveIndex = i;
            }

            if (SelectedRow is null)
            {
                SelectedRow = row;
            }
        }

        _selectedRowPaths.Clear();
        foreach (var path in matchedPaths)
        {
            _selectedRowPaths.Add(path);
        }

        if (_selectedRowPaths.Count == 0)
        {
            _selectionAnchorPath = null;
            _selectionActiveIndex = -1;
        }
        else if (_selectionAnchorPath is null || !_selectedRowPaths.Contains(_selectionAnchorPath))
        {
            _selectionAnchorPath = SelectedRow?.Node.FullPath;
            _selectionActiveIndex = SelectedRow is null ? -1 : DisplayRows.IndexOf(SelectedRow);
        }
    }

    private IEnumerable<DiffRow> CreateRowsFromNodes(IEnumerable<DiffNode> nodes, int depth)
    {
        var rows = new List<DiffRow>();
        foreach (var node in nodes)
        {
            rows.Add(new DiffRow(node, depth));
        }

        return ApplySort(rows).ToList();
    }

    private static void ResetExpansion(DiffNode node)
    {
        node.IsExpanded = false;
        foreach (var child in node.Children)
        {
            ResetExpansion(child);
        }
    }

    private IEnumerable<DiffRow> ApplySort(IEnumerable<DiffRow> rows)
    {
        return SelectedSortIndex switch
        {
            1 => rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            2 => rows.OrderByDescending(r => r.SizeAfter),
            3 => rows.OrderByDescending(r => r.LastModifiedEffective),
            _ => rows.OrderByDescending(r => Math.Abs(r.SizeDelta))
        };
    }

    [RelayCommand]
    private void SelectRow(DiffRow? row)
        => SetSingleSelection(row);

    public bool IsRowSelected(DiffRow? row)
        => row is not null && row.IsSelected;

    public void SelectRowWithModifiers(DiffRow? row, bool isCtrlPressed, bool isShiftPressed)
    {
        if (row is null)
        {
            if (!isCtrlPressed && !isShiftPressed)
            {
                ClearSelection();
            }

            return;
        }

        var rowIndex = DisplayRows.IndexOf(row);
        if (rowIndex < 0)
        {
            SetSingleSelection(row);
            return;
        }

        if (isShiftPressed)
        {
            if (_selectionAnchorPath is null)
            {
                _selectionAnchorPath = SelectedRow?.Node.FullPath ?? row.Node.FullPath;
            }

            SelectRange(_selectionAnchorPath, rowIndex);
            return;
        }

        if (isCtrlPressed)
        {
            ToggleSelection(row, rowIndex);
            return;
        }

        SetSingleSelection(row, rowIndex);
    }

    public void ExtendSelectionByOffset(int offset)
    {
        if (DisplayRows.Count == 0 || offset == 0)
        {
            return;
        }

        if (_selectionAnchorPath is null)
        {
            var start = offset > 0 ? 0 : DisplayRows.Count - 1;
            SetSingleSelection(DisplayRows[start], start);
            return;
        }

        if (_selectionActiveIndex < 0)
        {
            _selectionActiveIndex = FindRowIndexByPath(_selectionAnchorPath);
            if (_selectionActiveIndex < 0)
            {
                _selectionActiveIndex = 0;
            }
        }

        var targetIndex = Math.Clamp(_selectionActiveIndex + offset, 0, DisplayRows.Count - 1);
        SelectRange(_selectionAnchorPath, targetIndex);
    }

    private void SetSingleSelection(DiffRow? row, int rowIndex = -1)
    {
        ClearSelection();

        if (row is null)
        {
            return;
        }

        row.IsSelected = true;
        SelectedRow = row;
        _selectedRowPaths.Add(row.Node.FullPath);
        _selectionAnchorPath = row.Node.FullPath;

        if (rowIndex < 0)
        {
            rowIndex = DisplayRows.IndexOf(row);
        }

        _selectionActiveIndex = rowIndex;
    }

    private void ToggleSelection(DiffRow row, int rowIndex)
    {
        if (_selectedRowPaths.Contains(row.Node.FullPath))
        {
            _selectedRowPaths.Remove(row.Node.FullPath);
            row.IsSelected = false;

            if (SelectedRow == row)
            {
                SelectedRow = DisplayRows.FirstOrDefault(current => current.IsSelected);
            }

            if (_selectedRowPaths.Count == 0)
            {
                _selectionAnchorPath = null;
                _selectionActiveIndex = -1;
            }
        }
        else
        {
            _selectedRowPaths.Add(row.Node.FullPath);
            row.IsSelected = true;
            SelectedRow = row;
            _selectionActiveIndex = rowIndex;
            _selectionAnchorPath ??= row.Node.FullPath;
        }
    }

    private void SelectRange(string anchorPath, int targetIndex)
    {
        var anchorIndex = FindRowIndexByPath(anchorPath);

        if (anchorIndex < 0)
        {
            anchorIndex = targetIndex;
            _selectionAnchorPath = DisplayRows[targetIndex].Node.FullPath;
        }

        var from = Math.Min(anchorIndex, targetIndex);
        var to = Math.Max(anchorIndex, targetIndex);

        _selectedRowPaths.Clear();
        foreach (var displayRow in DisplayRows)
        {
            displayRow.IsSelected = false;
        }

        for (var i = from; i <= to; i += 1)
        {
            var row = DisplayRows[i];
            row.IsSelected = true;
            _selectedRowPaths.Add(row.Node.FullPath);
        }

        SelectedRow = DisplayRows[targetIndex];
        _selectionActiveIndex = targetIndex;
        _selectionAnchorPath ??= SelectedRow.Node.FullPath;
    }

    private void ClearSelection()
    {
        foreach (var row in DisplayRows)
        {
            row.IsSelected = false;
        }

        _selectedRowPaths.Clear();
        _selectionAnchorPath = null;
        _selectionActiveIndex = -1;
        SelectedRow = null;
    }

    private int FindRowIndexByPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return -1;
        }

        for (var i = 0; i < DisplayRows.Count; i += 1)
        {
            if (string.Equals(DisplayRows[i].Node.FullPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    [RelayCommand]
    private Task ToggleExpandAndSelectAsync(DiffRow? row)
    {
        if (row is null)
        {
            return Task.CompletedTask;
        }
        if (!row.HasChildren)
        {
            return Task.CompletedTask;
        }

        row.IsExpanded = !row.IsExpanded;
        BuildRows();

        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task DeleteRowAsync(DiffRow? row)
    {
        if (row is null)
        {
            return;
        }

        if (row.IsSelected && _selectedRowPaths.Count > 1)
        {
            await DeleteRowsInternalAsync(GetSelectedRowsForDeletion(), permanent: false);
            return;
        }

        if (row.Status == DiffStatus.Removed)
        {
            return;
        }

        await DeleteRowsInternalAsync(new[] { row }, permanent: false);
    }

    [RelayCommand]
    private Task DeleteSelectedRowsAsync()
        => DeleteRowsInternalAsync(GetSelectedRowsForDeletion(), permanent: false);

    [RelayCommand]
    private Task DeleteSelectedRowsPermanentAsync()
        => DeleteRowsInternalAsync(GetSelectedRowsForDeletion(), permanent: true);

    private async Task DeleteRowsInternalAsync(IReadOnlyList<DiffRow> rows, bool permanent)
    {
        if (rows.Count == 0)
        {
            return;
        }

        Exception? lastError = null;
        var deletedCount = 0;
        foreach (var row in rows.OrderByDescending(r => r.Depth))
        {
            if (row.Status == DiffStatus.Removed)
            {
                continue;
            }

            try
            {
                await _fileDeleteService.DeleteAsync(row.Node.FullPath, permanent, CancellationToken.None);
                RemoveFromTree(row.Node);
                _selectedRowPaths.Remove(row.Node.FullPath);
                deletedCount += 1;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete {Path}", row.Node.FullPath);
                lastError = ex;
            }
        }

        BuildRows();

        if (deletedCount == 0)
        {
            if (lastError is not null)
            {
                ErrorMessage = lastError.Message;
            }

            return;
        }

        if (lastError is not null)
        {
            ErrorMessage = lastError.Message;
        }
    }

    private IReadOnlyList<DiffRow> GetSelectedRowsForDeletion()
    {
        var selectedRows = DisplayRows
            .Where(row => row.IsSelected && row.CanDelete)
            .ToList();

        if (selectedRows.Count <= 1)
        {
            return selectedRows;
        }

        var selectedNodes = new HashSet<DiffNode>(selectedRows.Select(row => row.Node));
        return selectedRows
            .Where(row => !HasSelectedAncestor(row.Node, selectedNodes))
            .ToList();
    }

    private static bool HasSelectedAncestor(DiffNode node, IReadOnlySet<DiffNode> selectedNodes)
    {
        var parent = node.Parent;
        while (parent is not null)
        {
            if (selectedNodes.Contains(parent))
            {
                return true;
            }

            parent = parent.Parent;
        }

        return false;
    }


    private void InsertChildren(DiffRow parentRow)
    {
        var index = DisplayRows.IndexOf(parentRow);
        if (index < 0)
        {
            BuildRows();
            return;
        }

        var children = CreateRowsFromNodes(parentRow.Node.Children, parentRow.Depth + 1).ToList();
        var insertIndex = index + 1;
        InsertFlattened(children, ref insertIndex);
    }

    private void InsertFlattened(IEnumerable<DiffRow> rows, ref int insertIndex)
    {
        foreach (var row in rows)
        {
            DisplayRows.Insert(insertIndex, row);
            insertIndex += 1;

            if (row.HasChildren && row.IsExpanded)
            {
                var children = ApplySort(row.Node.Children.Select(child => new DiffRow(child, row.Depth + 1))).ToList();
                InsertFlattened(children, ref insertIndex);
            }
        }
    }

    private void RemoveDescendants(DiffRow parentRow)
    {
        var index = DisplayRows.IndexOf(parentRow);
        if (index < 0)
        {
            BuildRows();
            return;
        }

        var start = index + 1;
        while (start < DisplayRows.Count && DisplayRows[start].Depth > parentRow.Depth)
        {
            DisplayRows.RemoveAt(start);
        }
    }

    private void RemoveFromTree(DiffNode node)
    {
        if (node.Parent is not null)
        {
            node.Parent.Children.Remove(node);
        }
        else if (DiffRoot is not null)
        {
            DiffRoot.Children.Remove(node);
        }
    }

    private void FilterSnapshots()
    {
        FilteredSnapshots.Clear();
        SnapshotBefore = null;

        if (string.IsNullOrWhiteSpace(SelectedBaseDrive))
        {
            OnPropertyChanged(nameof(HasBaseSnapshots));
            OnPropertyChanged(nameof(HasNoBaseSnapshots));
            return;
        }

        var baseDrive = NormalizeDrivePath(SelectedBaseDrive);

        foreach (var snapshot in AvailableSnapshots.Where(s => string.Equals(NormalizeDrivePath(s.DrivePath), baseDrive, StringComparison.OrdinalIgnoreCase)))
        {
            FilteredSnapshots.Add(snapshot);
        }

        if (FilteredSnapshots.Count > 0)
        {
            SnapshotBefore = FilteredSnapshots[0];
        }

        OnPropertyChanged(nameof(HasBaseSnapshots));
        OnPropertyChanged(nameof(HasNoBaseSnapshots));
    }

    private async Task RecompareWithCurrentAsync()
    {
        if (SnapshotBefore is null || _currentSnapshot is null)
        {
            return;
        }

        ErrorMessage = null;
        IsLoading = true;
        await Task.Yield();
        try
        {
            DiffRoot = await Task.Run(() => _diffService.Compare(SnapshotBefore, _currentSnapshot));
            ResetExpansion(DiffRoot);
            BuildRows();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Re-compare failed");
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void InsertSnapshotRecord(SnapshotRecord snapshot)
    {
        AvailableSnapshots.Insert(0, snapshot);

        if (!string.IsNullOrWhiteSpace(SelectedBaseDrive)
            && string.Equals(NormalizeDrivePath(snapshot.DrivePath), NormalizeDrivePath(SelectedBaseDrive), StringComparison.OrdinalIgnoreCase))
        {
            FilteredSnapshots.Insert(0, snapshot);
            if (SnapshotBefore is null)
            {
                SnapshotBefore = snapshot;
            }
        }

        OnPropertyChanged(nameof(HasBaseSnapshots));
        OnPropertyChanged(nameof(HasNoBaseSnapshots));
    }

    private void LoadDrivesFromSnapshots(IEnumerable<SnapshotRecord> records)
    {
        AvailableDrives.Clear();
        foreach (var drive in records.Select(r => NormalizeDrivePath(r.DrivePath)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AvailableDrives.Add(drive);
        }
    }

    private static string NormalizeDrivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.TrimEnd('\\');
        return trimmed + "\\";
    }


    private async Task<SnapshotRecord> BuildCurrentSnapshotAsync(string drivePath)
    {
        var root = await _scanService.ScanFullAsync(drivePath, progress: null, CancellationToken.None);
        return new SnapshotRecord
        {
            Name = $"Escaneo {DateTime.Now:g}",
            DrivePath = drivePath,
            CreatedAt = DateTime.Now,
            TotalSizeBytes = root.SizeBytes,
            TreeJson = SnapshotService.SerializeTree(root)
        };
    }
}
