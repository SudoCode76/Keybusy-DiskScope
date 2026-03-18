using System.Management;
using Microsoft.Extensions.Logging;

namespace Keybusy_DiskScope.Services.Implementation;

public sealed class DriveInfoService : IDriveInfoService
{
    private readonly ILogger<DriveInfoService> _logger;

    public DriveInfoService(ILogger<DriveInfoService> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<DriveInfoData>> GetDrivesAsync(CancellationToken ct)
    {
        return await Task.Run(() => GetDrivesInternal(ct), ct);
    }

    private IReadOnlyList<DriveInfoData> GetDrivesInternal(CancellationToken ct)
    {
        var physicalDisks = GetPhysicalDisks();
        var driveToDiskNumber = GetDriveToDiskNumberMap();

        var drives = new List<DriveInfoData>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            ct.ThrowIfCancellationRequested();

            if (!drive.IsReady)
            {
                continue;
            }

            var root = drive.RootDirectory.FullName;
            var letter = root.TrimEnd('\\');
            int? diskNumber = driveToDiskNumber.TryGetValue(letter, out var number) ? number : null;

            PhysicalDiskInfo? physical = null;
            if (diskNumber.HasValue && physicalDisks.TryGetValue(diskNumber.Value, out var info))
            {
                physical = info;
            }

            var usedBytes = drive.TotalSize - drive.TotalFreeSpace;
            drives.Add(new DriveInfoData
            {
                Name = drive.Name,
                RootPath = root,
                VolumeLabel = drive.VolumeLabel ?? string.Empty,
                FileSystem = drive.DriveFormat ?? string.Empty,
                DriveType = drive.DriveType,
                TotalBytes = drive.TotalSize,
                FreeBytes = drive.TotalFreeSpace,
                UsedBytes = usedBytes,
                Model = physical?.Model,
                MediaType = physical?.MediaType,
                TemperatureC = physical?.TemperatureC,
                HealthStatus = physical?.HealthStatus
            });
        }

        return drives;
    }

    private Dictionary<int, PhysicalDiskInfo> GetPhysicalDisks()
    {
        var result = new Dictionary<int, PhysicalDiskInfo>();

        try
        {
            var scope = new ManagementScope("\\\\.\\root\\Microsoft\\Windows\\Storage");
            scope.Connect();

            var query = new ObjectQuery("SELECT DeviceId, FriendlyName, MediaType, HealthStatus, OperationalStatus, Temperature FROM MSFT_PhysicalDisk");
            using var searcher = new ManagementObjectSearcher(scope, query);
            using var disks = searcher.Get();

            foreach (ManagementObject disk in disks)
            {
                var deviceIdRaw = disk["DeviceId"]?.ToString();
                if (string.IsNullOrWhiteSpace(deviceIdRaw) || !int.TryParse(deviceIdRaw, out var deviceId))
                {
                    continue;
                }

                var mediaType = MapMediaType(disk["MediaType"]);
                var health = MapHealthStatus(disk["HealthStatus"], disk["OperationalStatus"]);
                var tempC = MapTemperature(disk["Temperature"]);

                result[deviceId] = new PhysicalDiskInfo
                {
                    DiskNumber = deviceId,
                    Model = disk["FriendlyName"]?.ToString(),
                    MediaType = mediaType,
                    HealthStatus = health,
                    TemperatureC = tempC
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query MSFT_PhysicalDisk; falling back to limited data.");
        }

        return result;
    }

    private Dictionary<string, int> GetDriveToDiskNumberMap()
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var scope = new ManagementScope("\\\\.\\root\\Microsoft\\Windows\\Storage");
            scope.Connect();

            var diskQuery = new ObjectQuery("SELECT DeviceId, Number FROM MSFT_Disk");
            using var diskSearcher = new ManagementObjectSearcher(scope, diskQuery);
            using var disks = diskSearcher.Get();

            foreach (ManagementObject disk in disks)
            {
                var deviceId = disk["DeviceId"]?.ToString();
                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    continue;
                }

                if (!int.TryParse(disk["Number"]?.ToString(), out var number))
                {
                    continue;
                }

                var partitionsQuery = $"ASSOCIATORS OF {{MSFT_Disk.DeviceId='{deviceId}'}} WHERE AssocClass=MSFT_DiskToPartition";
                using var partitionSearcher = new ManagementObjectSearcher(scope, new ObjectQuery(partitionsQuery));
                using var partitions = partitionSearcher.Get();

                foreach (ManagementObject partition in partitions)
                {
                    var partitionId = partition["DeviceId"]?.ToString();
                    if (string.IsNullOrWhiteSpace(partitionId))
                    {
                        continue;
                    }

                    var logicalQuery = $"ASSOCIATORS OF {{MSFT_Partition.DeviceId='{partitionId}'}} WHERE AssocClass=MSFT_PartitionToLogicalDisk";
                    using var logicalSearcher = new ManagementObjectSearcher(scope, new ObjectQuery(logicalQuery));
                    using var logicalDisks = logicalSearcher.Get();

                    foreach (ManagementObject logical in logicalDisks)
                    {
                        var letter = logical["DeviceId"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(letter))
                        {
                            result[letter] = number;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to map logical drives to disks via MSFT_*; mapping may be incomplete.");
        }

        return result;
    }

    private static string? MapMediaType(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (int.TryParse(value.ToString(), out var mediaType))
        {
            return mediaType switch
            {
                3 => "HDD",
                4 => "SSD",
                5 => "SCM",
                _ => "Desconocido"
            };
        }

        return null;
    }

    private static string? MapHealthStatus(object? healthValue, object? operationalValue)
    {
        if (healthValue is not null && int.TryParse(healthValue.ToString(), out var healthStatus))
        {
            return healthStatus switch
            {
                1 => "Bueno",
                2 => "Advertencia",
                3 => "Crítico",
                _ => "Desconocido"
            };
        }

        if (operationalValue is ushort[] operationalStatuses && operationalStatuses.Length > 0)
        {
            return operationalStatuses[0] switch
            {
                2 => "Bueno",
                3 => "Advertencia",
                4 => "Crítico",
                _ => "Desconocido"
            };
        }

        return null;
    }

    private static double? MapTemperature(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (!double.TryParse(value.ToString(), out var raw))
        {
            return null;
        }

        if (raw <= 0)
        {
            return null;
        }

        if (raw > 1000)
        {
            var celsius = (raw / 10d) - 273.15d;
            return Math.Round(celsius, 0);
        }

        if (raw > 200)
        {
            return null;
        }

        return Math.Round(raw, 0);
    }

    private sealed class PhysicalDiskInfo
    {
        public int DiskNumber { get; init; }
        public string? Model { get; init; }
        public string? MediaType { get; init; }
        public string? HealthStatus { get; init; }
        public double? TemperatureC { get; init; }
    }
}
