namespace Keybusy_DiskScope.Services;

public interface IDriveInfoService
{
    Task<DriveInfoResult> GetDrivesAsync(CancellationToken ct);
}
