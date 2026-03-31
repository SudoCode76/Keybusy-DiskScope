namespace Keybusy_DiskScope.Services;

/// <summary>
/// Scans a drive or directory and builds a <see cref="DiskNode"/> tree.
/// </summary>
public interface IScanService
{
    ScanEngineType LastScanEngineType { get; }
    string LastScanEngineDetail { get; }

    Task<DiskNode> ScanPreviewAsync(
        string rootPath,
        CancellationToken ct);

    Task<IReadOnlyList<DiskNode>> LoadChildrenAsync(
        string directoryPath,
        CancellationToken ct);

    /// <summary>
    /// Scans <paramref name="rootPath"/> recursively and returns the root node.
    /// Progress is reported as (bytesScanned, currentPath).
    /// </summary>
    Task<DiskNode> ScanFullAsync(
        string rootPath,
        IProgress<(long BytesScanned, string CurrentPath)>? progress,
        CancellationToken ct);
}
