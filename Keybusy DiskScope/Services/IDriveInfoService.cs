namespace Keybusy_DiskScope.Services;

public interface IDriveInfoService
{
    Task<IReadOnlyList<DriveInfoData>> GetDrivesAsync(CancellationToken ct);
}
