namespace Keybusy_DiskScope.Services.Implementation;

/// <summary>
/// Scans a directory tree on a background thread and builds a DiskNode hierarchy.
/// </summary>
public sealed class ScanService : IScanService
{
    private readonly ILogger<ScanService> _logger;

    public ScanService(ILogger<ScanService> logger) => _logger = logger;

    public async Task<DiskNode> ScanAsync(
        string rootPath,
        IProgress<(long BytesScanned, string CurrentPath)>? progress,
        CancellationToken ct)
    {
        return await Task.Run(() => ScanDirectory(rootPath, progress, ref _totalScanned, ct), ct);
    }

    private long _totalScanned;

    private DiskNode ScanDirectory(
        string path,
        IProgress<(long, string)>? progress,
        ref long totalScanned,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var dirInfo = new DirectoryInfo(path);
        var node = new DiskNode
        {
            Name = dirInfo.Name,
            FullPath = dirInfo.FullName,
            IsDirectory = true,
            LastModified = dirInfo.LastWriteTime
        };

        // Scan subdirectories
        try
        {
            foreach (var sub in dirInfo.EnumerateDirectories())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var child = ScanDirectory(sub.FullName, progress, ref totalScanned, ct);
                    node.SizeBytes += child.SizeBytes;
                    node.Children.Add(child);
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogWarning(ex, "Access denied: {Path}", sub.FullName);
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied enumerating directories: {Path}", path);
        }

        // Scan files
        try
        {
            foreach (var file in dirInfo.EnumerateFiles())
            {
                ct.ThrowIfCancellationRequested();
                long size = 0;
                try { size = file.Length; }
                catch (IOException) { /* file in use or deleted */ }

                var fileNode = new DiskNode
                {
                    Name = file.Name,
                    FullPath = file.FullName,
                    IsDirectory = false,
                    SizeBytes = size,
                    Extension = file.Extension.ToLowerInvariant(),
                    LastModified = file.LastWriteTime
                };

                node.SizeBytes += size;
                node.Children.Add(fileNode);

                Interlocked.Add(ref totalScanned, size);
                progress?.Report((Interlocked.Read(ref totalScanned), file.FullName));
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied enumerating files: {Path}", path);
        }

        // Sort: directories first, then files, both alphabetically
        node.Children.Sort((a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return node;
    }
}
