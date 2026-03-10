namespace Keybusy_DiskScope.Services;

/// <summary>
/// Persists and retrieves scan snapshots.
/// </summary>
public interface ISnapshotService
{
    Task<IReadOnlyList<SnapshotRecord>> GetAllAsync(CancellationToken ct = default);
    Task<SnapshotRecord?> GetByIdAsync(int id, CancellationToken ct = default);
    Task SaveAsync(SnapshotRecord snapshot, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
