using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Keybusy_DiskScope.Services.Implementation;

public sealed class NtfsFastScanService : INtfsFastScanService
{
    private const ulong FrnMask = 0x0000FFFFFFFFFFFF;
    private const uint GenericRead = 0x80000000;
    private const uint FileReadAttributes = 0x80;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeReparsePoint = 0x00000400;

    private const uint FsctlEnumUsnData = 0x000900b3;
    private const uint FsctlQueryUsnJournal = 0x000900f4;

    private readonly ILogger<NtfsFastScanService> _logger;

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
        if (!IsAdministrator())
        {
            _logger.LogInformation("Fast NTFS scan skipped: process is not elevated.");
            return null;
        }

        var normalizedRoot = NormalizeRootPath(rootPath);
        if (normalizedRoot is null)
        {
            _logger.LogInformation("Fast NTFS scan skipped: invalid root path {RootPath}", rootPath);
            return null;
        }

        if (!IsNtfsRoot(normalizedRoot))
        {
            _logger.LogInformation("Fast NTFS scan skipped: {RootPath} is not NTFS.", normalizedRoot);
            return null;
        }

        if (!TryGetVolumeHandlePath(normalizedRoot, out var volumePath))
        {
            _logger.LogInformation("Fast NTFS scan skipped: cannot resolve volume handle for {RootPath}", normalizedRoot);
            return null;
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
            _logger.LogWarning("Fast NTFS scan unavailable. CreateFile failed for {VolumePath}. Win32={Error}", volumePath, Marshal.GetLastWin32Error());
            return null;
        }

        if (!TryGetRootFrn(normalizedRoot, out var rootFrn))
        {
            _logger.LogWarning("Fast NTFS scan fallback: cannot resolve root FRN for {RootPath}", normalizedRoot);
            return null;
        }

        if (!TryQueryUsnJournal(volumeHandle, out var usnJournalData))
        {
            _logger.LogWarning("Fast NTFS scan fallback: cannot query USN journal for {RootPath}", normalizedRoot);
            return null;
        }

        var entries = new Dictionary<ulong, UsnEntry>(capacity: 256 * 1024);
        if (!TryEnumerateUsnRecords(volumeHandle, usnJournalData.NextUsn, entries, ct))
        {
            _logger.LogWarning("Fast NTFS scan fallback: failed enumerating USN records for {RootPath}", normalizedRoot);
            return null;
        }

        var childrenDirs = new Dictionary<ulong, List<ulong>>();
        var childrenFiles = new Dictionary<ulong, List<ulong>>();
        BuildChildMaps(entries, childrenDirs, childrenFiles);

        var reachableDirs = BuildReachableDirectories(rootFrn, childrenDirs, entries);
        if (!reachableDirs.Contains(rootFrn))
        {
            reachableDirs.Add(rootFrn);
        }

        var reachableFiles = BuildReachableFiles(reachableDirs, childrenFiles);
        if (reachableDirs.Count <= 1 && reachableFiles.Count == 0)
        {
            _logger.LogWarning("Fast NTFS scan fallback: no reachable entries for {RootPath}", normalizedRoot);
            return null;
        }

        var pathCache = new Dictionary<ulong, string>();
        pathCache[rootFrn] = normalizedRoot.TrimEnd('\\');

        var sizeByFileFrn = new Dictionary<ulong, long>(reachableFiles.Count);
        var aggregateByDirectoryFrn = new Dictionary<ulong, AggregateInfo>(reachableDirs.Count);
        foreach (var directoryFrn in reachableDirs)
        {
            aggregateByDirectoryFrn[directoryFrn] = new AggregateInfo();
        }

        BuildFolderCounts(reachableDirs, entries, aggregateByDirectoryFrn, rootFrn);
        BuildFileSizesAndCounts(
            reachableFiles,
            entries,
            pathCache,
            sizeByFileFrn,
            aggregateByDirectoryFrn,
            rootFrn,
            normalizedRoot,
            progress,
            ct);

        var rootNode = CreateDirectoryNode(normalizedRoot, depth: 0);
        rootNode.ChildrenLoaded = true;
        rootNode.HasChildren = true;

        BuildDiskTree(
            rootFrn,
            rootNode,
            0,
            rootFrn,
            entries,
            childrenDirs,
            childrenFiles,
            pathCache,
            sizeByFileFrn,
            aggregateByDirectoryFrn,
            normalizedRoot,
            ct);

        if (aggregateByDirectoryFrn.TryGetValue(rootFrn, out var rootAgg))
        {
            rootNode.SizeBytes = rootAgg.SizeBytes;
            rootNode.FileCount = rootAgg.FileCount;
            rootNode.FolderCount = rootAgg.FolderCount;
        }

        rootNode.HasChildren = rootNode.Children.Count > 0;
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
        if (!string.Equals(path.TrimEnd('\\'), normalizedRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return normalizedRoot;
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

    private static bool TryGetRootFrn(string rootPath, out ulong rootFrn)
    {
        rootFrn = 0;
        using var directoryHandle = CreateFile(
            rootPath,
            FileReadAttributes,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            IntPtr.Zero);

        if (directoryHandle.IsInvalid)
        {
            return false;
        }

        if (!GetFileInformationByHandle(directoryHandle, out var info))
        {
            return false;
        }

        rootFrn = NormalizeFrn(((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
        return rootFrn != 0;
    }

    private static bool TryQueryUsnJournal(SafeFileHandle volumeHandle, out UsnJournalDataV0 data)
    {
        var outSize = Marshal.SizeOf<UsnJournalDataV0>();
        var outBuffer = new byte[outSize];
        if (!DeviceIoControl(volumeHandle, FsctlQueryUsnJournal, IntPtr.Zero, 0, outBuffer, (uint)outSize, out var bytesReturned, IntPtr.Zero)
            || bytesReturned < outSize)
        {
            data = default;
            return false;
        }

        data = MemoryMarshalRead<UsnJournalDataV0>(outBuffer);
        return true;
    }

    private static bool TryEnumerateUsnRecords(
        SafeFileHandle volumeHandle,
        long highUsn,
        Dictionary<ulong, UsnEntry> entries,
        CancellationToken ct)
    {
        var enumData = new MftEnumDataV0
        {
            StartFileReferenceNumber = 0,
            LowUsn = 0,
            HighUsn = highUsn
        };

        var outBuffer = new byte[1024 * 1024];
        var inBufferSize = Marshal.SizeOf<MftEnumDataV0>();
        var inBuffer = Marshal.AllocHGlobal(inBufferSize);

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
                    (uint)inBufferSize,
                    outBuffer,
                    (uint)outBuffer.Length,
                    out var bytesReturned,
                    IntPtr.Zero))
                {
                    var error = Marshal.GetLastWin32Error();
                    const int ErrorHandleEof = 38;
                    if (error == ErrorHandleEof)
                    {
                        return true;
                    }

                    return false;
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
                        var frn = NormalizeFrn(BitConverter.ToUInt64(outBuffer, offset + 8));
                        var parentFrn = NormalizeFrn(BitConverter.ToUInt64(outBuffer, offset + 16));
                        var fileAttributes = BitConverter.ToUInt32(outBuffer, offset + 56);
                        var nameLength = BitConverter.ToUInt16(outBuffer, offset + 60);
                        var nameOffset = BitConverter.ToUInt16(outBuffer, offset + 62);

                        if (nameOffset + nameLength <= recordLength)
                        {
                            var name = Encoding.Unicode.GetString(outBuffer, offset + nameOffset, nameLength);
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                var isDirectory = (fileAttributes & FileAttributeDirectory) != 0;
                                var isReparsePoint = (fileAttributes & FileAttributeReparsePoint) != 0;
                                if (!isReparsePoint)
                                {
                                    entries[frn] = new UsnEntry(frn, parentFrn, name, isDirectory);
                                }
                            }
                        }
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

    private static void BuildChildMaps(
        IReadOnlyDictionary<ulong, UsnEntry> entries,
        Dictionary<ulong, List<ulong>> childrenDirs,
        Dictionary<ulong, List<ulong>> childrenFiles)
    {
        foreach (var entry in entries.Values)
        {
            var target = entry.IsDirectory ? childrenDirs : childrenFiles;
            if (!target.TryGetValue(entry.ParentFrn, out var list))
            {
                list = new List<ulong>();
                target[entry.ParentFrn] = list;
            }

            list.Add(entry.Frn);
        }
    }

    private static HashSet<ulong> BuildReachableDirectories(
        ulong rootFrn,
        IReadOnlyDictionary<ulong, List<ulong>> childrenDirs,
        IReadOnlyDictionary<ulong, UsnEntry> entries)
    {
        var reachable = new HashSet<ulong> { rootFrn };
        var queue = new Queue<ulong>();
        queue.Enqueue(rootFrn);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!childrenDirs.TryGetValue(current, out var children))
            {
                continue;
            }

            foreach (var childFrn in children)
            {
                if (!entries.TryGetValue(childFrn, out var entry) || !entry.IsDirectory)
                {
                    continue;
                }

                if (reachable.Add(childFrn))
                {
                    queue.Enqueue(childFrn);
                }
            }
        }

        return reachable;
    }

    private static List<ulong> BuildReachableFiles(
        IEnumerable<ulong> reachableDirs,
        IReadOnlyDictionary<ulong, List<ulong>> childrenFiles)
    {
        var files = new List<ulong>();
        foreach (var directoryFrn in reachableDirs)
        {
            if (childrenFiles.TryGetValue(directoryFrn, out var fileChildren))
            {
                files.AddRange(fileChildren);
            }
        }

        return files;
    }

    private static void BuildFolderCounts(
        IEnumerable<ulong> reachableDirs,
        IReadOnlyDictionary<ulong, UsnEntry> entries,
        IDictionary<ulong, AggregateInfo> aggregateByDirectoryFrn,
        ulong rootFrn)
    {
        foreach (var directoryFrn in reachableDirs)
        {
            if (directoryFrn == rootFrn)
            {
                continue;
            }

            var currentParent = entries.TryGetValue(directoryFrn, out var dir) ? dir.ParentFrn : 0;
            var safety = 0;
            while (currentParent != 0 && safety < 512)
            {
                if (!aggregateByDirectoryFrn.TryGetValue(currentParent, out var aggregate))
                {
                    break;
                }

                aggregate.FolderCount += 1;

                if (currentParent == rootFrn)
                {
                    break;
                }

                if (!entries.TryGetValue(currentParent, out var parent))
                {
                    break;
                }

                if (parent.ParentFrn == currentParent)
                {
                    break;
                }

                currentParent = parent.ParentFrn;
                safety += 1;
            }
        }
    }

    private static void BuildFileSizesAndCounts(
        IEnumerable<ulong> reachableFiles,
        IReadOnlyDictionary<ulong, UsnEntry> entries,
        IDictionary<ulong, string> pathCache,
        IDictionary<ulong, long> sizeByFileFrn,
        IDictionary<ulong, AggregateInfo> aggregateByDirectoryFrn,
        ulong rootFrn,
        string normalizedRoot,
        IProgress<(long BytesScanned, string CurrentPath)>? progress,
        CancellationToken ct)
    {
        var totalScanned = 0L;
        var lastReportMs = 0L;

        foreach (var fileFrn in reachableFiles)
        {
            ct.ThrowIfCancellationRequested();
            if (!entries.TryGetValue(fileFrn, out var fileEntry))
            {
                continue;
            }

            var filePath = BuildPath(fileFrn, entries, pathCache, rootFrn, normalizedRoot);
            if (string.IsNullOrWhiteSpace(filePath))
            {
                continue;
            }

            var size = TryGetFileSize(filePath);
            sizeByFileFrn[fileFrn] = size;
            totalScanned += size;

            var nowMs = Environment.TickCount64;
            if (progress is not null && nowMs - lastReportMs >= 120)
            {
                lastReportMs = nowMs;
                progress.Report((totalScanned, filePath));
            }

            var currentParent = fileEntry.ParentFrn;
            var safety = 0;
            while (currentParent != 0 && safety < 512)
            {
                if (!aggregateByDirectoryFrn.TryGetValue(currentParent, out var aggregate))
                {
                    break;
                }

                aggregate.SizeBytes += size;
                aggregate.FileCount += 1;

                if (currentParent == rootFrn)
                {
                    break;
                }

                if (!entries.TryGetValue(currentParent, out var parent))
                {
                    break;
                }

                if (parent.ParentFrn == currentParent)
                {
                    break;
                }

                currentParent = parent.ParentFrn;
                safety += 1;
            }
        }

        progress?.Report((totalScanned, normalizedRoot));
    }

    private static void BuildDiskTree(
        ulong parentFrn,
        DiskNode parentNode,
        int depth,
        ulong rootFrn,
        IReadOnlyDictionary<ulong, UsnEntry> entries,
        IReadOnlyDictionary<ulong, List<ulong>> childrenDirs,
        IReadOnlyDictionary<ulong, List<ulong>> childrenFiles,
        IDictionary<ulong, string> pathCache,
        IReadOnlyDictionary<ulong, long> sizeByFileFrn,
        IReadOnlyDictionary<ulong, AggregateInfo> aggregateByDirectoryFrn,
        string normalizedRoot,
        CancellationToken ct)
    {
        var directoryChildren = new List<UsnEntry>();
        if (childrenDirs.TryGetValue(parentFrn, out var childDirFrns))
        {
            foreach (var childDirFrn in childDirFrns)
            {
                if (entries.TryGetValue(childDirFrn, out var childDir) && childDir.IsDirectory)
                {
                    directoryChildren.Add(childDir);
                }
            }
        }

        var fileChildren = new List<UsnEntry>();
        if (childrenFiles.TryGetValue(parentFrn, out var childFileFrns))
        {
            foreach (var childFileFrn in childFileFrns)
            {
                if (entries.TryGetValue(childFileFrn, out var childFile) && !childFile.IsDirectory)
                {
                    fileChildren.Add(childFile);
                }
            }
        }

        directoryChildren.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        fileChildren.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));

        foreach (var directory in directoryChildren)
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = BuildPath(directory.Frn, entries, pathCache, rootFrn, normalizedRoot);
            var node = CreateDirectoryNode(fullPath, depth + 1);
            node.Parent = parentNode;
            node.ChildrenLoaded = true;

            if (aggregateByDirectoryFrn.TryGetValue(directory.Frn, out var aggregate))
            {
                node.SizeBytes = aggregate.SizeBytes;
                node.FileCount = aggregate.FileCount;
                node.FolderCount = aggregate.FolderCount;
            }

            node.HasChildren = (childrenDirs.TryGetValue(directory.Frn, out var dirs) && dirs.Count > 0)
                || (childrenFiles.TryGetValue(directory.Frn, out var files) && files.Count > 0);

            parentNode.Children.Add(node);

            BuildDiskTree(
                directory.Frn,
                node,
                depth + 1,
                rootFrn,
                entries,
                childrenDirs,
                childrenFiles,
                pathCache,
                sizeByFileFrn,
                aggregateByDirectoryFrn,
                normalizedRoot,
                ct);
        }

        foreach (var file in fileChildren)
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = BuildPath(file.Frn, entries, pathCache, rootFrn, normalizedRoot);
            var extension = Path.GetExtension(file.Name) ?? string.Empty;
            var size = sizeByFileFrn.TryGetValue(file.Frn, out var value) ? value : 0;

            var fileNode = new DiskNode
            {
                Name = file.Name,
                FullPath = fullPath,
                IsDirectory = false,
                SizeBytes = size,
                Extension = extension.ToLowerInvariant(),
                LastModified = DateTime.MinValue,
                FileCount = 1,
                Depth = depth + 1,
                Parent = parentNode,
                HasChildren = false,
                ChildrenLoaded = true
            };

            parentNode.Children.Add(fileNode);
        }
    }

    private static string BuildPath(
        ulong frn,
        IReadOnlyDictionary<ulong, UsnEntry> entries,
        IDictionary<ulong, string> pathCache,
        ulong rootFrn,
        string normalizedRoot)
    {
        if (pathCache.TryGetValue(frn, out var cached))
        {
            return cached;
        }

        if (!entries.TryGetValue(frn, out var entry))
        {
            return string.Empty;
        }

        var rootWithoutSlash = normalizedRoot.TrimEnd('\\');
        if (frn == rootFrn)
        {
            pathCache[frn] = rootWithoutSlash;
            return rootWithoutSlash;
        }

        var parentPath = rootWithoutSlash;
        if (entry.ParentFrn != rootFrn && entry.ParentFrn != 0)
        {
            var fromParent = BuildPath(entry.ParentFrn, entries, pathCache, rootFrn, normalizedRoot);
            if (!string.IsNullOrWhiteSpace(fromParent))
            {
                parentPath = fromParent;
            }
        }

        var fullPath = parentPath.EndsWith(':')
            ? $"{parentPath}\\{entry.Name}"
            : Path.Combine(parentPath, entry.Name);

        pathCache[frn] = fullPath;
        return fullPath;
    }

    private static ulong NormalizeFrn(ulong frn)
        => frn & FrnMask;

    private static long TryGetFileSize(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            return info.Exists ? info.Length : 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
        catch (IOException)
        {
            return 0;
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

        return new DiskNode
        {
            Name = name,
            FullPath = path,
            IsDirectory = true,
            LastModified = DateTime.MinValue,
            Depth = depth
        };
    }

    private static T MemoryMarshalRead<T>(byte[] source) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var handle = GCHandle.Alloc(source, GCHandleType.Pinned);
        try
        {
            return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject())!;
        }
        finally
        {
            handle.Free();
        }
    }

    private sealed class AggregateInfo
    {
        public long SizeBytes;
        public int FileCount;
        public int FolderCount;
    }

    private readonly struct UsnEntry
    {
        public UsnEntry(ulong frn, ulong parentFrn, string name, bool isDirectory)
        {
            Frn = frn;
            ParentFrn = parentFrn;
            Name = name;
            IsDirectory = isDirectory;
        }

        public ulong Frn { get; }
        public ulong ParentFrn { get; }
        public string Name { get; }
        public bool IsDirectory { get; }
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
}
