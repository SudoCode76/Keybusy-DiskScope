namespace Keybusy_DiskScope.Services;

public interface IFileDeleteService
{
    Task DeleteAsync(string path, bool permanent, CancellationToken ct);
}
