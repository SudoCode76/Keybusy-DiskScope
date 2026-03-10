namespace Keybusy_DiskScope.ViewModels;

public partial class CompareViewModel : ObservableObject
{
    private readonly ISnapshotService _snapshotService;
    private readonly IDiffService _diffService;
    private readonly ILogger<CompareViewModel> _logger;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private SnapshotRecord? _snapshotBefore;
    [ObservableProperty] private SnapshotRecord? _snapshotAfter;
    [ObservableProperty] private DiffNode? _diffRoot;

    public bool HasError => ErrorMessage is not null;
    public bool HasResult => DiffRoot is not null;

    public ObservableCollection<SnapshotRecord> AvailableSnapshots { get; } = new();

    public CompareViewModel(
        ISnapshotService snapshotService,
        IDiffService diffService,
        ILogger<CompareViewModel> logger)
    {
        _snapshotService = snapshotService;
        _diffService = diffService;
        _logger = logger;
    }

    [RelayCommand]
    private async Task LoadSnapshotsAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
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
    private void RunCompare()
    {
        if (SnapshotBefore is null || SnapshotAfter is null) return;

        ErrorMessage = null;
        try
        {
            DiffRoot = _diffService.Compare(SnapshotBefore, SnapshotAfter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Comparison failed");
            ErrorMessage = ex.Message;
        }
    }

    partial void OnErrorMessageChanged(string? value)
        => OnPropertyChanged(nameof(HasError));

    partial void OnDiffRootChanged(DiffNode? value)
        => OnPropertyChanged(nameof(HasResult));
}
