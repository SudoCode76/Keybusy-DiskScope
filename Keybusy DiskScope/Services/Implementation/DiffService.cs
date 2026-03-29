using System.Text.Json;

using Keybusy_DiskScope.Services.Implementation;

namespace Keybusy_DiskScope.Services.Implementation;

public sealed class DiffService : IDiffService
{
    public DiffNode Compare(SnapshotRecord before, SnapshotRecord after)
    {
        var treeBefore = SnapshotService.DeserializeTree(before);
        var treeAfter  = SnapshotService.DeserializeTree(after);

        if (treeBefore is null || treeAfter is null)
            throw new InvalidOperationException("One or both snapshots have invalid tree data.");

        return DiffNodes(treeBefore, treeAfter);
    }

    private static DiffNode DiffNodes(DiskNode? before, DiskNode? after)
    {
        string path = after?.FullPath ?? before?.FullPath ?? string.Empty;
        string name = after?.Name     ?? before?.Name     ?? string.Empty;
        bool isDir  = after?.IsDirectory ?? before?.IsDirectory ?? false;

        long sizeBefore = before?.SizeBytes ?? 0;
        long sizeAfter  = after?.SizeBytes  ?? 0;
        long delta      = sizeAfter - sizeBefore;

        var status = (before, after) switch
        {
            (null, _)  => DiffStatus.Added,
            (_, null)  => DiffStatus.Removed,
            _ when delta > 0 => DiffStatus.Grown,
            _ when delta < 0 => DiffStatus.Shrunk,
            _ => DiffStatus.Unchanged
        };

        var node = new DiffNode
        {
            FullPath   = path,
            Name       = name,
            IsDirectory = isDir,
            Status     = status,
            SizeBefore = sizeBefore,
            SizeAfter  = sizeAfter
        };

        if (isDir)
        {
            var beforeChildren = (before?.Children ?? new()).ToDictionary(c => c.FullPath);
            var afterChildren  = (after?.Children  ?? new()).ToDictionary(c => c.FullPath);
            var allKeys = beforeChildren.Keys.Union(afterChildren.Keys);

            foreach (string key in allKeys)
            {
                beforeChildren.TryGetValue(key, out var b);
                afterChildren.TryGetValue(key,  out var a);
                var childDiff = DiffNodes(b, a);
                childDiff.Parent = node;
                if (childDiff.Status != DiffStatus.Unchanged || childDiff.Children.Count > 0)
                {
                    node.Children.Add(childDiff);
                }
            }
        }

        return node;
    }
}
