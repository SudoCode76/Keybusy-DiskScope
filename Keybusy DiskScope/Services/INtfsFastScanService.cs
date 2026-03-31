namespace Keybusy_DiskScope.Services;

public interface INtfsFastScanService
{
    Task<DiskNode?> TryScanFullAsync(
        string rootPath,
        IProgress<(long BytesScanned, string CurrentPath)>? progress,
        CancellationToken ct);
}
