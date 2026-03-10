namespace Keybusy_DiskScope.Services;

/// <summary>
/// Scans a drive or directory and builds a <see cref="DiskNode"/> tree.
/// </summary>
public interface IScanService
{
    /// <summary>
    /// Scans <paramref name="rootPath"/> recursively and returns the root node.
    /// Progress is reported as (bytesScanned, currentPath).
    /// </summary>
    Task<DiskNode> ScanAsync(
        string rootPath,
        IProgress<(long BytesScanned, string CurrentPath)>? progress,
        CancellationToken ct);
}
