namespace Keybusy_DiskScope.Services;

public interface INtfsFastScanService
{
    string LastFailureDetail { get; }
    string LastRunSummary { get; }

    Task<DiskNode?> TryScanFullAsync(
        string rootPath,
        IProgress<(long BytesScanned, string CurrentPath)>? progress,
        CancellationToken ct);
}
