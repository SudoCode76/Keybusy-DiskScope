using Microsoft.VisualBasic.FileIO;

namespace Keybusy_DiskScope.Services.Implementation;

public sealed class FileDeleteService : IFileDeleteService
{
    public Task DeleteAsync(string path, bool permanent, CancellationToken ct)
    {
        return Task.Run(() => DeleteInternal(path, permanent, ct), ct);
    }

    private static void DeleteInternal(string path, bool permanent, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (permanent)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
                return;
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        if (Directory.Exists(path))
        {
            FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            return;
        }

        if (File.Exists(path))
        {
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
    }
}
