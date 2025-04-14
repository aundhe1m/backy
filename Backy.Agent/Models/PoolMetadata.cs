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
    [JsonPropertyName("mdDeviceName")]
    public string MdDeviceName { get; set; } = string.Empty;
    
    /// <summary>
    /// User-friendly name for the pool
    /// </summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;
    
    /// <summary>
    /// Legacy Pool Group ID (deprecated, use PoolGroupGuid instead)
    /// </summary>
    [JsonPropertyName("poolGroupId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PoolGroupId { get; set; }
    
    /// <summary>
    /// Unique identifier for the pool group, stable across reboots
    /// </summary>
    [JsonPropertyName("poolGroupGuid")]
    public Guid PoolGroupGuid { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Serial numbers of drives in the pool
    /// </summary>
    [JsonPropertyName("driveSerials")]
    public List<string> DriveSerials { get; set; } = new List<string>();
    
    /// <summary>
    /// Mapping of drive serial numbers to user-friendly labels
    /// </summary>
    [JsonPropertyName("driveLabels")]
    public Dictionary<string, string> DriveLabels { get; set; } = new Dictionary<string, string>();
    
    /// <summary>
    /// Last known mount path
    /// </summary>
    [JsonPropertyName("lastMountPath")]
    public string LastMountPath { get; set; } = string.Empty;
    
    /// <summary>
    /// Creation date of the pool
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// API model for listing all pools
/// </summary>
public class PoolListItem
{
    [JsonPropertyName("mdDeviceName")]
    public string MdDeviceName { get; set; } = string.Empty;
    
    [JsonPropertyName("poolGroupGuid")]
    public Guid PoolGroupGuid { get; set; }
    
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;
    
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
    
    [JsonPropertyName("mountPath")]
    public string MountPath { get; set; } = string.Empty;
    
    [JsonPropertyName("isMounted")]
    public bool IsMounted { get; set; }
    
    [JsonPropertyName("resyncPercentage")]
    public double? ResyncPercentage { get; set; }
    
    [JsonPropertyName("resyncTimeEstimate")]
    public double? ResyncTimeEstimate { get; set; }
    
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