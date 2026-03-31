namespace Keybusy_DiskScope.Models;

/// <summary>
/// Represents a file or folder node in the scanned disk tree.
/// </summary>
public sealed class DiskNode
{
    public bool IsPlaceholder { get; init; }
    public bool IsFileGroup { get; init; }
    public bool IsNotPlaceholder => !IsPlaceholder;
    public bool IsActionable => !IsPlaceholder && !IsFileGroup;
    public bool IsFile => !IsDirectory;
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public long SizeBytes { get; set; }
    public string Extension { get; init; } = string.Empty;
    public DateTime LastModified { get; init; }
    public int FileCount { get; set; }
    public int FolderCount { get; set; }
    public double SizePercent { get; set; }
    public bool HasChildren { get; set; }
    public bool ChildrenLoaded { get; set; }
    public int Depth { get; set; }
    public bool IsExpanded { get; set; }
    public DiskNode? Parent { get; set; }
    public bool IsSelected { get; set; }

    public string ExpandGlyph => IsExpanded ? "\uE70D" : "\uE76C";

    /// <summary>Child nodes (only populated for directories).</summary>
    public ObservableCollection<DiskNode> Children { get; } = new();

    /// <summary>Human-readable size string, e.g. "1.2 GB".</summary>
    public string DisplaySize => IsPlaceholder ? string.Empty : FormatSize(SizeBytes);

    public string DisplayPercent => IsPlaceholder ? string.Empty : SizePercent <= 0 ? "0%" : $"{SizePercent:0}%";

    public string DisplayFileCount => IsPlaceholder ? string.Empty : FileCount.ToString("N0");

    public string DisplayLastModified => IsPlaceholder ? string.Empty : LastModified.ToString("g");

    /// <summary>Icon glyph for the node type (Segoe Fluent Icons).</summary>
    public string IconGlyph => IsDirectory ? "\uE8B7" : "\uE8A5"; // Folder / Document

    public static DiskNode CreatePlaceholder(int depth = 0)
        => new()
        {
            IsPlaceholder = true,
            Name = "Cargando...",
            IsDirectory = false,
            Depth = depth
        };

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1_024)         return $"{bytes / 1_024.0:F1} KB";
        return $"{bytes} B";
    }
}
