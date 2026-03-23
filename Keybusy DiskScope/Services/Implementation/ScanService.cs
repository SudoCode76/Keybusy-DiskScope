namespace Keybusy_DiskScope.Services.Implementation;

/// <summary>
/// Scans a directory tree on a background thread and builds a DiskNode hierarchy.
/// </summary>
public sealed class ScanService : IScanService
{
    private readonly ILogger<ScanService> _logger;
    private readonly int _maxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount);

    public ScanService(ILogger<ScanService> logger) => _logger = logger;

    public Task<DiskNode> ScanPreviewAsync(
        string rootPath,
        CancellationToken ct)
    {
        return Task.Run(() => BuildPreview(rootPath, ct, 0), ct);
    }

    public Task<IReadOnlyList<DiskNode>> LoadChildrenAsync(
        string directoryPath,
        CancellationToken ct)
    {
        return Task.Run(() => LoadChildren(directoryPath, ct), ct);
    }

    public Task<DiskNode> ScanFullAsync(
        string rootPath,
        IProgress<(long BytesScanned, string CurrentPath)>? progress,
        CancellationToken ct)
    {
        _totalScanned = 0;
        return Task.Run(() => ScanDirectoryParallel(rootPath, progress, ct, 0), ct);
    }

    private long _totalScanned;

    private static EnumerationOptions CreateEnumerationOptions()
        => new()
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

    private DiskNode BuildPreview(string path, CancellationToken ct, int depth)
    {
        ct.ThrowIfCancellationRequested();

        var node = CreateDirectoryNode(path, depth);
        var options = CreateEnumerationOptions();

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", options))
            {
                ct.ThrowIfCancellationRequested();

                if (IsDirectory(entry))
                {
                    var child = CreateDirectoryNode(entry, depth + 1);
                    child.HasChildren = DirectoryHasChildren(entry, options);
                    node.Children.Add(child);
                    node.FolderCount += 1;
                }
                else
                {
                    var fileNode = CreateFileNode(entry, depth + 1);
                    node.Children.Add(fileNode);
                    node.SizeBytes += fileNode.SizeBytes;
                    node.FileCount += 1;
                }
            }
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogWarning(ex, "Directory missing during preview: {Path}", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied during preview: {Path}", path);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "I/O error during preview: {Path}", path);
        }

        node.HasChildren = node.Children.Count > 0;
        node.ChildrenLoaded = true;
        SortChildren(node.Children);
        return node;
    }

    private IReadOnlyList<DiskNode> LoadChildren(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var options = CreateEnumerationOptions();
        var results = new List<DiskNode>();

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", options))
            {
                ct.ThrowIfCancellationRequested();

                if (IsDirectory(entry))
                {
                    var child = CreateDirectoryNode(entry, 0);
                    child.HasChildren = DirectoryHasChildren(entry, options);
                    results.Add(child);
                }
                else
                {
                    results.Add(CreateFileNode(entry, 0));
                }
            }
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogWarning(ex, "Directory missing enumerating children: {Path}", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied enumerating children: {Path}", path);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "I/O error enumerating children: {Path}", path);
        }

        SortChildren(results);
        return results;
    }

    private DiskNode ScanDirectoryParallel(
        string path,
        IProgress<(long, string)>? progress,
        CancellationToken ct,
        int depth)
    {
        ct.ThrowIfCancellationRequested();

        var node = CreateDirectoryNode(path, depth);
        var options = CreateEnumerationOptions();
        var subDirs = new List<string>();
        var fileNodes = new List<DiskNode>();

        // Enumerate entries (non-recursive)
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", options))
            {
                ct.ThrowIfCancellationRequested();

                if (IsDirectory(entry))
                {
                    subDirs.Add(entry);
                }
                else
                {
                    var fileNode = CreateFileNode(entry, depth + 1);
                    fileNodes.Add(fileNode);
                    node.SizeBytes += fileNode.SizeBytes;
                    node.FileCount += 1;

                    Interlocked.Add(ref _totalScanned, fileNode.SizeBytes);
                    progress?.Report((Interlocked.Read(ref _totalScanned), fileNode.FullPath));
                }
            }
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogWarning(ex, "Directory missing during enumeration: {Path}", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied enumerating directories: {Path}", path);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "I/O error enumerating directories: {Path}", path);
        }

        var childNodes = new ConcurrentBag<DiskNode>();
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = _maxDegreeOfParallelism
        };

        Parallel.ForEach(subDirs, parallelOptions, subPath =>
        {
            try
            {
                var child = ScanDirectoryParallel(subPath, progress, ct, depth + 1);
                childNodes.Add(child);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Access denied: {Path}", subPath);
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger.LogWarning(ex, "Directory missing: {Path}", subPath);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "I/O error scanning: {Path}", subPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error scanning: {Path}", subPath);
            }
        });

        foreach (var fileNode in fileNodes)
        {
            node.Children.Add(fileNode);
        }

        foreach (var child in childNodes)
        {
            node.Children.Add(child);
            node.SizeBytes += child.SizeBytes;
            node.FileCount += child.FileCount;
            node.FolderCount += 1 + child.FolderCount;
        }

        node.HasChildren = node.Children.Count > 0;
        node.ChildrenLoaded = true;
        SortChildren(node.Children);

        return node;
    }

    private static bool IsDirectory(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            return (attrs & FileAttributes.Directory) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static DiskNode CreateDirectoryNode(string path, int depth)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = trimmed;
        }

        var lastWrite = DateTime.MinValue;
        try
        {
            lastWrite = Directory.GetLastWriteTime(path);
        }
        catch (Exception)
        {
            lastWrite = DateTime.MinValue;
        }

        return new DiskNode
        {
            Name = name,
            FullPath = path,
            IsDirectory = true,
            LastModified = lastWrite,
            Depth = depth
        };
    }

    private static DiskNode CreateFileNode(string path, int depth)
    {
        var fileInfo = new FileInfo(path);
        long size = 0;
        DateTime lastWrite = DateTime.MinValue;
        try { size = fileInfo.Length; }
        catch (IOException) { /* file in use or deleted */ }
        catch (UnauthorizedAccessException) { /* no access */ }
        try { lastWrite = fileInfo.LastWriteTime; }
        catch (Exception) { lastWrite = DateTime.MinValue; }

        return new DiskNode
        {
            Name = fileInfo.Name,
            FullPath = fileInfo.FullName,
            IsDirectory = false,
            SizeBytes = size,
            Extension = fileInfo.Extension.ToLowerInvariant(),
            LastModified = lastWrite,
            FileCount = 1,
            Depth = depth
        };
    }

    private static bool DirectoryHasChildren(string path, EnumerationOptions options)
    {
        try
        {
            using var enumerator = Directory.EnumerateFileSystemEntries(path, "*", options).GetEnumerator();
            return enumerator.MoveNext();
        }
        catch
        {
            return false;
        }
    }

    private static void SortChildren(IList<DiskNode> nodes)
    {
        var ordered = nodes
            .OrderByDescending(n => n.IsDirectory)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        nodes.Clear();
        foreach (var node in ordered)
        {
            nodes.Add(node);
        }
    }
}
