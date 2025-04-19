using System.Text.Json.Serialization;

namespace Backy.Agent.Models;

/// <summary>
/// Contains metadata about a RAID pool, persisted between reboots
/// </summary>
public class PoolMetadata
{
    /// <summary>
    /// The MD device name (e.g., 'md0', 'md127')
    /// </summary>
    [JsonPropertyName("mdDeviceName")]
    public string MdDeviceName { get; set; } = string.Empty;
    
    /// <summary>
    /// User-friendly label for the pool
    /// </summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;
    
    /// <summary>
    /// Stable GUID that identifies this pool across reboots
    /// </summary>
    [JsonPropertyName("poolGroupGuid")]
    public Guid PoolGroupGuid { get; set; } = Guid.Empty;
    
    /// <summary>
    /// Serial numbers of drives in the pool
    /// </summary>
    [JsonPropertyName("driveSerials")]
    public List<string> DriveSerials { get; set; } = new();
    
    /// <summary>
    /// Mapping of drive serial numbers to user-friendly labels
    /// </summary>
    [JsonPropertyName("driveLabels")]
    public Dictionary<string, string> DriveLabels { get; set; } = new();
    
    /// <summary>
    /// Last path where this pool was mounted
    /// </summary>
    [JsonPropertyName("lastMountPath")]
    public string LastMountPath { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether the pool is currently mounted
    /// </summary>
    [JsonPropertyName("isMounted")]
    public bool IsMounted { get; set; } = false;
    
    /// <summary>
    /// When this pool was created
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Collection of pool metadata stored in the metadata file
/// </summary>
public class PoolMetadataCollection
{
    /// <summary>
    /// List of all pools with their metadata
    /// </summary>
    [JsonPropertyName("pools")]
    public List<PoolMetadata> Pools { get; set; } = new();
    
    /// <summary>
    /// When the metadata was last updated
    /// </summary>
    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Summary information about a pool for listing
/// </summary>
public class PoolListItem
{
    [JsonPropertyName("mdDeviceName")]
    public string MdDeviceName { get; set; } = string.Empty;
    
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;
    
    [JsonPropertyName("poolGroupGuid")]
    public Guid PoolGroupGuid { get; set; }
    
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
    
    [JsonPropertyName("isMounted")]
    public bool IsMounted { get; set; }
    
    [JsonPropertyName("mountPath")]
    public string MountPath { get; set; } = string.Empty;
    
    [JsonPropertyName("resyncPercentage")]
    public double? ResyncPercentage { get; set; }
    
    [JsonPropertyName("resyncTimeEstimate")]
    public double? ResyncTimeEstimate { get; set; }
    
    [JsonPropertyName("drives")]
    public List<PoolDriveSummary> Drives { get; set; } = new();
}

/// <summary>
/// Summary information about a drive in a pool
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
/// Extended pool creation request that can include a specific GUID
/// </summary>
public class PoolCreationRequestExtended : PoolCreationRequest
{
    /// <summary>
    /// Optional GUID to assign to the new pool
    /// </summary>
    [JsonPropertyName("poolGroupGuid")]
    public Guid? PoolGroupGuid { get; set; }
}

/// <summary>
/// Request to remove pool metadata
/// </summary>
public class PoolMetadataRemovalRequest
{
    /// <summary>
    /// The pool group GUID to remove metadata for
    /// </summary>
    [JsonPropertyName("poolGroupGuid")]
    public Guid? PoolGroupGuid { get; set; }
    
    /// <summary>
    /// Whether to remove all pool metadata
    /// </summary>
    [JsonPropertyName("removeAll")]
    public bool RemoveAll { get; set; }
}