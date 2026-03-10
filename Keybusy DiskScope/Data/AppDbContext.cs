using Microsoft.EntityFrameworkCore;

namespace Keybusy_DiskScope.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<SnapshotRecord> Snapshots => Set<SnapshotRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SnapshotRecord>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Name).IsRequired().HasMaxLength(200);
            e.Property(s => s.DrivePath).IsRequired().HasMaxLength(260);
            e.Property(s => s.TreeJson).IsRequired();
        });
    }
}
