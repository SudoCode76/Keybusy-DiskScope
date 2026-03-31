using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace Keybusy_DiskScope.Services.Implementation;

public sealed class NtfsFastScanService : INtfsFastScanService
{
    private const uint GenericRead = 0x80000000;
    private const uint FileReadAttributes = 0x80;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeReparsePoint = 0x00000400;
    private const uint InvalidFileSize = 0xFFFFFFFF;

    private const uint FsctlEnumUsnData = 0x000900b3;
    private const uint FsctlQueryUsnJournal = 0x000900f4;
    private const int GetFileExInfoStandard = 0;

    private const int FileIdInfoClass = 18;
    private const int FileStandardInfoClass = 1;
    private static readonly NtfsFileId NtfsRootFrn = NtfsFileId.FromUInt64(5);

    private readonly ILogger<NtfsFastScanService> _logger;

    public string LastFailureDetail { get; private set; } = string.Empty;
    public string LastRunSummary { get; private set; } = string.Empty;

    public NtfsFastScanService(ILogger<NtfsFastScanService> logger)
    {
        _logger = logger;
    }

    public Task<DiskNode?> TryScanFullAsync(
        string rootPath,
        IProgress<(long BytesScanned, string CurrentPath)>? progress,
        CancellationToken ct)
    {
        return Task.Run(() => TryScanFullInternal(rootPath, progress, ct), ct);
    }

    private DiskNode? TryScanFullInternal(
        string rootPath,
        IProgress<(long BytesScanned, string CurrentPath)>? progress,
        CancellationToken ct)
    {
        LastFailureDetail = string.Empty;
        LastRunSummary = string.Empty;

        var currentStage = "inicio";
        var totalTimer = System.Diagnostics.Stopwatch.StartNew();
        var stageTimer = System.Diagnostics.Stopwatch.StartNew();
        var usnStageMs = 0L;
        var graphStageMs = 0L;
        var aggregateStageMs = 0L;
        var treeStageMs = 0L;

        DiskNode? Fail(string detail)
        {
            LastFailureDetail = detail;
            LastRunSummary = $"fallo:{currentStage}, total:{totalTimer.ElapsedMilliseconds} ms";
            return null;
        }

        if (!IsAdministrator())
        {
            currentStage = "admin";
            return Fail("Se requiere ejecutar la app como administrador.");
        }

        var normalizedRoot = NormalizeRootPath(rootPath);
        if (normalizedRoot is null)
        {
            currentStage = "validacion-ruta";
            return Fail("Ruta raiz invalida para analisis rapido NTFS.");
        }

        if (!IsNtfsRoot(normalizedRoot))
        {
            currentStage = "validacion-ntfs";
            return Fail("La unidad no es NTFS.");
        }

        if (!TryGetVolumeHandlePath(normalizedRoot, out var volumePath))
        {
            currentStage = "volumen-path";
            return Fail("No se pudo abrir el volumen NTFS.");
        }

        using var volumeHandle = CreateFile(
            volumePath,
            GenericRead,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (volumeHandle.IsInvalid)
        {
            currentStage = "volumen-open";
            return Fail($"No se pudo abrir el volumen NTFS (Win32 {Marshal.GetLastWin32Error()}).");
        }

        if (!TryGetRootFileId(normalizedRoot, out var rootId))
        {
            currentStage = "root-id";
            return Fail("No se pudo resolver el identificador raiz NTFS.");
        }

        currentStage = "usn-query";
        stageTimer.Restart();
        if (!TryQueryUsnJournal(volumeHandle, out var journalData))
        {
            return Fail("No se pudo consultar el USN Journal.");
        }

        var entries = new Dictionary<NtfsFileId, UsnEntry>(capacity: 256 * 1024);
        currentStage = "usn-enum";
        if (!TryEnumerateUsnRecords(volumeHandle, journalData.NextUsn, entries, ct))
        {
            return Fail("No se pudo enumerar registros NTFS del USN Journal.");
        }
        usnStageMs = stageTimer.ElapsedMilliseconds;

        currentStage = "grafo";
        stageTimer.Restart();
        var childrenDirs = new Dictionary<NtfsFileId, List<NtfsFileId>>();
        var childrenFiles = new Dictionary<NtfsFileId, List<NtfsFileId>>();
        BuildChildMaps(entries, childrenDirs, childrenFiles);

        var rootResolution = ResolveBestRootCandidate(normalizedRoot, rootId, childrenDirs, childrenFiles, entries);
        rootId = rootResolution.RootId;

        var reachableDirs = rootResolution.ReachableDirs;
        var reachableFiles = rootResolution.ReachableFiles;
        graphStageMs = stageTimer.ElapsedMilliseconds;
            if (reachableDirs.Count <= 1 && reachableFiles.Count == 0)
            {
                var topDirParent = childrenDirs
                    .OrderByDescending(kv => kv.Value.Count)
                    .Select(kv => $"{kv.Key.ToDebugString()}:{kv.Value.Count}")
                    .FirstOrDefault() ?? "none";

                var topFileParent = childrenFiles
                    .OrderByDescending(kv => kv.Value.Count)
                    .Select(kv => $"{kv.Key.ToDebugString()}:{kv.Value.Count}")
                    .FirstOrDefault() ?? "none";

                var detail =
                    $"[NTFS128] No se encontraron entradas accesibles para el arbol NTFS. Entradas USN: {entries.Count}. " +
                    $"ChildrenDirs:{childrenDirs.Count}, ChildrenFiles:{childrenFiles.Count}, " +
                    $"TopDirParent:{topDirParent}, TopFileParent:{topFileParent}. " +
                    $"Root candidates: {rootResolution.Diagnostics}.";
                currentStage = "grafo-vacio";
                return Fail(detail);
            }

        var rootPrefix = normalizedRoot.TrimEnd('\\');
        var pathCache = new Dictionary<NtfsFileId, string>
        {
            [rootId] = rootPrefix
        };

        var sizeByFile = new Dictionary<NtfsFileId, long>(reachableFiles.Count);
        var lastModifiedByFile = new Dictionary<NtfsFileId, DateTime>(reachableFiles.Count);
        var aggregateByDirectory = new Dictionary<NtfsFileId, AggregateInfo>(reachableDirs.Count);
        foreach (var directoryId in reachableDirs)
        {
            aggregateByDirectory[directoryId] = new AggregateInfo();
        }

        currentStage = "agregacion";
        stageTimer.Restart();
        BuildFileSizesAndCounts(
            reachableFiles,
            entries,
            pathCache,
            sizeByFile,
            lastModifiedByFile,
            aggregateByDirectory,
            rootId,
            rootPrefix,
            progress,
            ct);
        PropagateDirectoryAggregates(rootId, childrenDirs, aggregateByDirectory);
        aggregateStageMs = stageTimer.ElapsedMilliseconds;

        var rootNode = CreateDirectoryNode(normalizedRoot, depth: 0, lastModified: TryGetPathLastWriteTime(rootPrefix));
        rootNode.ChildrenLoaded = true;

        currentStage = "arbol";
        stageTimer.Restart();
        BuildDiskTree(
            rootId,
            rootNode,
            0,
            rootId,
            entries,
            childrenDirs,
            childrenFiles,
            pathCache,
            sizeByFile,
            lastModifiedByFile,
            aggregateByDirectory,
            rootPrefix,
            ct);
        treeStageMs = stageTimer.ElapsedMilliseconds;

        if (aggregateByDirectory.TryGetValue(rootId, out var rootAggregate))
        {
            rootNode.SizeBytes = rootAggregate.SizeBytes;
            rootNode.FileCount = rootAggregate.FileCount;
            rootNode.FolderCount = rootAggregate.FolderCount;
        }

        rootNode.HasChildren = rootNode.Children.Count > 0;
        rootNode.ChildrenLoaded = true;

        LastFailureDetail = string.Empty;
        LastRunSummary =
            $"usn:{usnStageMs}ms, grafo:{graphStageMs}ms, exactitud:{aggregateStageMs}ms, arbol:{treeStageMs}ms, " +
            $"entries:{entries.Count}, dirs:{reachableDirs.Count}, files:{reachableFiles.Count}, total:{totalTimer.ElapsedMilliseconds}ms";

        _logger.LogInformation(
            "Fast NTFS profiling for {RootPath}: {Summary}",
            normalizedRoot,
            LastRunSummary);

        return rootNode;
    }

    private static bool IsAdministrator()
    {
        try
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static string? NormalizeRootPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        var normalizedRoot = root.EndsWith('\\') ? root : $"{root}\\";
        return string.Equals(path.TrimEnd('\\'), normalizedRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            ? normalizedRoot
            : null;
    }

    private static bool IsNtfsRoot(string rootPath)
    {
        try
        {
            var drive = new DriveInfo(rootPath);
            return drive.IsReady && string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetVolumeHandlePath(string rootPath, out string volumePath)
    {
        volumePath = string.Empty;
        var driveLetter = rootPath.TrimEnd('\\');
        if (driveLetter.Length < 2 || driveLetter[1] != ':')
        {
            return false;
        }

        volumePath = $"\\\\.\\{driveLetter[0]}:";
        return true;
    }

    private static bool TryGetRootFileId(string rootPath, out NtfsFileId rootId)
    {
        rootId = default;

        using var handle = CreateFile(
            rootPath,
            FileReadAttributes,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            return false;
        }

        var fileIdInfoSize = Marshal.SizeOf<FileIdInfo>();
        var fileIdInfoBuffer = Marshal.AllocHGlobal(fileIdInfoSize);
        try
        {
            if (GetFileInformationByHandleEx(handle, FileIdInfoClass, fileIdInfoBuffer, (uint)fileIdInfoSize))
            {
                var info = Marshal.PtrToStructure<FileIdInfo>(fileIdInfoBuffer);
                rootId = new NtfsFileId(info.FileIdLow, info.FileIdHigh);
                return true;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(fileIdInfoBuffer);
        }

        if (!GetFileInformationByHandle(handle, out var byHandleInfo))
        {
            return false;
        }

        var frn64 = ((ulong)byHandleInfo.FileIndexHigh << 32) | byHandleInfo.FileIndexLow;
        rootId = NtfsFileId.FromUInt64(frn64);
        return true;
    }

    private static bool TryQueryUsnJournal(SafeFileHandle volumeHandle, out UsnJournalDataV0 data)
    {
        var size = Marshal.SizeOf<UsnJournalDataV0>();
        var buffer = new byte[size];
        if (!DeviceIoControl(volumeHandle, FsctlQueryUsnJournal, IntPtr.Zero, 0, buffer, (uint)size, out var bytesReturned, IntPtr.Zero)
            || bytesReturned < size)
        {
            data = default;
            return false;
        }

        data = ReadStruct<UsnJournalDataV0>(buffer);
        return true;
    }

    private static bool TryEnumerateUsnRecords(
        SafeFileHandle volumeHandle,
        long highUsn,
        Dictionary<NtfsFileId, UsnEntry> entries,
        CancellationToken ct)
    {
        var enumData = new MftEnumDataV0
        {
            StartFileReferenceNumber = 0,
            LowUsn = 0,
            HighUsn = highUsn
        };

        var outBuffer = new byte[1024 * 1024];
        var inSize = Marshal.SizeOf<MftEnumDataV0>();
        var inBuffer = Marshal.AllocHGlobal(inSize);

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                Marshal.StructureToPtr(enumData, inBuffer, fDeleteOld: false);

                if (!DeviceIoControl(
                    volumeHandle,
                    FsctlEnumUsnData,
                    inBuffer,
                    (uint)inSize,
                    outBuffer,
                    (uint)outBuffer.Length,
                    out var bytesReturned,
                    IntPtr.Zero))
                {
                    const int ErrorHandleEof = 38;
                    return Marshal.GetLastWin32Error() == ErrorHandleEof;
                }

                if (bytesReturned <= sizeof(long))
                {
                    return true;
                }

                enumData.StartFileReferenceNumber = BitConverter.ToUInt64(outBuffer, 0);
                var offset = sizeof(long);

                while (offset + 8 <= bytesReturned)
                {
                    var recordLength = BitConverter.ToInt32(outBuffer, offset);
                    if (recordLength <= 0 || offset + recordLength > bytesReturned)
                    {
                        break;
                    }

                    var majorVersion = BitConverter.ToUInt16(outBuffer, offset + 4);
                    if (majorVersion == 2 && recordLength >= 64)
                    {
                        var id = NtfsFileId.FromUInt64(BitConverter.ToUInt64(outBuffer, offset + 8));
                        var parent = NtfsFileId.FromUInt64(BitConverter.ToUInt64(outBuffer, offset + 16));
                        var lastModified = DateTimeFromFileTime(BitConverter.ToInt64(outBuffer, offset + 32));
                        var attributes = BitConverter.ToUInt32(outBuffer, offset + 52);
                        var nameLength = BitConverter.ToUInt16(outBuffer, offset + 56);
                        var nameOffset = BitConverter.ToUInt16(outBuffer, offset + 58);
                        TryAddEntry(entries, outBuffer, offset, recordLength, id, parent, lastModified, attributes, nameLength, nameOffset);
                    }
                    else if (majorVersion == 3 && recordLength >= 76)
                    {
                        var id = new NtfsFileId(
                            BitConverter.ToUInt64(outBuffer, offset + 8),
                            BitConverter.ToUInt64(outBuffer, offset + 16));

                        var parent = new NtfsFileId(
                            BitConverter.ToUInt64(outBuffer, offset + 24),
                            BitConverter.ToUInt64(outBuffer, offset + 32));

                        var lastModified = DateTimeFromFileTime(BitConverter.ToInt64(outBuffer, offset + 48));
                        var attributes = BitConverter.ToUInt32(outBuffer, offset + 68);
                        var nameLength = BitConverter.ToUInt16(outBuffer, offset + 72);
                        var nameOffset = BitConverter.ToUInt16(outBuffer, offset + 74);
                        TryAddEntry(entries, outBuffer, offset, recordLength, id, parent, lastModified, attributes, nameLength, nameOffset);
                    }

                    offset += recordLength;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(inBuffer);
        }
    }

    private static void TryAddEntry(
        IDictionary<NtfsFileId, UsnEntry> entries,
        byte[] buffer,
        int recordOffset,
        int recordLength,
        NtfsFileId id,
        NtfsFileId parent,
        DateTime lastModified,
        uint attributes,
        ushort nameLength,
        ushort nameOffset)
    {
        if (nameOffset + nameLength > recordLength)
        {
            return;
        }

        var name = SanitizeEntryName(Encoding.Unicode.GetString(buffer, recordOffset + nameOffset, nameLength));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var isReparse = (attributes & FileAttributeReparsePoint) != 0;
        if (isReparse)
        {
            return;
        }

        entries[id] = new UsnEntry(id, parent, name, (attributes & FileAttributeDirectory) != 0, lastModified);
    }

    private static void BuildChildMaps(
        IReadOnlyDictionary<NtfsFileId, UsnEntry> entries,
        Dictionary<NtfsFileId, List<NtfsFileId>> childrenDirs,
        Dictionary<NtfsFileId, List<NtfsFileId>> childrenFiles)
    {
        foreach (var entry in entries.Values)
        {
            var target = entry.IsDirectory ? childrenDirs : childrenFiles;
            if (!target.TryGetValue(entry.ParentId, out var children))
            {
                children = new List<NtfsFileId>();
                target[entry.ParentId] = children;
            }

            children.Add(entry.Id);
        }
    }

    private static HashSet<NtfsFileId> BuildReachableDirectories(
        NtfsFileId rootId,
        IReadOnlyDictionary<NtfsFileId, List<NtfsFileId>> childrenDirs,
        IReadOnlyDictionary<NtfsFileId, UsnEntry> entries)
    {
        var reachable = new HashSet<NtfsFileId> { rootId };
        var queue = new Queue<NtfsFileId>();
        queue.Enqueue(rootId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!childrenDirs.TryGetValue(current, out var children))
            {
                continue;
            }

            foreach (var childId in children)
            {
                if (!entries.TryGetValue(childId, out var entry) || !entry.IsDirectory)
                {
                    continue;
                }

                if (reachable.Add(childId))
                {
                    queue.Enqueue(childId);
                }
            }
        }

        return reachable;
    }

    private static List<NtfsFileId> BuildReachableFiles(
        IEnumerable<NtfsFileId> reachableDirs,
        IReadOnlyDictionary<NtfsFileId, List<NtfsFileId>> childrenFiles)
    {
        var files = new List<NtfsFileId>();
        foreach (var dir in reachableDirs)
        {
            if (childrenFiles.TryGetValue(dir, out var childFiles))
            {
                files.AddRange(childFiles);
            }
        }

        return files;
    }

    private static RootResolution ResolveBestRootCandidate(
        string normalizedRoot,
        NtfsFileId initialRoot,
        IReadOnlyDictionary<NtfsFileId, List<NtfsFileId>> childrenDirs,
        IReadOnlyDictionary<NtfsFileId, List<NtfsFileId>> childrenFiles,
        IReadOnlyDictionary<NtfsFileId, UsnEntry> entries)
    {
        var candidates = new List<NtfsFileId>
        {
            initialRoot,
            initialRoot.ToLegacy48(),
            NtfsRootFrn
        };

        var matchedRoot = ResolveRootByTopLevelDirectoryMatches(normalizedRoot, childrenDirs, entries);
        if (!matchedRoot.Equals(default))
        {
            candidates.Add(matchedRoot);
            candidates.Add(matchedRoot.ToLegacy48());
        }

        var bestRoot = initialRoot;
        var bestDirs = new HashSet<NtfsFileId>();
        var bestFiles = new List<NtfsFileId>();
        var bestScore = -1;
        var diagnostics = new List<string>();

        foreach (var candidate in candidates.Distinct())
        {
            var dirs = BuildReachableDirectories(candidate, childrenDirs, entries);
            if (!dirs.Contains(candidate))
            {
                dirs.Add(candidate);
            }

            var files = BuildReachableFiles(dirs, childrenFiles);
            var score = (dirs.Count * 3) + files.Count;
            diagnostics.Add($"{candidate.ToDebugString()} => d:{dirs.Count},f:{files.Count}");

            if (score > bestScore)
            {
                bestScore = score;
                bestRoot = candidate;
                bestDirs = dirs;
                bestFiles = files;
            }
        }

        return new RootResolution(bestRoot, bestDirs, bestFiles, string.Join(" | ", diagnostics));
    }

    private static NtfsFileId ResolveRootByTopLevelDirectoryMatches(
        string normalizedRoot,
        IReadOnlyDictionary<NtfsFileId, List<NtfsFileId>> childrenDirs,
        IReadOnlyDictionary<NtfsFileId, UsnEntry> entries)
    {
        var topLevelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            foreach (var directoryPath in Directory.EnumerateDirectories(normalizedRoot, "*", options))
            {
                var name = Path.GetFileName(directoryPath.TrimEnd('\\'));
                if (!string.IsNullOrWhiteSpace(name))
                {
                    topLevelNames.Add(name);
                }
            }
        }
        catch
        {
            return default;
        }

        if (topLevelNames.Count == 0)
        {
            return default;
        }

        var bestScore = 0;
        var bestRoot = default(NtfsFileId);

        foreach (var item in childrenDirs)
        {
            var score = 0;
            foreach (var childId in item.Value)
            {
                if (!entries.TryGetValue(childId, out var entry) || !entry.IsDirectory)
                {
                    continue;
                }

                if (topLevelNames.Contains(entry.Name))
                {
                    score += 1;
                }
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestRoot = item.Key;
            }
        }

        return bestScore >= 2 ? bestRoot : default;
    }

    private static void BuildFileSizesAndCounts(
        IEnumerable<NtfsFileId> reachableFiles,
        IReadOnlyDictionary<NtfsFileId, UsnEntry> entries,
        IDictionary<NtfsFileId, string> pathCache,
        IDictionary<NtfsFileId, long> sizeByFile,
        IDictionary<NtfsFileId, DateTime> lastModifiedByFile,
        IDictionary<NtfsFileId, AggregateInfo> aggregateByDirectory,
        NtfsFileId rootId,
        string rootPrefix,
        IProgress<(long BytesScanned, string CurrentPath)>? progress,
        CancellationToken ct)
    {
        var fileWorkItems = BuildFileWorkItems(reachableFiles, entries, pathCache, rootId, rootPrefix, ct);
        var metadataByIndex = new FileMetadata[fileWorkItems.Count];

        var totalScanned = 0L;
        var lastReportMs = 0L;
        aggregateByDirectory.TryGetValue(rootId, out var rootAggregate);

        var parallelOptions = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 12)
        };

        Parallel.For(0, fileWorkItems.Count, parallelOptions, index =>
        {
            parallelOptions.CancellationToken.ThrowIfCancellationRequested();

            var item = fileWorkItems[index];
            var metadata = GetFileMetadata(item.Path);
            metadataByIndex[index] = metadata;
            var size = metadata.SizeBytes;

            var scanned = Interlocked.Add(ref totalScanned, size);

            if (aggregateByDirectory.TryGetValue(item.ParentId, out var parentAggregate))
            {
                Interlocked.Add(ref parentAggregate.DirectSizeBytes, size);
                Interlocked.Increment(ref parentAggregate.DirectFileCount);
            }
            else if (rootAggregate is not null)
            {
                Interlocked.Add(ref rootAggregate.DirectSizeBytes, size);
                Interlocked.Increment(ref rootAggregate.DirectFileCount);
            }

            if (progress is null)
            {
                return;
            }

            var now = Environment.TickCount64;
            var last = Interlocked.Read(ref lastReportMs);
            if (now - last >= 120 && Interlocked.CompareExchange(ref lastReportMs, now, last) == last)
            {
                progress.Report((scanned, item.Path));
            }
        });

        for (var i = 0; i < fileWorkItems.Count; i += 1)
        {
            var item = fileWorkItems[i];
            var metadata = metadataByIndex[i];
            sizeByFile[item.FileId] = metadata.SizeBytes;
            if (metadata.LastModified != default)
            {
                lastModifiedByFile[item.FileId] = metadata.LastModified;
            }
        }

        progress?.Report((BytesScanned: Interlocked.Read(ref totalScanned), CurrentPath: rootPrefix));
    }

    private static List<FileWorkItem> BuildFileWorkItems(
        IEnumerable<NtfsFileId> reachableFiles,
        IReadOnlyDictionary<NtfsFileId, UsnEntry> entries,
        IDictionary<NtfsFileId, string> pathCache,
        NtfsFileId rootId,
        string rootPrefix,
        CancellationToken ct)
    {
        var workItems = new List<FileWorkItem>();

        foreach (var fileId in reachableFiles)
        {
            ct.ThrowIfCancellationRequested();
            if (!entries.TryGetValue(fileId, out var fileEntry))
            {
                continue;
            }

            var filePath = BuildPath(fileId, entries, pathCache, rootId, rootPrefix);
            if (string.IsNullOrWhiteSpace(filePath))
            {
                continue;
            }

            workItems.Add(new FileWorkItem(fileId, fileEntry.ParentId, filePath));
        }

        return workItems;
    }

    private static void PropagateDirectoryAggregates(
        NtfsFileId rootId,
        IReadOnlyDictionary<NtfsFileId, List<NtfsFileId>> childrenDirs,
        IDictionary<NtfsFileId, AggregateInfo> aggregateByDirectory)
    {
        var stack = new Stack<(NtfsFileId Id, bool Expanded)>();
        stack.Push((rootId, false));

        while (stack.Count > 0)
        {
            var (id, expanded) = stack.Pop();
            if (!aggregateByDirectory.TryGetValue(id, out var aggregate))
            {
                continue;
            }

            if (!expanded)
            {
                stack.Push((id, true));

                if (childrenDirs.TryGetValue(id, out var childDirs))
                {
                    foreach (var childId in childDirs)
                    {
                        if (!childId.Equals(id) && aggregateByDirectory.ContainsKey(childId))
                        {
                            stack.Push((childId, false));
                        }
                    }
                }

                continue;
            }

            aggregate.SizeBytes = aggregate.DirectSizeBytes;
            aggregate.FileCount = aggregate.DirectFileCount;
            aggregate.FolderCount = 0;

            if (!childrenDirs.TryGetValue(id, out var children))
            {
                continue;
            }

            foreach (var childId in children)
            {
                if (childId.Equals(id))
                {
                    continue;
                }

                if (!aggregateByDirectory.TryGetValue(childId, out var childAggregate))
                {
                    continue;
                }

                aggregate.SizeBytes += childAggregate.SizeBytes;
                aggregate.FileCount += childAggregate.FileCount;
                aggregate.FolderCount += 1 + childAggregate.FolderCount;
            }
        }
    }

    private static void BuildDiskTree(
        NtfsFileId parentId,
        DiskNode parentNode,
        int depth,
        NtfsFileId rootId,
        IReadOnlyDictionary<NtfsFileId, UsnEntry> entries,
        IReadOnlyDictionary<NtfsFileId, List<NtfsFileId>> childrenDirs,
        IReadOnlyDictionary<NtfsFileId, List<NtfsFileId>> childrenFiles,
        IDictionary<NtfsFileId, string> pathCache,
        IReadOnlyDictionary<NtfsFileId, long> sizeByFile,
        IReadOnlyDictionary<NtfsFileId, DateTime> lastModifiedByFile,
        IReadOnlyDictionary<NtfsFileId, AggregateInfo> aggregateByDirectory,
        string rootPrefix,
        CancellationToken ct)
    {
        var dirChildren = GetSortedChildren(parentId, childrenDirs, entries, directories: true);
        var fileChildren = GetSortedChildren(parentId, childrenFiles, entries, directories: false);

        foreach (var directory in dirChildren)
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = BuildPath(directory.Id, entries, pathCache, rootId, rootPrefix);
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                continue;
            }

            var node = CreateDirectoryNode(fullPath, depth + 1, directory.LastModified);
            node.Parent = parentNode;
            node.ChildrenLoaded = true;

            if (aggregateByDirectory.TryGetValue(directory.Id, out var aggregate))
            {
                node.SizeBytes = aggregate.SizeBytes;
                node.FileCount = aggregate.FileCount;
                node.FolderCount = aggregate.FolderCount;
            }

            node.HasChildren = (childrenDirs.TryGetValue(directory.Id, out var subDirs) && subDirs.Count > 0)
                || (childrenFiles.TryGetValue(directory.Id, out var subFiles) && subFiles.Count > 0);

            parentNode.Children.Add(node);

            BuildDiskTree(
                directory.Id,
                node,
                depth + 1,
                rootId,
                entries,
                childrenDirs,
                childrenFiles,
                pathCache,
                sizeByFile,
                lastModifiedByFile,
                aggregateByDirectory,
                rootPrefix,
                ct);
        }

        foreach (var file in fileChildren)
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = BuildPath(file.Id, entries, pathCache, rootId, rootPrefix);
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                continue;
            }

            var extension = Path.GetExtension(file.Name) ?? string.Empty;
            var size = sizeByFile.TryGetValue(file.Id, out var value) ? value : 0;
            var lastModified = lastModifiedByFile.TryGetValue(file.Id, out var fileLastModified)
                ? fileLastModified
                : file.LastModified;

            var fileNode = new DiskNode
            {
                Name = file.Name,
                FullPath = fullPath,
                IsDirectory = false,
                SizeBytes = size,
                Extension = extension.ToLowerInvariant(),
                LastModified = lastModified,
                FileCount = 1,
                Depth = depth + 1,
                Parent = parentNode,
                HasChildren = false,
                ChildrenLoaded = true
            };

            parentNode.Children.Add(fileNode);
        }
    }

    private static List<UsnEntry> GetSortedChildren(
        NtfsFileId parentId,
        IReadOnlyDictionary<NtfsFileId, List<NtfsFileId>> map,
        IReadOnlyDictionary<NtfsFileId, UsnEntry> entries,
        bool directories)
    {
        var result = new List<UsnEntry>();
        if (!map.TryGetValue(parentId, out var ids))
        {
            return result;
        }

        foreach (var id in ids)
        {
            if (entries.TryGetValue(id, out var entry) && entry.IsDirectory == directories)
            {
                result.Add(entry);
            }
        }

        result.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        return result;
    }

    private static string BuildPath(
        NtfsFileId id,
        IReadOnlyDictionary<NtfsFileId, UsnEntry> entries,
        IDictionary<NtfsFileId, string> pathCache,
        NtfsFileId rootId,
        string rootPrefix)
    {
        if (pathCache.TryGetValue(id, out var cached))
        {
            return cached;
        }

        if (!entries.TryGetValue(id, out var entry))
        {
            return string.Empty;
        }

        var names = new Stack<string>();
        var cursor = id;
        var guard = 0;

        while (!cursor.Equals(rootId) && guard < 1024)
        {
            if (pathCache.TryGetValue(cursor, out var cachedPath))
            {
                while (names.Count > 0)
                {
                    cachedPath = Path.Combine(cachedPath, names.Pop());
                }

                pathCache[id] = cachedPath;
                return cachedPath;
            }

            if (!entries.TryGetValue(cursor, out var current))
            {
                return string.Empty;
            }

            var safeName = SanitizeEntryName(current.Name);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                return string.Empty;
            }

            names.Push(safeName);
            if (current.ParentId.Equals(cursor))
            {
                return string.Empty;
            }

            cursor = current.ParentId;
            guard += 1;
        }

        var path = rootPrefix;
        while (names.Count > 0)
        {
            path = Path.Combine(path, names.Pop());
        }

        pathCache[id] = path;
        return path;
    }

    private static FileMetadata GetFileMetadata(string filePath)
    {
        if (!GetFileAttributesEx(filePath, GetFileExInfoStandard, out var data))
        {
            if (TryGetFileMetadataByHandle(filePath, out var fallbackMetadata))
            {
                return fallbackMetadata;
            }

            if (IsProtectedPagingFile(filePath)
                && TryGetCompressedFileSize(filePath, out var compressedFallbackSize)
                && compressedFallbackSize > 0)
            {
                return new FileMetadata(compressedFallbackSize, DateTime.MinValue);
            }

            return default;
        }

        if ((data.FileAttributes & FileAttributeDirectory) != 0)
        {
            return default;
        }

        var size = ((ulong)data.FileSizeHigh << 32) | data.FileSizeLow;
        var normalizedSize = size > long.MaxValue ? long.MaxValue : (long)size;
        var lastModified = DateTimeFromFileTime((((long)data.LastWriteTime.HighDateTime) << 32) | data.LastWriteTime.LowDateTime);

        if (normalizedSize == 0 && IsProtectedPagingFile(filePath)
            && TryGetFileMetadataByHandle(filePath, out var protectedMetadata)
            && protectedMetadata.SizeBytes > 0)
        {
            return protectedMetadata;
        }

        if (normalizedSize == 0 && IsProtectedPagingFile(filePath)
            && TryGetCompressedFileSize(filePath, out var compressedSize)
            && compressedSize > 0)
        {
            return new FileMetadata(compressedSize, lastModified);
        }

        return new FileMetadata(normalizedSize, lastModified);
    }

    private static bool TryGetCompressedFileSize(string filePath, out long sizeBytes)
    {
        sizeBytes = 0;

        var lowSize = GetCompressedFileSize(filePath, out var highSize);
        if (lowSize == InvalidFileSize && Marshal.GetLastWin32Error() != 0)
        {
            return false;
        }

        var size = ((ulong)highSize << 32) | lowSize;
        sizeBytes = size > long.MaxValue ? long.MaxValue : (long)size;
        return true;
    }

    private static bool TryGetFileMetadataByHandle(string filePath, out FileMetadata metadata)
    {
        metadata = default;

        using var handle = CreateFile(
            filePath,
            FileReadAttributes,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            return false;
        }

        if (!GetFileInformationByHandleEx(handle, FileStandardInfoClass, out FileStandardInfo standardInfo, (uint)Marshal.SizeOf<FileStandardInfo>()))
        {
            return false;
        }

        if (standardInfo.Directory)
        {
            return false;
        }

        var lastModified = DateTime.MinValue;
        if (GetFileTime(handle, out _, out _, out var writeTime))
        {
            lastModified = DateTimeFromFileTime((((long)writeTime.HighDateTime) << 32) | writeTime.LowDateTime);
        }

        metadata = new FileMetadata(standardInfo.EndOfFile, lastModified);
        return true;
    }

    private static bool IsProtectedPagingFile(string filePath)
    {
        var name = Path.GetFileName(filePath);
        return string.Equals(name, "pagefile.sys", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "hiberfil.sys", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "swapfile.sys", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime TryGetPathLastWriteTime(string path)
    {
        if (!GetFileAttributesEx(path, GetFileExInfoStandard, out var data))
        {
            return DateTime.MinValue;
        }

        return DateTimeFromFileTime((((long)data.LastWriteTime.HighDateTime) << 32) | data.LastWriteTime.LowDateTime);
    }

    private static DateTime DateTimeFromFileTime(long fileTime)
    {
        if (fileTime <= 0)
        {
            return DateTime.MinValue;
        }

        try
        {
            return DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static DiskNode CreateDirectoryNode(string path, int depth, DateTime lastModified)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = trimmed;
        }

        return new DiskNode
        {
            Name = name,
            FullPath = path,
            IsDirectory = true,
            LastModified = lastModified,
            Depth = depth
        };
    }

    private static string SanitizeEntryName(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var nullIndex = value.IndexOf('\0');
        if (nullIndex >= 0)
        {
            value = value[..nullIndex];
        }

        return value.Trim();
    }

    private static T ReadStruct<T>(byte[] source) where T : struct
    {
        var handle = GCHandle.Alloc(source, GCHandleType.Pinned);
        try
        {
            return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }

    private readonly struct NtfsFileId : IEquatable<NtfsFileId>
    {
        public NtfsFileId(ulong low, ulong high)
        {
            Low = low;
            High = high;
        }

        public ulong Low { get; }
        public ulong High { get; }

        public static NtfsFileId FromUInt64(ulong value) => new(value, 0);

        public NtfsFileId ToLegacy48()
            => new(Low & 0x0000FFFFFFFFFFFFUL, 0);

        public string ToDebugString() => $"{High:x16}:{Low:x16}";

        public bool Equals(NtfsFileId other) => Low == other.Low && High == other.High;
        public override bool Equals(object? obj) => obj is NtfsFileId other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Low, High);
    }

    private readonly struct UsnEntry
    {
        public UsnEntry(NtfsFileId id, NtfsFileId parentId, string name, bool isDirectory, DateTime lastModified)
        {
            Id = id;
            ParentId = parentId;
            Name = name;
            IsDirectory = isDirectory;
            LastModified = lastModified;
        }

        public NtfsFileId Id { get; }
        public NtfsFileId ParentId { get; }
        public string Name { get; }
        public bool IsDirectory { get; }
        public DateTime LastModified { get; }
    }

    private readonly struct FileMetadata
    {
        public FileMetadata(long sizeBytes, DateTime lastModified)
        {
            SizeBytes = sizeBytes;
            LastModified = lastModified;
        }

        public long SizeBytes { get; }
        public DateTime LastModified { get; }
    }

    private readonly struct FileWorkItem
    {
        public FileWorkItem(NtfsFileId fileId, NtfsFileId parentId, string path)
        {
            FileId = fileId;
            ParentId = parentId;
            Path = path;
        }

        public NtfsFileId FileId { get; }
        public NtfsFileId ParentId { get; }
        public string Path { get; }
    }

    private sealed class RootResolution
    {
        public RootResolution(NtfsFileId rootId, HashSet<NtfsFileId> reachableDirs, List<NtfsFileId> reachableFiles, string diagnostics)
        {
            RootId = rootId;
            ReachableDirs = reachableDirs;
            ReachableFiles = reachableFiles;
            Diagnostics = diagnostics;
        }

        public NtfsFileId RootId { get; }
        public HashSet<NtfsFileId> ReachableDirs { get; }
        public List<NtfsFileId> ReachableFiles { get; }
        public string Diagnostics { get; }
    }

    private sealed class AggregateInfo
    {
        public long DirectSizeBytes;
        public int DirectFileCount;
        public long SizeBytes;
        public int FileCount;
        public int FolderCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MftEnumDataV0
    {
        public ulong StartFileReferenceNumber;
        public long LowUsn;
        public long HighUsn;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UsnJournalDataV0
    {
        public ulong UsnJournalId;
        public long FirstUsn;
        public long NextUsn;
        public long LowestValidUsn;
        public long MaxUsn;
        public ulong MaximumSize;
        public ulong AllocationDelta;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileStandardInfo
    {
        public long AllocationSize;
        public long EndOfFile;
        public uint NumberOfLinks;
        [MarshalAs(UnmanagedType.U1)] public bool DeletePending;
        [MarshalAs(UnmanagedType.U1)] public bool Directory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32FileAttributeData
    {
        public uint FileAttributes;
        public FileTime CreationTime;
        public FileTime LastAccessTime;
        public FileTime LastWriteTime;
        public uint FileSizeHigh;
        public uint FileSizeLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo
    {
        public ulong VolumeSerialNumber;
        public ulong FileIdLow;
        public ulong FileIdHigh;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        IntPtr inBuffer,
        uint inBufferSize,
        [Out] byte[] outBuffer,
        uint outBufferSize,
        out int bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle handle,
        out ByHandleFileInformation information);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle handle,
        int fileInformationClass,
        IntPtr fileInformation,
        uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle handle,
        int fileInformationClass,
        out FileStandardInfo fileInformation,
        uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileTime(
        SafeFileHandle handle,
        out FileTime creationTime,
        out FileTime lastAccessTime,
        out FileTime lastWriteTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetFileAttributesEx(
        string name,
        int fileInfoLevelId,
        out Win32FileAttributeData fileInformation);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetCompressedFileSizeW")]
    private static extern uint GetCompressedFileSize(
        string fileName,
        out uint fileSizeHigh);
}
