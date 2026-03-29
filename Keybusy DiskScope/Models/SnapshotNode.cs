namespace Keybusy_DiskScope.Models;

public sealed class SnapshotNode
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public string Extension { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public long SizeBytes { get; init; }
    public int FileCount { get; init; }
    public int FolderCount { get; init; }
    public DateTime LastModified { get; init; }
    public List<SnapshotNode> Children { get; init; } = new();
}
