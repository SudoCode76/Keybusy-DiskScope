using System.Buffers.Binary;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace Keybusy_DiskScope.Services.Implementation;

public sealed class DriveInfoService : IDriveInfoService
{
    private readonly ILogger<DriveInfoService> _logger;

    public DriveInfoService(ILogger<DriveInfoService> logger)
    {
        _logger = logger;
    }

    public async Task<DriveInfoResult> GetDrivesAsync(CancellationToken ct)
    {
        return await Task.Run(() => GetDrivesInternal(ct), ct);
    }

    private DriveInfoResult GetDrivesInternal(CancellationToken ct)
    {
        var diagnostics = new List<string>();
        try
        {
            var physicalDisks = GetPhysicalDisks(diagnostics);
            var driveToDiskNumber = GetDriveToDiskNumberMap(diagnostics);

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

                diagnostics.Add($"Unidad {drive.Name} -> Disco {diskNumber?.ToString() ?? "N/D"}; Media={physical?.MediaType ?? "N/D"}; Temp={(physical?.TemperatureC.HasValue == true ? physical.TemperatureC.Value.ToString("0") + "C" : "N/D")}; Salud={physical?.HealthStatus ?? "N/D"}");
            }

            return new DriveInfoResult
            {
                Drives = drives,
                Diagnostics = diagnostics
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Drive discovery failed; falling back to basic drive info.");
            diagnostics.Add($"Fallback: fallo en deteccion avanzada ({DescribeException(ex)})");

            var drives = new List<DriveInfoData>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady)
                {
                    continue;
                }

                var usedBytes = drive.TotalSize - drive.TotalFreeSpace;
                drives.Add(new DriveInfoData
                {
                    Name = drive.Name,
                    RootPath = drive.RootDirectory.FullName,
                    VolumeLabel = drive.VolumeLabel ?? string.Empty,
                    FileSystem = drive.DriveFormat ?? string.Empty,
                    DriveType = drive.DriveType,
                    TotalBytes = drive.TotalSize,
                    FreeBytes = drive.TotalFreeSpace,
                    UsedBytes = usedBytes
                });
            }

            return new DriveInfoResult
            {
                Drives = drives,
                Diagnostics = diagnostics
            };
        }
    }

    private Dictionary<int, PhysicalDiskInfo> GetPhysicalDisks(List<string> diagnostics)
    {
        var result = new Dictionary<int, PhysicalDiskInfo>();

        try
        {
            var scope = new ManagementScope("\\\\.\\root\\Microsoft\\Windows\\Storage");
            scope.Connect();

            var query = new ObjectQuery("SELECT * FROM MSFT_PhysicalDisk");
            using var searcher = new ManagementObjectSearcher(scope, query);
            using var disks = searcher.Get();

            foreach (ManagementObject disk in disks)
            {
                var deviceIdRaw = GetPropertyValue(disk, "DeviceId")?.ToString();
                if (string.IsNullOrWhiteSpace(deviceIdRaw) || !int.TryParse(deviceIdRaw, out var deviceId))
                {
                    continue;
                }

                var mediaType = MapMediaType(GetPropertyValue(disk, "MediaType"));
                var health = MapHealthStatus(GetPropertyValue(disk, "HealthStatus"), GetPropertyValue(disk, "OperationalStatus"));
                var tempC = MapTemperature(GetPropertyValue(disk, "Temperature"));

                result[deviceId] = new PhysicalDiskInfo
                {
                    DiskNumber = deviceId,
                    Model = GetPropertyValue(disk, "FriendlyName")?.ToString(),
                    MediaType = mediaType,
                    HealthStatus = health,
                    TemperatureC = tempC
                };

                diagnostics.Add($"MSFT_PhysicalDisk {deviceId}: Model={result[deviceId].Model ?? "N/D"}; Media={mediaType ?? "N/D"}; Salud={health ?? "N/D"}; Temp={(tempC.HasValue ? tempC.Value.ToString("0") + "C" : "N/D")}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query MSFT_PhysicalDisk; falling back to limited data.");
            diagnostics.Add($"MSFT_PhysicalDisk: fallo al consultar ({DescribeException(ex)})");
        }

        if (result.Count == 0)
        {
            var win32Disks = GetWin32Disks(diagnostics);
            foreach (var entry in win32Disks)
            {
                result[entry.Key] = entry.Value;
            }
        }

        var smartInfo = GetSmartInfo(diagnostics);
        if (smartInfo.Count > 0)
        {
            foreach (var entry in smartInfo)
            {
                if (result.TryGetValue(entry.Key, out var disk))
                {
                    if (!disk.TemperatureC.HasValue && entry.Value.TemperatureC.HasValue)
                    {
                        disk.TemperatureC = entry.Value.TemperatureC;
                    }

                    if (string.IsNullOrWhiteSpace(disk.HealthStatus) && !string.IsNullOrWhiteSpace(entry.Value.HealthStatus))
                    {
                        disk.HealthStatus = entry.Value.HealthStatus;
                    }
                }
            }
        }

        if (result.Count > 0)
        {
            foreach (var entry in result)
            {
                if (entry.Value.TemperatureC.HasValue && !string.IsNullOrWhiteSpace(entry.Value.HealthStatus))
                {
                    continue;
                }

                if (TryGetNvmeSmartInfo(entry.Key, out var temp, out var health, diagnostics))
                {
                    if (!entry.Value.TemperatureC.HasValue && temp.HasValue)
                    {
                        entry.Value.TemperatureC = temp;
                    }

                    if (string.IsNullOrWhiteSpace(entry.Value.HealthStatus) && !string.IsNullOrWhiteSpace(health))
                    {
                        entry.Value.HealthStatus = health;
                    }
                }
            }
        }

        return result;
    }

    private Dictionary<string, int> GetDriveToDiskNumberMap(List<string> diagnostics)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var scope = new ManagementScope("\\\\.\\root\\Microsoft\\Windows\\Storage");
            scope.Connect();

            var diskQuery = new ObjectQuery("SELECT * FROM MSFT_Disk");
            using var diskSearcher = new ManagementObjectSearcher(scope, diskQuery);
            using var disks = diskSearcher.Get();

            foreach (ManagementObject disk in disks)
            {
                var deviceId = GetPropertyValue(disk, "DeviceId")?.ToString();
                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    continue;
                }

                if (!int.TryParse(GetPropertyValue(disk, "Number")?.ToString(), out var number))
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
                            diagnostics.Add($"Mapeo {letter} -> Disco {number}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to map logical drives to disks via MSFT_*; mapping may be incomplete.");
            diagnostics.Add($"Mapeo MSFT_Disk/Partition: fallo al consultar ({DescribeException(ex)})");
        }

        if (result.Count == 0)
        {
            var fallback = GetDriveToDiskNumberMapWin32(diagnostics);
            foreach (var entry in fallback)
            {
                result[entry.Key] = entry.Value;
            }
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

        if (TryGetFirstStatusCode(operationalValue, out var statusCode))
        {
            return statusCode switch
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

    private Dictionary<int, SmartInfo> GetSmartInfo(List<string> diagnostics)
    {
        var result = new Dictionary<int, SmartInfo>();

        try
        {
            var scope = new ManagementScope("\\\\.\\root\\wmi");
            scope.Connect();

            var statusQuery = new ObjectQuery("SELECT * FROM MSStorageDriver_FailurePredictStatus");
            using (var statusSearcher = new ManagementObjectSearcher(scope, statusQuery))
            using (var statuses = statusSearcher.Get())
            {
                foreach (ManagementObject status in statuses)
                {
                    try
                    {
                        var instance = GetPropertyValue(status, "InstanceName")?.ToString();
                        if (!TryGetDiskNumberFromInstance(instance, out var diskNumber))
                        {
                            continue;
                        }

                        var predictFailure = SafeBool(GetPropertyValue(status, "PredictFailure"));
                        var health = predictFailure ? "Crítico" : "Bueno";

                        if (!result.TryGetValue(diskNumber, out var info))
                        {
                            info = new SmartInfo();
                            result[diskNumber] = info;
                        }

                        info.HealthStatus = health;
                        diagnostics.Add($"SMART Salud Disco {diskNumber}: {health}");
                    }
                    catch (Exception ex)
                    {
                        diagnostics.Add($"SMART Salud: fallo por item ({DescribeException(ex)})");
                    }
                }
            }

            var tempQuery = new ObjectQuery("SELECT * FROM MSStorageDriver_Temperature");
            using (var tempSearcher = new ManagementObjectSearcher(scope, tempQuery))
            using (var temps = tempSearcher.Get())
            {
                foreach (ManagementObject temp in temps)
                {
                    try
                    {
                        var instance = GetPropertyValue(temp, "InstanceName")?.ToString();
                        if (!TryGetDiskNumberFromInstance(instance, out var diskNumber))
                        {
                            continue;
                        }

                        var temperature = MapTemperature(GetPropertyValue(temp, "CurrentTemperature"));
                        if (!result.TryGetValue(diskNumber, out var info))
                        {
                            info = new SmartInfo();
                            result[diskNumber] = info;
                        }

                        info.TemperatureC = temperature;
                        diagnostics.Add($"SMART Temp Disco {diskNumber}: {(temperature.HasValue ? temperature.Value.ToString("0") + "C" : "N/D")}");
                    }
                    catch (Exception ex)
                    {
                        diagnostics.Add($"SMART Temp: fallo por item ({DescribeException(ex)})");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query SMART temperature/health via root\\wmi.");
            diagnostics.Add($"SMART root\\wmi: fallo al consultar ({DescribeException(ex)})");
        }

        return result;
    }

    private Dictionary<int, PhysicalDiskInfo> GetWin32Disks(List<string> diagnostics)
    {
        var result = new Dictionary<int, PhysicalDiskInfo>();

        try
        {
            var scope = new ManagementScope("\\\\.\\root\\cimv2");
            scope.Connect();

            var query = new ObjectQuery("SELECT Index, Model, MediaType, InterfaceType FROM Win32_DiskDrive");
            using var searcher = new ManagementObjectSearcher(scope, query);
            using var disks = searcher.Get();

            foreach (ManagementObject disk in disks)
            {
                if (!int.TryParse(disk["Index"]?.ToString(), out var index))
                {
                    continue;
                }

                var model = disk["Model"]?.ToString();
                var mediaTypeRaw = disk["MediaType"]?.ToString();
                var interfaceType = disk["InterfaceType"]?.ToString();
                var mediaType = MapMediaTypeFromWin32(mediaTypeRaw, model, interfaceType);

                result[index] = new PhysicalDiskInfo
                {
                    DiskNumber = index,
                    Model = model,
                    MediaType = mediaType,
                    InterfaceType = interfaceType
                };

                diagnostics.Add($"Win32_DiskDrive {index}: Model={model ?? "N/D"}; Media={mediaType ?? "N/D"}; Interface={interfaceType ?? "N/D"}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query Win32_DiskDrive.");
            diagnostics.Add($"Win32_DiskDrive: fallo al consultar ({DescribeException(ex)})");
        }

        return result;
    }

    private Dictionary<string, int> GetDriveToDiskNumberMapWin32(List<string> diagnostics)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var scope = new ManagementScope("\\\\.\\root\\cimv2");
            scope.Connect();

            var diskIndexByDeviceId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var diskQuery = new ObjectQuery("SELECT DeviceID, Index FROM Win32_DiskDrive");
            using (var diskSearcher = new ManagementObjectSearcher(scope, diskQuery))
            using (var disks = diskSearcher.Get())
            {
                foreach (ManagementObject disk in disks)
                {
                    var deviceId = disk["DeviceID"]?.ToString();
                    if (string.IsNullOrWhiteSpace(deviceId))
                    {
                        continue;
                    }

                    if (!int.TryParse(disk["Index"]?.ToString(), out var index))
                    {
                        continue;
                    }

                    diskIndexByDeviceId[deviceId] = index;
                }
            }

            var partitionToDiskIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var diskToPartitionQuery = new ObjectQuery("SELECT Antecedent, Dependent FROM Win32_DiskDriveToDiskPartition");
            using (var assocSearcher = new ManagementObjectSearcher(scope, diskToPartitionQuery))
            using (var assocs = assocSearcher.Get())
            {
                foreach (ManagementObject assoc in assocs)
                {
                    var antecedent = assoc["Antecedent"]?.ToString();
                    var dependent = assoc["Dependent"]?.ToString();
                    if (!TryGetWmiKeyValue(antecedent, "DeviceID", out var diskDeviceId)
                        || !TryGetWmiKeyValue(dependent, "DeviceID", out var partitionId))
                    {
                        continue;
                    }

                    if (diskIndexByDeviceId.TryGetValue(diskDeviceId, out var index))
                    {
                        partitionToDiskIndex[partitionId] = index;
                    }
                }
            }

            var partitionToLogicalQuery = new ObjectQuery("SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition");
            using (var assocSearcher = new ManagementObjectSearcher(scope, partitionToLogicalQuery))
            using (var assocs = assocSearcher.Get())
            {
                foreach (ManagementObject assoc in assocs)
                {
                    var antecedent = assoc["Antecedent"]?.ToString();
                    var dependent = assoc["Dependent"]?.ToString();
                    if (!TryGetWmiKeyValue(antecedent, "DeviceID", out var partitionId)
                        || !TryGetWmiKeyValue(dependent, "DeviceID", out var logicalLetter))
                    {
                        continue;
                    }

                    if (partitionToDiskIndex.TryGetValue(partitionId, out var index))
                    {
                        result[logicalLetter] = index;
                        diagnostics.Add($"Mapeo Win32 {logicalLetter} -> Disco {index}");
                    }
                }
            }

            if (result.Count == 0)
            {
                diagnostics.Add("Mapeo Win32: sin asociaciones encontradas");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to map logical drives via Win32_*.");
            diagnostics.Add($"Mapeo Win32: fallo al consultar ({DescribeException(ex)})");
        }

        return result;
    }

    private static string MapMediaTypeFromWin32(string? mediaType, string? model, string? interfaceType)
    {
        var modelText = model?.ToUpperInvariant() ?? string.Empty;
        var mediaText = mediaType?.ToUpperInvariant() ?? string.Empty;
        var interfaceText = interfaceType?.ToUpperInvariant() ?? string.Empty;

        if (modelText.Contains("NVME") || modelText.Contains("SSD") || mediaText.Contains("SSD"))
        {
            return "SSD";
        }

        if (mediaText.Contains("HDD") || mediaText.Contains("HARD"))
        {
            return "HDD";
        }

        if (interfaceText.Contains("USB"))
        {
            return "USB";
        }

        return "Desconocido";
    }

    private static string EscapeWmiPath(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static bool TryGetNvmeSmartInfo(int diskNumber, out double? temperatureC, out string? healthStatus, List<string> diagnostics)
    {
        temperatureC = null;
        healthStatus = null;

        var devicePath = $"\\\\.\\PhysicalDrive{diskNumber}";
        using var handle = CreateFile(devicePath, FileAccessGenericReadWrite, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 5)
            {
                diagnostics.Add($"NVMe IOCTL Disco {diskNumber}: acceso denegado. Ejecuta la app como administrador.");
            }
            else
            {
                diagnostics.Add($"NVMe IOCTL Disco {diskNumber}: CreateFile fallo ({error})");
            }
            return false;
        }

        var commandLength = 64;
        var dataLength = 512;
        var headerSize = Marshal.SizeOf<StorageProtocolCommand>();
        var bufferSize = headerSize + commandLength + dataLength;
        var buffer = new byte[bufferSize];

        var command = new StorageProtocolCommand
        {
            Version = (uint)headerSize,
            Length = (uint)headerSize,
            ProtocolType = StorageProtocolTypeNvme,
            Flags = ProtocolCommandFlagDataIn,
            CommandLength = (uint)commandLength,
            DataFromDeviceTransferLength = (uint)dataLength,
            TimeOutValue = 10,
            DataFromDeviceBufferOffset = (uint)(headerSize + commandLength)
        };

        WriteStructToBuffer(command, buffer, 0);
        WriteNvmeGetLogPageCommand(buffer, headerSize, dataLength);

        var success = DeviceIoControl(handle, IoctlStorageProtocolCommand, buffer, buffer.Length, buffer, buffer.Length, out var bytesReturned, IntPtr.Zero);
        if (!success)
        {
            diagnostics.Add($"NVMe IOCTL Disco {diskNumber}: fallo ({Marshal.GetLastWin32Error()})");
            return false;
        }

        if (bytesReturned < headerSize + commandLength + 8)
        {
            diagnostics.Add($"NVMe IOCTL Disco {diskNumber}: respuesta insuficiente");
            return false;
        }

        var dataOffset = (int)command.DataFromDeviceBufferOffset;
        if (dataOffset + dataLength > buffer.Length)
        {
            diagnostics.Add($"NVMe IOCTL Disco {diskNumber}: buffer fuera de rango");
            return false;
        }

        var criticalWarning = buffer[dataOffset];
        var tempRaw = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(dataOffset + 1, 2));
        if (tempRaw > 0)
        {
            var celsius = tempRaw - 273.15d;
            if (celsius > -50 && celsius < 200)
            {
                temperatureC = Math.Round(celsius, 0);
            }
        }

        healthStatus = MapNvmeHealthStatus(criticalWarning);
        diagnostics.Add($"NVMe IOCTL Disco {diskNumber}: Temp={(temperatureC.HasValue ? temperatureC.Value.ToString("0") + "C" : "N/D")}; Salud={healthStatus ?? "N/D"}; CW=0x{criticalWarning:X2}");
        return true;
    }

    private static void WriteNvmeGetLogPageCommand(byte[] buffer, int offset, int dataLength)
    {
        Span<byte> cmd = buffer.AsSpan(offset, 64);
        cmd.Clear();
        cmd[0] = 0x02; // Get Log Page

        BinaryPrimitives.WriteUInt32LittleEndian(cmd.Slice(4, 4), 0xFFFFFFFF); // NSID

        var numd = (uint)(dataLength / 4 - 1);
        var cdw10 = 0x02u | (numd << 16);
        BinaryPrimitives.WriteUInt32LittleEndian(cmd.Slice(40, 4), cdw10);
    }

    private static string MapNvmeHealthStatus(byte criticalWarning)
    {
        if (criticalWarning == 0)
        {
            return "Bueno";
        }

        if ((criticalWarning & 0x1) != 0 || (criticalWarning & 0x4) != 0 || (criticalWarning & 0x8) != 0)
        {
            return "Crítico";
        }

        return "Advertencia";
    }

    private static void WriteStructToBuffer<T>(T value, byte[] buffer, int offset) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, ptr, false);
            Marshal.Copy(ptr, buffer, offset, size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle deviceHandle,
        uint ioControlCode,
        byte[] inBuffer,
        int inBufferSize,
        byte[] outBuffer,
        int outBufferSize,
        out int bytesReturned,
        IntPtr overlapped);

    private const uint IoctlStorageProtocolCommand = 0x002DD3C0;
    private const uint FileAccessGenericReadWrite = 0xC0000000;
    private const uint FileShareReadWrite = 0x00000003;
    private const uint OpenExisting = 3;
    private const uint StorageProtocolTypeNvme = 3;
    private const uint ProtocolCommandFlagDataIn = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct StorageProtocolCommand
    {
        public uint Version;
        public uint Length;
        public uint ProtocolType;
        public uint Flags;
        public uint ReturnStatus;
        public uint ErrorCode;
        public uint CommandLength;
        public uint ErrorInfoLength;
        public uint DataToDeviceTransferLength;
        public uint DataFromDeviceTransferLength;
        public uint TimeOutValue;
        public uint ErrorInfoOffset;
        public uint DataToDeviceBufferOffset;
        public uint DataFromDeviceBufferOffset;
        public uint CommandSpecific;
    }

    private static object? GetPropertyValue(ManagementBaseObject source, string name)
    {
        try
        {
            return source.Properties[name]?.Value;
        }
        catch (InvalidCastException)
        {
            return null;
        }
        catch (ManagementException)
        {
            return null;
        }
    }

    private static bool SafeBool(object? value)
    {
        if (value is null)
        {
            return false;
        }

        try
        {
            return Convert.ToBoolean(value);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }
    }

    private static bool TryGetFirstStatusCode(object? value, out int statusCode)
    {
        statusCode = 0;
        if (value is null)
        {
            return false;
        }

        if (value is Array array && array.Length > 0)
        {
            try
            {
                statusCode = Convert.ToInt32(array.GetValue(0));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        try
        {
            statusCode = Convert.ToInt32(value);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryGetWmiKeyValue(string? path, string key, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var match = Regex.Match(path, key + "=\"(.*?)\"", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        value = match.Groups[1].Value.Replace("\\\\", "\\");
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string DescribeException(Exception ex)
    {
        return $"{ex.GetType().Name}: {ex.Message} (0x{ex.HResult:X8})";
    }

    private static bool TryGetDiskNumberFromInstance(string? instanceName, out int diskNumber)
    {
        diskNumber = 0;
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            return false;
        }

        var match = Regex.Match(instanceName, @"PhysicalDrive(\d+)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        return int.TryParse(match.Groups[1].Value, out diskNumber);
    }

    private sealed class PhysicalDiskInfo
    {
        public int DiskNumber { get; init; }
        public string? Model { get; init; }
        public string? MediaType { get; init; }
        public string? InterfaceType { get; init; }
        public string? HealthStatus { get; set; }
        public double? TemperatureC { get; set; }
    }

    private sealed class SmartInfo
    {
        public string? HealthStatus { get; set; }
        public double? TemperatureC { get; set; }
    }
}
