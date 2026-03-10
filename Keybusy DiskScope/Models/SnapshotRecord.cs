namespace Keybusy_DiskScope.Models;

/// <summary>
/// EF Core entity — one saved scan snapshot.
/// </summary>
public sealed class SnapshotRecord
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DrivePath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public long TotalSizeBytes { get; set; }

    /// <summary>Serialized JSON tree of DiskNode entries (stored as blob).</summary>
    public string TreeJson { get; set; } = string.Empty;

    public string DisplaySize => DiskNode.FormatSize(TotalSizeBytes);
    public string DisplayDate => CreatedAt.ToString("dd/MM/yyyy HH:mm");
}
