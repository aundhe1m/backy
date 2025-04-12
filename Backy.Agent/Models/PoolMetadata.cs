using System.Text.Json.Serialization;

namespace Backy.Agent.Models;

/// <summary>
/// Contains metadata about all pools for persistence
/// </summary>
public class PoolMetadataCollection
{
    [JsonPropertyName("pools")]
    public List<PoolMetadata> Pools { get; set; } = new List<PoolMetadata>();

    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Contains metadata about a single pool for persistence
/// </summary>
public class PoolMetadata
{
    /// <summary>
    /// The mdadm device name (e.g., md0, md1)
    /// </summary>
    public string MdDeviceName { get; set; } = string.Empty;
    
    /// <summary>
    /// User-friendly name for the pool
    /// </summary>
    public string Label { get; set; } = string.Empty;
    
    /// <summary>
    /// Legacy Pool Group ID (deprecated, use PoolGroupGuid instead)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PoolGroupId { get; set; }
    
    /// <summary>
    /// Unique identifier for the pool group, stable across reboots
    /// </summary>
    public Guid PoolGroupGuid { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Serial numbers of drives in the pool
    /// </summary>
    public List<string> DriveSerials { get; set; } = new List<string>();
    
    /// <summary>
    /// Mapping of drive serial numbers to user-friendly labels
    /// </summary>
    public Dictionary<string, string> DriveLabels { get; set; } = new Dictionary<string, string>();
    
    /// <summary>
    /// Last known mount path
    /// </summary>
    public string LastMountPath { get; set; } = string.Empty;
}

/// <summary>
/// API model for listing all pools
/// </summary>
public class PoolListItem
{
    [JsonPropertyName("poolId")]
    public string PoolId { get; set; } = string.Empty;
    
    [JsonPropertyName("poolGroupGuid")]
    public Guid? PoolGroupGuid { get; set; }
    
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;
    
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
    
    [JsonPropertyName("mountPath")]
    public string MountPath { get; set; } = string.Empty;
    
    [JsonPropertyName("isMounted")]
    public bool IsMounted { get; set; }
    
    [JsonPropertyName("driveCount")]
    public int DriveCount { get; set; }
    
    [JsonPropertyName("drives")]
    public List<PoolDriveSummary> Drives { get; set; } = new List<PoolDriveSummary>();
}

/// <summary>
/// Drive information summary for pool listing
/// </summary>
public class PoolDriveSummary
{
    [JsonPropertyName("serial")]
    public string Serial { get; set; } = string.Empty;
    
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;
    
    [JsonPropertyName("isConnected")]
    public bool IsConnected { get; set; }
}

/// <summary>
/// Request model for creating a pool with PoolGroupGuid
/// </summary>
public class PoolCreationRequestExtended : PoolCreationRequest
{
    [JsonPropertyName("poolGroupGuid")]
    public Guid? PoolGroupGuid { get; set; }
}

/// <summary>
/// Request model for removing pool metadata
/// </summary>
public class PoolMetadataRemovalRequest
{
    /// <summary>
    /// The mdadm device name (e.g., md0, md1)
    /// </summary>
    [JsonPropertyName("poolId")]
    public string? PoolId { get; set; }
    
    /// <summary>
    /// Legacy Pool Group ID (deprecated, use PoolGroupGuid instead)
    /// </summary>
    public int? PoolGroupId { get; set; }
    
    /// <summary>
    /// Unique identifier for the pool group
    /// </summary>
    [JsonPropertyName("poolGroupGuid")]
    public Guid? PoolGroupGuid { get; set; }
    
    /// <summary>
    /// If true, all metadata will be removed
    /// </summary>
    [JsonPropertyName("removeAll")]
    public bool RemoveAll { get; set; }
}