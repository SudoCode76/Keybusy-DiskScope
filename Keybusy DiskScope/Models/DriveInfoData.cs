namespace Keybusy_DiskScope.Models;

public sealed class DriveInfoData
{
    public required string Name { get; init; }
    public required string RootPath { get; init; }
    public required string VolumeLabel { get; init; }
    public required string FileSystem { get; init; }
    public DriveType DriveType { get; init; }
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
    public long UsedBytes { get; init; }
    public string? Model { get; init; }
    public string? MediaType { get; init; }
    public double? TemperatureC { get; init; }
    public string? HealthStatus { get; init; }
}
