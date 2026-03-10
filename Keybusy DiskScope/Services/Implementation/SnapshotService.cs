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
        => string.IsNullOrEmpty(record.TreeJson)
            ? null
            : JsonSerializer.Deserialize<DiskNode>(record.TreeJson);

    public static string SerializeTree(DiskNode root)
        => JsonSerializer.Serialize(root);
}
