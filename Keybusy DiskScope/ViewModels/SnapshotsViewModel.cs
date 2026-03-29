namespace Keybusy_DiskScope.ViewModels;

public partial class SnapshotsViewModel : ObservableObject
{
    private readonly ISnapshotService _snapshotService;
    private readonly ILogger<SnapshotsViewModel> _logger;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private SnapshotRecord? _selectedSnapshot;

    public bool HasError => ErrorMessage is not null;
    public bool HasSnapshots => Snapshots.Count > 0;
    public bool HasNoSnapshots => !HasSnapshots;
    public bool IsNotLoading => !IsLoading;

    public ObservableCollection<SnapshotRecord> Snapshots { get; } = new();

    public SnapshotsViewModel(
        ISnapshotService snapshotService,
        ILogger<SnapshotsViewModel> logger)
    {
        _snapshotService = snapshotService;
        _logger = logger;
        Snapshots.CollectionChanged += (_, _) => OnSnapshotsChanged();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        await Task.Yield();
        try
        {
            var records = await _snapshotService.GetAllAsync();
            Snapshots.Clear();
            foreach (var r in records)
                Snapshots.Add(r);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load snapshots");
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(SnapshotRecord snapshot)
    {
        try
        {
            await _snapshotService.DeleteAsync(snapshot.Id);
            Snapshots.Remove(snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete snapshot {Id}", snapshot.Id);
            ErrorMessage = ex.Message;
        }
    }

    partial void OnErrorMessageChanged(string? value)
        => OnPropertyChanged(nameof(HasError));

    partial void OnIsLoadingChanged(bool value)
        => OnPropertyChanged(nameof(IsNotLoading));

    private void OnSnapshotsChanged()
    {
        OnPropertyChanged(nameof(HasSnapshots));
        OnPropertyChanged(nameof(HasNoSnapshots));
    }
}
