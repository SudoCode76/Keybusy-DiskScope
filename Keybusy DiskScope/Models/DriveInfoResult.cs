namespace Keybusy_DiskScope.Models;

public sealed class DriveInfoResult
{
    public required IReadOnlyList<DriveInfoData> Drives { get; init; }
    public required IReadOnlyList<string> Diagnostics { get; init; }
}
