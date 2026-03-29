using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Keybusy_DiskScope.Data;

namespace Keybusy_DiskScope.Services.Implementation;

public sealed class SnapshotService : ISnapshotService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public SnapshotService(IDbContextFactory<AppDbContext> dbFactory)
        => _dbFactory = dbFactory;

    public async Task<IReadOnlyList<SnapshotRecord>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Snapshots
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<SnapshotRecord?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Snapshots.FindAsync(new object[] { id }, ct);
    }

    public async Task SaveAsync(SnapshotRecord snapshot, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        if (snapshot.Id == 0)
            db.Snapshots.Add(snapshot);
        else
            db.Snapshots.Update(snapshot);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var record = await db.Snapshots.FindAsync(new object[] { id }, ct);
        if (record is not null)
        {
            db.Snapshots.Remove(record);
            await db.SaveChangesAsync(ct);
        }
    }

    // Helper: deserialize a snapshot's tree from JSON
    public static DiskNode? DeserializeTree(SnapshotRecord record)
    {
        if (string.IsNullOrEmpty(record.TreeJson))
        {
            return null;
        }

        var snapshot = JsonSerializer.Deserialize<SnapshotNode>(record.TreeJson);
        return snapshot is null ? null : MapToDiskNode(snapshot, parent: null, depth: 0);
    }

    public static string SerializeTree(DiskNode root)
    {
        var snapshot = MapToSnapshot(root);
        return JsonSerializer.Serialize(snapshot);
    }

    private static SnapshotNode MapToSnapshot(DiskNode node)
    {
        var children = new List<SnapshotNode>();
        foreach (var child in node.Children)
        {
            if (child.IsPlaceholder)
            {
                continue;
            }

            children.Add(MapToSnapshot(child));
        }

        return new SnapshotNode
        {
            Name = node.Name,
            FullPath = node.FullPath,
            Extension = node.Extension,
            IsDirectory = node.IsDirectory,
            SizeBytes = node.SizeBytes,
            FileCount = node.FileCount,
            FolderCount = node.FolderCount,
            LastModified = node.LastModified,
            Children = children
        };
    }

    private static DiskNode MapToDiskNode(SnapshotNode snapshot, DiskNode? parent, int depth)
    {
        var node = new DiskNode
        {
            Name = snapshot.Name,
            FullPath = snapshot.FullPath,
            Extension = snapshot.Extension,
            IsDirectory = snapshot.IsDirectory,
            SizeBytes = snapshot.SizeBytes,
            FileCount = snapshot.FileCount,
            FolderCount = snapshot.FolderCount,
            LastModified = snapshot.LastModified,
            Depth = depth,
            Parent = parent,
            HasChildren = snapshot.Children.Count > 0,
            ChildrenLoaded = true
        };

        foreach (var child in snapshot.Children)
        {
            node.Children.Add(MapToDiskNode(child, node, depth + 1));
        }

        return node;
    }
}
