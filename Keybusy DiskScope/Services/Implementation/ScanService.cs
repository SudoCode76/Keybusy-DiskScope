namespace Keybusy_DiskScope.Services.Implementation;

/// <summary>
/// Scans a directory tree on a background thread and builds a DiskNode hierarchy.
/// </summary>
public sealed class ScanService : IScanService
{
    private readonly ILogger<ScanService> _logger;
    private readonly INtfsFastScanService _ntfsFastScanService;
    private readonly ISettingsService _settingsService;
    private readonly int _maxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);

    public ScanEngineType LastScanEngineType { get; private set; } = ScanEngineType.Unknown;
    public string LastScanEngineDetail { get; private set; } = "Pendiente";

    public ScanService(
        ILogger<ScanService> logger,
        INtfsFastScanService ntfsFastScanService,
        ISettingsService settingsService)
    {
        _logger = logger;
        _ntfsFastScanService = ntfsFastScanService;
        _settingsService = settingsService;
    }

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

    public async Task<DiskNode> ScanFullAsync(
        string rootPath,
        IProgress<(long BytesScanned, string CurrentPath)>? progress,
        CancellationToken ct)
    {
        LastScanEngineType = ScanEngineType.Unknown;
        LastScanEngineDetail = "Detectando motor...";

        if (_settingsService.EnableFastNtfsScan)
        {
            try
            {
                var fastResult = await _ntfsFastScanService.TryScanFullAsync(rootPath, progress, ct);
                if (fastResult is not null)
                {
                    _logger.LogInformation("Using fast NTFS scan path for {RootPath}", rootPath);
                    LastScanEngineType = ScanEngineType.FastNtfs;
                    LastScanEngineDetail = string.IsNullOrWhiteSpace(_ntfsFastScanService.LastRunSummary)
                        ? "NTFS rapido"
                        : $"NTFS rapido ({_ntfsFastScanService.LastRunSummary})";
                    return fastResult;
                }

                LastScanEngineType = ScanEngineType.FastNtfsFallbackClassic;
                LastScanEngineDetail = $"Fallback a clasico ({_ntfsFastScanService.LastFailureDetail})";

                if (_settingsService.ForceFastNtfsOnly)
                {
                    throw new InvalidOperationException($"Modo estricto activo. NTFS rapido no disponible: {_ntfsFastScanService.LastFailureDetail}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fast NTFS scan failed for {RootPath}, falling back to standard scan", rootPath);
                LastScanEngineType = ScanEngineType.FastNtfsFallbackClassic;
                LastScanEngineDetail = string.IsNullOrWhiteSpace(_ntfsFastScanService.LastFailureDetail)
                    ? "Fallback a clasico (error en NTFS rapido)"
                    : $"Fallback a clasico ({_ntfsFastScanService.LastFailureDetail})";

                if (_settingsService.ForceFastNtfsOnly)
                {
                    throw;
                }
            }
        }
        else
        {
            LastScanEngineType = ScanEngineType.Classic;
            LastScanEngineDetail = "Clasico (rapido NTFS desactivado)";
        }

        _totalScanned = 0;
        _lastProgressReportMs = 0;
        var result = await Task.Run(() => ScanDirectoryParallel(rootPath, progress, ct, 0), ct);
        if (LastScanEngineType == ScanEngineType.Unknown)
        {
            LastScanEngineType = ScanEngineType.Classic;
            LastScanEngineDetail = "Clasico";
        }

        return result;
    }

    private long _totalScanned;
    private long _lastProgressReportMs;

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
            foreach (var entry in Directory.EnumerateDirectories(path, "*", options))
            {
                ct.ThrowIfCancellationRequested();

                var child = CreateDirectoryNode(entry, depth + 1);
                child.HasChildren = DirectoryHasChildren(entry, options);
                child.Parent = node;
                node.Children.Add(child);
                node.FolderCount += 1;
            }

            foreach (var entry in Directory.EnumerateFiles(path, "*", options))
            {
                ct.ThrowIfCancellationRequested();

                var fileNode = CreateFileNode(entry, depth + 1);
                fileNode.Parent = node;
                node.Children.Add(fileNode);
                node.SizeBytes += fileNode.SizeBytes;
                node.FileCount += 1;
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
            foreach (var entry in Directory.EnumerateDirectories(path, "*", options))
            {
                ct.ThrowIfCancellationRequested();

                var child = CreateDirectoryNode(entry, 0);
                child.HasChildren = DirectoryHasChildren(entry, options);
                child.Parent = null;
                results.Add(child);
            }

            foreach (var entry in Directory.EnumerateFiles(path, "*", options))
            {
                ct.ThrowIfCancellationRequested();
                results.Add(CreateFileNode(entry, 0));
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

        try
        {
            foreach (var entry in Directory.EnumerateDirectories(path, "*", options))
            {
                ct.ThrowIfCancellationRequested();
                subDirs.Add(entry);
            }

            foreach (var entry in Directory.EnumerateFiles(path, "*", options))
            {
                ct.ThrowIfCancellationRequested();

                var fileNode = CreateFileNode(entry, depth + 1);
                fileNode.Parent = node;
                fileNodes.Add(fileNode);
                node.SizeBytes += fileNode.SizeBytes;
                node.FileCount += 1;

                var totalScanned = Interlocked.Add(ref _totalScanned, fileNode.SizeBytes);
                ReportProgressThrottled(progress, totalScanned, fileNode.FullPath);
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

        try
        {
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
                    child.Parent = node;
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
        }
        catch (OperationCanceledException)
        {
            return node;
        }

        node.HasChildren = node.Children.Count > 0;
        node.ChildrenLoaded = true;
        SortChildren(node.Children);

        if (depth == 0)
        {
            ReportProgressThrottled(progress, Interlocked.Read(ref _totalScanned), node.FullPath, force: true);
        }

        return node;
    }

    private void ReportProgressThrottled(
        IProgress<(long BytesScanned, string CurrentPath)>? progress,
        long totalScanned,
        string currentPath,
        bool force = false)
    {
        if (progress is null)
        {
            return;
        }

        var nowMs = Environment.TickCount64;
        if (!force)
        {
            var lastReportMs = Interlocked.Read(ref _lastProgressReportMs);
            if (nowMs - lastReportMs < 120)
            {
                return;
            }
        }

        Interlocked.Exchange(ref _lastProgressReportMs, nowMs);
        progress.Report((totalScanned, currentPath));
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
