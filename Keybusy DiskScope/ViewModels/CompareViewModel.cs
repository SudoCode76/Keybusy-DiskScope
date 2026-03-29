using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using Keybusy_DiskScope.Models;
using Keybusy_DiskScope.Services;

namespace Keybusy_DiskScope.ViewModels;

public partial class CompareViewModel : ObservableObject
{
    private readonly ISnapshotService _snapshotService;
    private readonly IDiffService _diffService;
    private readonly IFileDeleteService _fileDeleteService;
    private readonly INavigationService _navigationService;
    private readonly ILogger<CompareViewModel> _logger;

    private IRelayCommand? _analyzeCurrentCommand;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private SnapshotRecord? _snapshotBefore;
    [ObservableProperty] private SnapshotRecord? _snapshotAfter;
    [ObservableProperty] private DiffNode? _diffRoot;
    [ObservableProperty] private DiffRow? _selectedRow;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private int _selectedFilterIndex;
    [ObservableProperty] private int _selectedSortIndex;

    public bool HasError => ErrorMessage is not null;
    public bool HasResult => DiffRoot is not null;
    public bool IsNotLoading => !IsLoading;

    public ObservableCollection<SnapshotRecord> AvailableSnapshots { get; } = new();
    public ObservableCollection<DiffRow> DisplayRows { get; } = new();

    public IReadOnlyList<string> FilterOptions { get; } = new[]
    {
        "Todos",
        "Modificados",
        "Nuevos",
        "Eliminados"
    };

    public IReadOnlyList<string> SortOptions { get; } = new[]
    {
        "Mayor diferencia",
        "Nombre",
        "Tamano actual"
    };

    public CompareViewModel(
        ISnapshotService snapshotService,
        IDiffService diffService,
        IFileDeleteService fileDeleteService,
        INavigationService navigationService,
        ILogger<CompareViewModel> logger)
    {
        _snapshotService = snapshotService;
        _diffService = diffService;
        _fileDeleteService = fileDeleteService;
        _navigationService = navigationService;
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
        if (SnapshotBefore is null || SnapshotAfter is null) return;

        ErrorMessage = null;
        IsLoading = true;
        await Task.Yield();
        try
        {
            DiffRoot = await Task.Run(() => _diffService.Compare(SnapshotBefore, SnapshotAfter));
            ResetExpansion(DiffRoot);
            BuildRows();
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

    partial void OnSearchTextChanged(string value)
        => BuildRows();

    partial void OnSelectedFilterIndexChanged(int value)
        => BuildRows();

    partial void OnSelectedSortIndexChanged(int value)
        => BuildRows();

    private void BuildRows()
    {
        DisplayRows.Clear();

        if (DiffRoot is null)
        {
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
    }

    private IEnumerable<DiffRow> CreateRowsFromNodes(IEnumerable<DiffNode> nodes, int depth)
    {
        var rows = new List<DiffRow>();
        foreach (var node in nodes)
        {
            node.Parent = node.Parent ?? DiffRoot;
            rows.Add(new DiffRow(node, depth));
        }

        var filtered = ApplyFilter(rows);
        return ApplySort(filtered).ToList();
    }

    private static void ResetExpansion(DiffNode node)
    {
        node.IsExpanded = false;
        foreach (var child in node.Children)
        {
            ResetExpansion(child);
        }
    }

    private IEnumerable<DiffRow> ApplyFilter(IEnumerable<DiffRow> rows)
    {
        var search = SearchText?.Trim();
        var filtered = rows;

        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(r => r.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        return SelectedFilterIndex switch
        {
            1 => filtered.Where(r => r.Status is DiffStatus.Grown or DiffStatus.Shrunk),
            2 => filtered.Where(r => r.Status == DiffStatus.Added),
            3 => filtered.Where(r => r.Status == DiffStatus.Removed),
            _ => filtered
        };
    }

    private IEnumerable<DiffRow> ApplySort(IEnumerable<DiffRow> rows)
    {
        return SelectedSortIndex switch
        {
            1 => rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            2 => rows.OrderByDescending(r => r.SizeAfter),
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
    private async Task ToggleExpandAndSelectAsync(DiffRow? row)
    {
        if (row is null)
        {
            return;
        }

        SelectRow(row);
        if (!row.HasChildren)
        {
            return;
        }

        if (row.IsExpanded)
        {
            row.IsExpanded = false;
            RemoveDescendants(row);
        }
        else
        {
            row.IsExpanded = true;
            InsertChildren(row);
        }
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

    public IRelayCommand AnalyzeCurrentCommand
        => _analyzeCurrentCommand ??= new RelayCommand(AnalyzeCurrent);

    private void AnalyzeCurrent()
    {
        if (SnapshotAfter is null || string.IsNullOrWhiteSpace(SnapshotAfter.DrivePath))
        {
            return;
        }

        _navigationService.NavigateTo("ScanPage", SnapshotAfter.DrivePath);
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
                var children = ApplySort(ApplyFilter(row.Node.Children.Select(child => new DiffRow(child, row.Depth + 1)))).ToList();
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
}
