using System.Text.Json.Serialization;
using System.IO;

namespace Backy.Agent.Models;

public class BlockDevice
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("size")]
    public long? Size { get; set; }
    
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    
    [JsonPropertyName("mountpoint")]
    public string? Mountpoint { get; set; }
    
    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }
    
    [JsonPropertyName("serial")]
    public string? Serial { get; set; }
    
    [JsonPropertyName("vendor")]
    public string? Vendor { get; set; }
    
    [JsonPropertyName("model")]
    public string? Model { get; set; }
    
    [JsonPropertyName("fstype")]
    public string? Fstype { get; set; }
    
    [JsonPropertyName("path")]
    public string? Path { get; set; }
    
    [JsonPropertyName("id-link")]
    public string? IdLink { get; set; }
    
    [JsonPropertyName("children")]
    public List<BlockDevice>? Children { get; set; }
}

public class LsblkOutput
{
    [JsonPropertyName("blockdevices")]
    public List<BlockDevice>? Blockdevices { get; set; }
}

public class DriveStatus
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "available";
    
    [JsonPropertyName("inPool")]
    public bool InPool { get; set; }
    
    [JsonPropertyName("poolGroupGuid")]
    public Guid? PoolGroupGuid { get; set; }
    
    [JsonPropertyName("mdDeviceName")]
    public string? MdDeviceName { get; set; }
    
    [JsonPropertyName("mountPoint")]
    public string? MountPoint { get; set; }
    
    [JsonPropertyName("processes")]
    public List<ProcessInfo> Processes { get; set; } = new();
}

public class ProcessInfo
{
    [JsonPropertyName("pid")]
    public int PID { get; set; }
    
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;
    
    [JsonPropertyName("user")]
    public string User { get; set; } = string.Empty;
    
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}

public class PoolCreationRequest
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;
    
    [JsonPropertyName("driveSerials")]
    public List<string> DriveSerials { get; set; } = new();
    
    [JsonPropertyName("driveLabels")]
    public Dictionary<string, string> DriveLabels { get; set; } = new();
    
    [JsonPropertyName("mountPath")]
    public string MountPath { get; set; } = string.Empty;
}

public class PoolCreationResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("poolGroupGuid")]
    public Guid PoolGroupGuid { get; set; }
    
    [JsonPropertyName("mdDeviceName")]
    public string? MdDeviceName { get; set; }
    
    [JsonPropertyName("mountPath")]
    public string? MountPath { get; set; }
    
    [JsonPropertyName("state")]
    public string? State { get; set; }
    
    [JsonPropertyName("commandOutputs")]
    public List<string> CommandOutputs { get; set; } = new();
}

public class PoolDetailResponse
{
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;
    
    [JsonPropertyName("poolStatus")]
    public string? PoolStatus { get; set; }
    
    [JsonPropertyName("size")]
    public long Size { get; set; }
    
    [JsonPropertyName("used")]
    public long Used { get; set; }
    
    [JsonPropertyName("available")]
    public long Available { get; set; }
    
    [JsonPropertyName("usePercent")]
    public string UsePercent { get; set; } = "0%";
    
    [JsonPropertyName("mountPath")]
    public string MountPath { get; set; } = string.Empty;
    
    [JsonPropertyName("resyncPercentage")]
    public double? ResyncPercentage { get; set; }
    
    [JsonPropertyName("resyncTimeEstimate")]
    public double? ResyncTimeEstimate { get; set; }
    
    [JsonPropertyName("drives")]
    public List<PoolDriveStatus> Drives { get; set; } = new();
    
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

public class PoolDriveStatus
{
    [JsonPropertyName("serial")]
    public string Serial { get; set; } = string.Empty;
    
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;
    
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

public class MountRequest
{
    [JsonPropertyName("mountPath")]
    public string MountPath { get; set; } = string.Empty;
}

public class CommandResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("commandOutputs")]
    public List<string> CommandOutputs { get; set; } = new();
}

public class ProcessesRequest
{
    [JsonPropertyName("pids")]
    public List<int> Pids { get; set; } = new();
    
    [JsonPropertyName("poolGroupGuid")]
    public Guid? PoolGroupGuid { get; set; }
}

public class ProcessesResponse
{
    [JsonPropertyName("processes")]
    public List<ProcessInfo> Processes { get; set; } = new();
}

public class ErrorResponse
{
    [JsonPropertyName("error")]
    public ErrorDetail Error { get; set; } = new();
}

public class ErrorDetail
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;
    
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    
    [JsonPropertyName("details")]
    public string? Details { get; set; }
}

// New model for pool operation status tracking
public class PoolOperationStatus
{
    [JsonPropertyName("poolGroupGuid")]
    public Guid PoolGroupGuid { get; set; }
    
    [JsonPropertyName("state")]
    public string State { get; set; } = "creating";
    
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
    
    [JsonPropertyName("commandOutputs")]
    public List<string> CommandOutputs { get; set; } = new();
    
    [JsonPropertyName("mdDeviceName")]
    public string? MdDeviceName { get; set; }
    
    [JsonPropertyName("mountPath")]
    public string? MountPath { get; set; }
    
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }
}

// New response model for pool command output
public class PoolCommandOutputResponse
{
    [JsonPropertyName("outputs")]
    public List<string> Outputs { get; set; } = new();
}

// New models for drive monitoring and mapping

/// <summary>
/// Event argument class for drive change events
/// </summary>
public class DriveChangeEventArgs : EventArgs
{
    /// <summary>
    /// The type of file system change that occurred
    /// </summary>
    public WatcherChangeTypes ChangeType { get; set; }
    
    /// <summary>
    /// The path of the device that changed (if available)
    /// </summary>
    public string? Path { get; set; }
    
    /// <summary>
    /// The full path to the device ID
    /// </summary>
    public string? FullPath { get; set; }
}

/// <summary>
/// Class for mapping between different drive identifiers
/// </summary>
public class DriveMapping
{
    /// <summary>
    /// Maps from disk ID name to drive info
    /// </summary>
    public Dictionary<string, DriveInfo> DiskIdNameToDrive { get; } = new();
    
    /// <summary>
    /// Maps from device path to disk ID name
    /// </summary>
    public Dictionary<string, string> DevicePathToDiskIdName { get; } = new();
    
    /// <summary>
    /// Maps from device name to disk ID name
    /// </summary>
    public Dictionary<string, string> DeviceNameToDiskIdName { get; } = new();
    
    /// <summary>
    /// Maps from serial number to disk ID name
    /// </summary>
    public Dictionary<string, string> SerialToDiskIdName { get; } = new();
    
    /// <summary>
    /// Last time the mapping was updated
    /// </summary>
    public DateTime LastUpdated { get; set; }
    
    /// <summary>
    /// Clears all mappings
    /// </summary>
    public void Clear()
    {
        DiskIdNameToDrive.Clear();
        DevicePathToDiskIdName.Clear();
        DeviceNameToDiskIdName.Clear();
        SerialToDiskIdName.Clear();
    }
}

/// <summary>
/// Basic drive information model
/// </summary>
public class DriveInfo
{
    /// <summary>
    /// The disk ID name (e.g., 'scsi-SATA_WDC_WD80EFAX-68K_1234567')
    /// </summary>
    [JsonPropertyName("diskIdName")]
    public string DiskIdName { get; set; } = string.Empty;
    
    /// <summary>
    /// The disk ID path (e.g., '/dev/disk/by-id/scsi-SATA_WDC_WD80EFAX-68K_1234567')
    /// </summary>
    [JsonPropertyName("diskIdPath")]
    public string DiskIdPath { get; set; } = string.Empty;
    
    /// <summary>
    /// The device name (e.g., 'sda')
    /// </summary>
    [JsonPropertyName("deviceName")]
    public string DeviceName { get; set; } = string.Empty;
    
    /// <summary>
    /// The device path (e.g., '/dev/sda')
    /// </summary>
    [JsonPropertyName("devicePath")]
    public string DevicePath { get; set; } = string.Empty;
    
    /// <summary>
    /// The drive serial number
    /// </summary>
    [JsonPropertyName("serial")]
    public string Serial { get; set; } = string.Empty;
    
    /// <summary>
    /// The drive size in bytes
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }
    
    /// <summary>
    /// Whether the drive is busy/in use
    /// </summary>
    [JsonPropertyName("isBusy")]
    public bool IsBusy { get; set; }
    
    /// <summary>
    /// Whether the drive is mounted
    /// </summary>
    [JsonPropertyName("isMounted")]
    public bool IsMounted { get; set; }
    
    /// <summary>
    /// Drive partitions
    /// </summary>
    [JsonPropertyName("partitions")]
    public List<PartitionInfo> Partitions { get; set; } = new();
}

/// <summary>
/// Partition information
/// </summary>
public class PartitionInfo
{
    /// <summary>
    /// The partition name (e.g., 'sda1')
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// The partition path (e.g., '/dev/sda1')
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
    
    /// <summary>
    /// The partition filesystem type
    /// </summary>
    [JsonPropertyName("fsType")]
    public string FsType { get; set; } = string.Empty;
    
    /// <summary>
    /// The partition UUID
    /// </summary>
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;
    
    /// <summary>
    /// The partition size in bytes
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }
    
    /// <summary>
    /// Whether the partition is mounted
    /// </summary>
    [JsonPropertyName("isMounted")]
    public bool IsMounted { get; set; }
    
    /// <summary>
    /// The mount point (if mounted)
    /// </summary>
    [JsonPropertyName("mountPoint")]
    public string MountPoint { get; set; } = string.Empty;
}

/// <summary>
/// Detailed drive information including SMART data
/// </summary>
public class DriveDetailInfo : DriveInfo
{
    /// <summary>
    /// The drive model family
    /// </summary>
    [JsonPropertyName("modelFamily")]
    public string ModelFamily { get; set; } = string.Empty;
    
    /// <summary>
    /// The drive model name
    /// </summary>
    [JsonPropertyName("modelName")]
    public string ModelName { get; set; } = string.Empty;
    
    /// <summary>
    /// The drive firmware version
    /// </summary>
    [JsonPropertyName("firmwareVersion")]
    public string FirmwareVersion { get; set; } = string.Empty;
    
    /// <summary>
    /// The drive capacity in bytes
    /// </summary>
    [JsonPropertyName("capacity")]
    public long Capacity { get; set; }
    
    /// <summary>
    /// Whether the SMART status is OK
    /// </summary>
    [JsonPropertyName("smartStatus")]
    public bool SmartStatus { get; set; }
    
    /// <summary>
    /// The drive power-on hours
    /// </summary>
    [JsonPropertyName("powerOnHours")]
    public long PowerOnHours { get; set; }
    
    /// <summary>
    /// The drive temperature in Celsius
    /// </summary>
    [JsonPropertyName("temperature")]
    public int Temperature { get; set; }
    
    /// <summary>
    /// Whether the drive is part of a RAID array
    /// </summary>
    [JsonPropertyName("isPartOfRaidArray")]
    public bool IsPartOfRaidArray { get; set; }
    
    /// <summary>
    /// List of RAID array names this drive is part of
    /// </summary>
    [JsonPropertyName("raidArrayNames")]
    public List<string> RaidArrayNames { get; set; } = new();
    
    /// <summary>
    /// Factory method to create a detailed info from basic info
    /// </summary>
    public static DriveDetailInfo FromBasicInfo(DriveInfo basicInfo)
    {
        return new DriveDetailInfo
        {
            DiskIdName = basicInfo.DiskIdName,
            DiskIdPath = basicInfo.DiskIdPath,
            DeviceName = basicInfo.DeviceName,
            DevicePath = basicInfo.DevicePath,
            Serial = basicInfo.Serial,
            Size = basicInfo.Size,
            IsBusy = basicInfo.IsBusy,
            IsMounted = basicInfo.IsMounted,
            Partitions = basicInfo.Partitions
        };
    }
}

/// <summary>
/// SMART data output models for parsing smartctl JSON output
/// </summary>
public class SmartctlOutput
{
    [JsonPropertyName("model_family")]
    public string? ModelFamily { get; set; }
    
    [JsonPropertyName("model_name")]
    public string? ModelName { get; set; }
    
    [JsonPropertyName("serial_number")]
    public string? SerialNumber { get; set; }
    
    [JsonPropertyName("firmware_version")]
    public string? FirmwareVersion { get; set; }
    
    [JsonPropertyName("user_capacity")]
    public SmartCapacity? UserCapacity { get; set; }
    
    [JsonPropertyName("temperature")]
    public SmartTemperature? Temperature { get; set; }
    
    [JsonPropertyName("smart_status")]
    public SmartStatus? SmartStatus { get; set; }
    
    [JsonPropertyName("ata_smart_attributes")]
    public SmartAttributes? Attributes { get; set; }
}

public class SmartCapacity
{
    [JsonPropertyName("bytes")]
    public long? Bytes { get; set; }
}

public class SmartTemperature
{
    [JsonPropertyName("current")]
    public int Current { get; set; }
}

public class SmartStatus
{
    [JsonPropertyName("passed")]
    public bool? Passed { get; set; }
}

public class SmartAttributes
{
    [JsonPropertyName("table")]
    public List<SmartAttribute>? Attributes { get; set; }
}

public class SmartAttribute
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("value")]
    public SmartAttributeValue? Value { get; set; }
}

public class SmartAttributeValue
{
    [JsonPropertyName("value")]
    public int Value { get; set; }
}