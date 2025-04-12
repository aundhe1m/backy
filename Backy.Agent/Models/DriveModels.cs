using System.Text.Json.Serialization;

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
    
    [JsonPropertyName("poolId")]
    public string? PoolId { get; set; }
    
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
    
    [JsonPropertyName("poolId")]
    public string? PoolId { get; set; }
    
    [JsonPropertyName("mountPath")]
    public string? MountPath { get; set; }
    
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    
    [JsonPropertyName("commandOutputs")]
    public List<string> CommandOutputs { get; set; } = new();
}

public class PoolDetailResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
    
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
    
    [JsonPropertyName("drives")]
    public List<PoolDriveStatus> Drives { get; set; } = new();
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