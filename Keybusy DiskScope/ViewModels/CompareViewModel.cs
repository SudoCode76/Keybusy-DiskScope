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
    }

    private void BuildRows()
    {
        string? selectedPath = SelectedRow?.Node.FullPath;
        DisplayRows.Clear();

        if (DiffRoot is null)
        {
            SelectedRow = null;
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

        RestoreSelection(selectedPath);
    }

    private void RestoreSelection(string? selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            SelectedRow = null;
            return;
        }

        DiffRow? matchedRow = null;
        foreach (var row in DisplayRows)
        {
            bool isMatch = string.Equals(row.Node.FullPath, selectedPath, StringComparison.OrdinalIgnoreCase);
            row.IsSelected = isMatch;
            if (isMatch && matchedRow is null)
            {
                matchedRow = row;
            }
        }

        SelectedRow = matchedRow;
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
    {
        if (SelectedRow is not null)
        {
            SelectedRow.IsSelected = false;
        }

        SelectedRow = row;
        if (SelectedRow is not null)
        {
            SelectedRow.IsSelected = true;
        }
    }

    [RelayCommand]
    private Task ToggleExpandAndSelectAsync(DiffRow? row)
    {
        if (row is null)
        {
            return Task.CompletedTask;
        }

        SelectRow(row);
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

        if (row.Status == DiffStatus.Removed)
        {
            return;
        }

        try
        {
            await _fileDeleteService.DeleteAsync(row.Node.FullPath, permanent: false, CancellationToken.None);
            RemoveFromTree(row.Node);
            BuildRows();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete {Path}", row.Node.FullPath);
            ErrorMessage = ex.Message;
        }
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
