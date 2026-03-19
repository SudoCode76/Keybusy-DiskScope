namespace Keybusy_DiskScope.Models;

/// <summary>
/// Represents a file or folder node in the scanned disk tree.
/// </summary>
public sealed class DiskNode
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public long SizeBytes { get; set; }
    public string Extension { get; init; } = string.Empty;
    public DateTime LastModified { get; init; }
    public int FileCount { get; set; }
    public int FolderCount { get; set; }
    public double SizePercent { get; set; }

    /// <summary>Child nodes (only populated for directories).</summary>
    public List<DiskNode> Children { get; init; } = new();

    /// <summary>Human-readable size string, e.g. "1.2 GB".</summary>
    public string DisplaySize => FormatSize(SizeBytes);

    public string DisplayPercent => SizePercent <= 0 ? "0%" : $"{SizePercent:0}%";

    public string DisplayFileCount => FileCount.ToString("N0");

    public string DisplayLastModified => LastModified.ToString("g");

    /// <summary>Icon glyph for the node type (Segoe Fluent Icons).</summary>
    public string IconGlyph => IsDirectory ? "\uE8B7" : "\uE8A5"; // Folder / Document

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1_024)         return $"{bytes / 1_024.0:F1} KB";
        return $"{bytes} B";
    }
}
