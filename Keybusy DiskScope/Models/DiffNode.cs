namespace Keybusy_DiskScope.Models;

/// <summary>
/// Represents the difference of a single path between two snapshots.
/// </summary>
public sealed class DiffNode
{
    public string FullPath { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public DiffStatus Status { get; init; }
    public bool IsExpanded { get; set; }
    public DiffNode? Parent { get; set; }

    public long SizeBefore { get; init; }
    public long SizeAfter { get; init; }
    public long SizeDelta => SizeAfter - SizeBefore;
    public DateTime LastModifiedBefore { get; init; }
    public DateTime LastModifiedAfter { get; init; }
    public DateTime LastModifiedEffective => LastModifiedAfter != default ? LastModifiedAfter : LastModifiedBefore;

    public string DisplayDelta
    {
        get
        {
            string formatted = DiskNode.FormatSize(Math.Abs(SizeDelta));
            return SizeDelta >= 0 ? $"+{formatted}" : $"-{formatted}";
        }
    }

    /// <summary>Icon glyph for diff status (Segoe Fluent Icons).</summary>
    public string StatusGlyph => Status switch
    {
        DiffStatus.Added   => "\uE710", // Add
        DiffStatus.Removed => "\uE711", // Remove
        DiffStatus.Grown   => "\uE74A", // Up arrow
        DiffStatus.Shrunk  => "\uE74B", // Down arrow
        _                  => "\uE8BD"  // CheckMark / Unchanged
    };

    public List<DiffNode> Children { get; init; } = new();
}

public enum DiffStatus
{
    Unchanged,
    Added,
    Removed,
    Grown,
    Shrunk
}
