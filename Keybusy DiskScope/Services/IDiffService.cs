namespace Keybusy_DiskScope.Services;

/// <summary>
/// Compares two snapshots and builds a <see cref="DiffNode"/> tree.
/// </summary>
public interface IDiffService
{
    DiffNode Compare(SnapshotRecord before, SnapshotRecord after);
}
