using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Backy.Agent.Models;
using Backy.Agent.Services.Core;
using Backy.Agent.Services.Storage.Drives;
using Backy.Agent.Services.Storage.Metadata;

namespace Backy.Agent.Services.Storage.Pools
{
    /// <summary>
    /// Interface for retrieving information about storage pools.
    /// </summary>
    public interface IPoolInfoService
    {
        /// <summary>
        /// Gets size information for a specific pool
        /// </summary>
        /// <param name="poolGroupGuid">The pool group GUID</param>
        /// <returns>Size information for the pool</returns>
        Task<Result<PoolSizeInfo>> GetPoolSizeInfoAsync(Guid poolGroupGuid);
        
        /// <summary>
        /// Gets size information for all pools
        /// </summary>
        /// <returns>Collection of size information for all pools</returns>
        Task<Result<IEnumerable<PoolSizeInfo>>> GetAllPoolSizesAsync();
        
        /// <summary>
        /// Gets health information for a specific pool
        /// </summary>
        /// <param name="poolGroupGuid">The pool group GUID</param>
        /// <returns>Health information for the pool</returns>
        Task<Result<PoolHealthInfo>> GetPoolHealthInfoAsync(Guid poolGroupGuid);
        
        /// <summary>
        /// Gets detailed information for a specific pool
        /// </summary>
        /// <param name="poolGroupGuid">The pool group GUID</param>
        /// <returns>Detailed information for the pool</returns>
        Task<Result<PoolDetailInfo>> GetPoolDetailInfoAsync(Guid poolGroupGuid);

        /// <summary>
        /// Checks if a pool exists
        /// </summary>
        /// <param name="poolGroupGuid">The pool group GUID</param>
        /// <returns>True if the pool exists, otherwise false</returns>
        Task<Result<bool>> PoolExistsAsync(Guid poolGroupGuid);

        /// <summary>
        /// Gets information for all pools
        /// </summary>
        /// <returns>Collection of information for all pools</returns>
        Task<Result<IEnumerable<PoolInfo>>> GetAllPoolsAsync();

        /// <summary>
        /// Gets information for a specific pool
        /// </summary>
        /// <param name="poolGroupGuid">The pool group GUID</param>
        /// <returns>Information for the pool</returns>
        Task<Result<PoolInfo>> GetPoolInfoAsync(Guid poolGroupGuid);

        /// <summary>
        /// Checks if a pool is mounted
        /// </summary>
        /// <param name="poolGroupGuid">The pool group GUID</param>
        /// <returns>True if the pool is mounted, otherwise false</returns>
        Task<Result<bool>> IsPoolMountedAsync(Guid poolGroupGuid);
    }

    /// <summary>
    /// Provides information about RAID pools in the system with caching support.
    /// </summary>
    /// <remarks>
    /// This service:
    /// - Retrieves pool information with appropriate caching
    /// - Provides detailed metrics about pool status and health
    /// - Reads pool component information
    /// - Monitors pool status changes
    /// - Uses IMdStatReader for low-level RAID information
    /// 
    /// Focuses on efficient, cached retrieval of pool information without
    /// modifying pool state.
    /// </remarks>
    public class PoolInfoService : IPoolInfoService
    {
        private readonly ILogger<PoolInfoService> _logger;
        private readonly ISystemCommandService _commandService;
        private readonly IMdStatReader _mdStatReader;
        private readonly IMountInfoReader _mountInfoReader;
        private readonly IDriveInfoService _driveInfoService;
        private readonly IMemoryCache _cache;
        private readonly IPoolMetadataService _metadataService;
        
        public PoolInfoService(
            ILogger<PoolInfoService> logger,
            ISystemCommandService commandService,
            IMdStatReader mdStatReader,
            IMountInfoReader mountInfoReader,
            IDriveInfoService driveInfoService,
            IMemoryCache cache,
            IPoolMetadataService metadataService)
        {
            _logger = logger;
            _commandService = commandService;
            _mdStatReader = mdStatReader;
            _mountInfoReader = mountInfoReader;
            _driveInfoService = driveInfoService;
            _cache = cache;
            _metadataService = metadataService;
        }

        /// <inheritdoc />
        public async Task<Result<PoolSizeInfo>> GetPoolSizeInfoAsync(Guid poolGroupGuid)
        {
            try
            {
                // Get the metadata to find the mount path
                var metadataResult = await _metadataService.GetPoolMetadataAsync(poolGroupGuid);
                if (!metadataResult.Success || metadataResult.Data == null)
                {
                    return Result<PoolSizeInfo>.Error("Failed to retrieve pool metadata");
                }
                
                var metadata = metadataResult.Data;
                if (string.IsNullOrEmpty(metadata.MountPath) || !metadata.IsMounted)
                {
                    return Result<PoolSizeInfo>.Error("Pool is not mounted");
                }
                
                // Use .NET's DriveInfo to get space information
                var driveInfo = new DriveInfo(metadata.MountPath);
                if (!driveInfo.IsReady)
                {
                    return Result<PoolSizeInfo>.Error("Drive is not ready");
                }
                
                // Calculate usage percentage
                var usedSpace = driveInfo.TotalSize - driveInfo.AvailableFreeSpace;
                var usagePercent = (usedSpace * 100.0 / driveInfo.TotalSize).ToString("0.0") + "%";
                
                return Result<PoolSizeInfo>.Success(new PoolSizeInfo
                {
                    PoolGroupGuid = poolGroupGuid,
                    Size = driveInfo.TotalSize,
                    Used = usedSpace,
                    Available = driveInfo.AvailableFreeSpace,
                    UsePercent = usagePercent,
                    MountPath = metadata.MountPath,
                    Label = metadata.Label ?? string.Empty
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pool size information for {PoolGroupGuid}", poolGroupGuid);
                return Result<PoolSizeInfo>.Error($"Error: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public async Task<Result<IEnumerable<PoolSizeInfo>>> GetAllPoolSizesAsync()
        {
            try
            {
                // Get all pool metadata
                var metadataResult = await _metadataService.GetAllPoolMetadataAsync();
                if (!metadataResult.Success || metadataResult.Data == null)
                {
                    return Result<IEnumerable<PoolSizeInfo>>.Error("Failed to retrieve pool metadata");
                }
                
                var poolSizes = new List<PoolSizeInfo>();
                
                foreach (var metadata in metadataResult.Data)
                {
                    // Only get size info for mounted pools
                    if (!string.IsNullOrEmpty(metadata.MountPath) && metadata.IsMounted)
                    {
                        var sizeResult = await GetPoolSizeInfoAsync(metadata.PoolGroupGuid);
                        if (sizeResult.Success && sizeResult.Data != null)
                        {
                            poolSizes.Add(sizeResult.Data);
                        }
                        else
                        {
                            // Add a placeholder with error information
                            poolSizes.Add(new PoolSizeInfo
                            {
                                PoolGroupGuid = metadata.PoolGroupGuid,
                                Label = metadata.Label ?? string.Empty,
                                MountPath = metadata.MountPath ?? string.Empty,
                                Error = sizeResult.ErrorMessage
                            });
                        }
                    }
                    else
                    {
                        // Add a placeholder for unmounted pools
                        poolSizes.Add(new PoolSizeInfo
                        {
                            PoolGroupGuid = metadata.PoolGroupGuid,
                            Label = metadata.Label ?? string.Empty,
                            MountPath = metadata.MountPath ?? string.Empty,
                            Error = "Pool is not mounted"
                        });
                    }
                }
                
                return Result<IEnumerable<PoolSizeInfo>>.Success(poolSizes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all pool sizes");
                return Result<IEnumerable<PoolSizeInfo>>.Error($"Error: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public async Task<Result<PoolHealthInfo>> GetPoolHealthInfoAsync(Guid poolGroupGuid)
        {
            try
            {
                // Get the metadata
                var metadataResult = await _metadataService.GetPoolMetadataAsync(poolGroupGuid);
                if (!metadataResult.Success || metadataResult.Data == null)
                {
                    return Result<PoolHealthInfo>.Error("Failed to retrieve pool metadata");
                }
                
                var metadata = metadataResult.Data;
                
                // Convert the GUID to the mdadm UUID format (no dashes)
                string mdadmUuid = poolGroupGuid.ToString("N");
                
                // Get MD stat information
                var mdStatInfo = await _mdStatReader.GetMdStatInfoAsync();
                
                // Find the array in mdstat
                MdStatArrayInfo? arrayInfo = null;
                foreach (var array in mdStatInfo.Arrays.Values)
                {
                    // Check if this is our array
                    // Since mdstat doesn't show UUID, we match by looking at metadata and device paths
                    if (array.Name.StartsWith($"md/") && array.Name.Contains(mdadmUuid))
                    {
                        arrayInfo = array;
                        break;
                    }
                }
                
                if (arrayInfo == null)
                {
                    // If not found by md/ path, try to match by looking at metadata devices
                    foreach (var array in mdStatInfo.Arrays.Values)
                    {
                        bool matchesDrives = true;
                        foreach (var device in array.Devices)
                        {
                            // Check if any drive in the metadata matches this device
                            bool foundInMetadata = false;
                            foreach (var drive in metadata.Drives.Values)
                            {
                                if (drive.DeviceName == device)
                                {
                                    foundInMetadata = true;
                                    break;
                                }
                            }
                            
                            if (!foundInMetadata)
                            {
                                matchesDrives = false;
                                break;
                            }
                        }
                        
                        if (matchesDrives)
                        {
                            arrayInfo = array;
                            break;
                        }
                    }
                }
                
                if (arrayInfo == null)
                {
                    return Result<PoolHealthInfo>.Error("Pool not found in mdstat");
                }
                
                // Get detailed information from mdadm --detail
                string mdDevicePath = $"/dev/md/{mdadmUuid}";
                if (!File.Exists(mdDevicePath))
                {
                    // Try using mdX format
                    mdDevicePath = $"/dev/{arrayInfo.Name}";
                }
                
                var detailResult = await _commandService.ExecuteCommandAsync($"mdadm --detail {mdDevicePath}", true);
                
                PoolHealthStatus status = PoolHealthStatus.Unknown;
                string statusDetails = string.Empty;
                
                if (detailResult.Success)
                {
                    // Parse the mdadm --detail output
                    if (detailResult.Output.Contains("State : clean") || detailResult.Output.Contains("State : active"))
                    {
                        status = PoolHealthStatus.Healthy;
                        statusDetails = "RAID array is clean and active";
                    }
                    else if (detailResult.Output.Contains("State : degraded"))
                    {
                        status = PoolHealthStatus.Degraded;
                        statusDetails = "RAID array is degraded, missing one or more drives";
                    }
                    else if (detailResult.Output.Contains("State : resyncing") || 
                             detailResult.Output.Contains("State : recovering") ||
                             detailResult.Output.Contains("State : checking"))
                    {
                        status = PoolHealthStatus.Rebuilding;
                        
                        // Extract rebuild percentage
                        if (arrayInfo.RecoveryInfo != null)
                        {
                            statusDetails = $"Rebuilding: {arrayInfo.RecoveryInfo.Progress}% complete, estimated finish time: {arrayInfo.RecoveryInfo.FinishTime}";
                        }
                        else
                        {
                            statusDetails = "Rebuilding in progress";
                        }
                    }
                    else if (detailResult.Output.Contains("State : failed"))
                    {
                        status = PoolHealthStatus.Failed;
                        statusDetails = "RAID array has failed";
                    }
                    else
                    {
                        status = PoolHealthStatus.Unknown;
                        statusDetails = "Could not determine RAID array status";
                    }
                }
                else
                {
                    _logger.LogWarning("Failed to get mdadm details: {Error}", detailResult.Error);
                    statusDetails = "Could not get detailed RAID information";
                }
                
                var healthInfo = new PoolHealthInfo
                {
                    PoolGroupGuid = poolGroupGuid,
                    Status = status,
                    StatusDetails = statusDetails,
                    RaidLevel = arrayInfo.Level,
                    ActiveDevices = arrayInfo.ActiveDevices,
                    TotalDevices = arrayInfo.TotalDevices,
                    LastUpdated = DateTime.UtcNow
                };
                
                return Result<PoolHealthInfo>.Success(healthInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pool health information for {PoolGroupGuid}", poolGroupGuid);
                return Result<PoolHealthInfo>.Error($"Error: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public async Task<Result<PoolDetailInfo>> GetPoolDetailInfoAsync(Guid poolGroupGuid)
        {
            try
            {
                // Get the metadata
                var metadataResult = await _metadataService.GetPoolMetadataAsync(poolGroupGuid);
                if (!metadataResult.Success || metadataResult.Data == null)
                {
                    return Result<PoolDetailInfo>.Error("Failed to retrieve pool metadata");
                }
                
                var metadata = metadataResult.Data;
                
                // Get size information
                PoolSizeInfo? sizeInfo = null;
                if (metadata.IsMounted && !string.IsNullOrEmpty(metadata.MountPath))
                {
                    var sizeResult = await GetPoolSizeInfoAsync(poolGroupGuid);
                    if (sizeResult.Success)
                    {
                        sizeInfo = sizeResult.Data;
                    }
                }
                
                // Get health information
                var healthResult = await GetPoolHealthInfoAsync(poolGroupGuid);
                PoolHealthInfo? healthInfo = healthResult.Success ? healthResult.Data : null;
                
                // Combine all information
                var detailInfo = new PoolDetailInfo
                {
                    PoolGroupGuid = poolGroupGuid,
                    Label = metadata.Label ?? string.Empty,
                    MountPath = metadata.MountPath ?? string.Empty,
                    IsMounted = metadata.IsMounted,
                    CreatedAt = metadata.CreatedAt,
                    DriveCount = metadata.Drives.Count,
                    Health = healthInfo?.Status ?? PoolHealthStatus.Unknown,
                    HealthDetails = healthInfo?.StatusDetails ?? "Health status unknown",
                    RaidLevel = healthInfo?.RaidLevel ?? "unknown"
                };
                
                // Add size information if available
                if (sizeInfo != null)
                {
                    detailInfo.Size = sizeInfo.Size;
                    detailInfo.Used = sizeInfo.Used;
                    detailInfo.Available = sizeInfo.Available;
                    detailInfo.UsePercent = sizeInfo.UsePercent;
                }
                
                // Add drive information
                detailInfo.Drives = new List<PoolDriveInfo>();
                foreach (var drive in metadata.Drives)
                {
                    detailInfo.Drives.Add(new PoolDriveInfo
                    {
                        DiskIdName = drive.Key,
                        Label = drive.Value.Label ?? string.Empty,
                        SerialNumber = drive.Value.SerialNumber ?? string.Empty,
                        DiskIdPath = drive.Value.DiskIdPath ?? string.Empty
                    });
                }
                
                return Result<PoolDetailInfo>.Success(detailInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pool detail information for {PoolGroupGuid}", poolGroupGuid);
                return Result<PoolDetailInfo>.Error($"Error: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public async Task<Result<bool>> PoolExistsAsync(Guid poolGroupGuid)
        {
            try
            {
                // Check if pool exists in metadata
                var metadataResult = await _metadataService.GetPoolMetadataAsync(poolGroupGuid);
                if (!metadataResult.Success)
                {
                    return Result<bool>.Success(false); // Not found in metadata
                }
                
                // Convert the GUID to the mdadm UUID format (no dashes)
                string mdadmUuid = poolGroupGuid.ToString("N");
                
                // Check if the MD device exists
                string mdDevicePath = $"/dev/md/{mdadmUuid}";
                bool deviceExists = File.Exists(mdDevicePath);
                
                // If the device exists, the pool exists
                if (deviceExists)
                {
                    return Result<bool>.Success(true);
                }
                
                // If not found by path, check mdstat
                var mdStatInfo = await _mdStatReader.GetMdStatInfoAsync();
                
                // Try to find the array in mdstat
                foreach (var array in mdStatInfo.Arrays.Values)
                {
                    if (array.Name.Contains(mdadmUuid))
                    {
                        return Result<bool>.Success(true);
                    }
                }
                
                // Check if any array in mdstat matches the drives in our metadata
                var metadata = metadataResult.Data;
                if (metadata != null && metadata.Drives.Any())
                {
                    foreach (var array in mdStatInfo.Arrays.Values)
                    {
                        // Count how many drives from our metadata are in this array
                        int matchingDrives = 0;
                        foreach (var drive in metadata.Drives)
                        {
                            string deviceName = Path.GetFileName(drive.Value.DiskIdPath);
                            if (array.Devices.Contains(deviceName))
                            {
                                matchingDrives++;
                            }
                        }
                        
                        // If all drives match, this is our array
                        if (matchingDrives == metadata.Drives.Count && matchingDrives > 0)
                        {
                            return Result<bool>.Success(true);
                        }
                    }
                }
                
                // Pool does not exist in the system
                return Result<bool>.Success(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if pool {PoolGroupGuid} exists", poolGroupGuid);
                return Result<bool>.Error($"Error checking pool existence: {ex.Message}");
            }
        }
        
        /// <inheritdoc />
        public async Task<Result<IEnumerable<PoolInfo>>> GetAllPoolsAsync()
        {
            try
            {
                // Get all pool metadata
                var metadataResult = await _metadataService.GetAllPoolMetadataAsync();
                if (!metadataResult.Success)
                {
                    return Result<IEnumerable<PoolInfo>>.Error(metadataResult.ErrorMessage);
                }
                
                var pools = new List<PoolInfo>();
                
                // Get MD stat information for all arrays
                var mdStatInfo = await _mdStatReader.GetMdStatInfoAsync();
                
                foreach (var metadata in metadataResult.Data)
                {
                    // Convert the GUID to mdadm UUID format
                    string mdadmUuid = metadata.PoolGroupGuid.ToString("N");
                    
                    // Create basic pool info from metadata
                    var poolInfo = new PoolInfo
                    {
                        PoolGroupGuid = metadata.PoolGroupGuid,
                        Label = metadata.Label ?? string.Empty,
                        MountPath = metadata.MountPath ?? string.Empty,
                        IsMounted = metadata.IsMounted,
                        CreatedAt = metadata.CreatedAt,
                        DriveCount = metadata.Drives.Count
                    };
                    
                    // Try to find the array in mdstat
                    MdStatArrayInfo? arrayInfo = null;
                    foreach (var array in mdStatInfo.Arrays.Values)
                    {
                        if (array.Name.Contains(mdadmUuid))
                        {
                            arrayInfo = array;
                            break;
                        }
                    }
                    
                    // If not found by name, try to match by drives
                    if (arrayInfo == null && metadata.Drives.Any())
                    {
                        foreach (var array in mdStatInfo.Arrays.Values)
                        {
                            // Count how many drives from our metadata are in this array
                            int matchingDrives = 0;
                            foreach (var drive in metadata.Drives)
                            {
                                string deviceName = Path.GetFileName(drive.Value.DiskIdPath);
                                if (array.Devices.Contains(deviceName))
                                {
                                    matchingDrives++;
                                }
                            }
                            
                            // If all drives match, this is our array
                            if (matchingDrives == metadata.Drives.Count && matchingDrives > 0)
                            {
                                arrayInfo = array;
                                break;
                            }
                        }
                    }
                    
                    // Add array info if found
                    if (arrayInfo != null)
                    {
                        poolInfo.Status = arrayInfo.Active ? "active" : "inactive";
                        poolInfo.RaidLevel = arrayInfo.Level;
                        
                        // Determine health status
                        if (arrayInfo.RecoveryInfo != null)
                        {
                            poolInfo.Status = "rebuilding";
                            poolInfo.RebuildProgress = arrayInfo.RecoveryInfo.Progress;
                        }
                        else if (arrayInfo.ActiveDevices < arrayInfo.TotalDevices)
                        {
                            poolInfo.Status = "degraded";
                        }
                    }
                    else
                    {
                        poolInfo.Status = "unavailable";
                    }
                    
                    pools.Add(poolInfo);
                }
                
                return Result<IEnumerable<PoolInfo>>.Success(pools);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all pools");
                return Result<IEnumerable<PoolInfo>>.Error($"Error getting pools: {ex.Message}");
            }
        }
        
        /// <inheritdoc />
        public async Task<Result<PoolInfo>> GetPoolInfoAsync(Guid poolGroupGuid)
        {
            try
            {
                // Get all pools and find the one we want
                var allPoolsResult = await GetAllPoolsAsync();
                if (!allPoolsResult.Success)
                {
                    return Result<PoolInfo>.Error(allPoolsResult.ErrorMessage);
                }
                
                var pool = allPoolsResult.Data?.FirstOrDefault(p => p.PoolGroupGuid == poolGroupGuid);
                if (pool == null)
                {
                    return Result<PoolInfo>.Error($"Pool with GUID {poolGroupGuid} not found");
                }
                
                return Result<PoolInfo>.Success(pool);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pool {PoolGroupGuid}", poolGroupGuid);
                return Result<PoolInfo>.Error($"Error getting pool: {ex.Message}");
            }
        }
        
        /// <inheritdoc />
        public async Task<Result<bool>> IsPoolMountedAsync(Guid poolGroupGuid)
        {
            try
            {
                // Get pool metadata
                var metadataResult = await _metadataService.GetPoolMetadataAsync(poolGroupGuid);
                if (!metadataResult.Success || metadataResult.Data == null)
                {
                    return Result<bool>.Error("Failed to retrieve pool metadata");
                }
                
                var metadata = metadataResult.Data;
                
                // Check if mount path is set
                if (string.IsNullOrEmpty(metadata.MountPath))
                {
                    return Result<bool>.Success(false); // No mount path set
                }
                
                // Check if path is mounted according to metadata
                if (!metadata.IsMounted)
                {
                    return Result<bool>.Success(false); // Not mounted according to metadata
                }
                
                // Verify mount by checking if the mountpoint exists and is a mount point
                var mountInfos = await _mountInfoReader.GetMountPointsAsync();
                bool isMounted = mountInfos.Any(m => m.MountPath == metadata.MountPath);
                
                // If it's supposed to be mounted but isn't, update metadata
                if (!isMounted && metadata.IsMounted)
                {
                    // Pool metadata indicates it's mounted but it's not actually mounted
                    // Update metadata
                    metadata.IsMounted = false;
                    await _metadataService.UpdatePoolMetadataAsync(metadata);
                    _logger.LogWarning("Pool {PoolGuid} was marked as mounted but mount point {MountPath} is not a valid mount point. Metadata updated.", 
                        poolGroupGuid, metadata.MountPath);
                }
                
                return Result<bool>.Success(isMounted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if pool {PoolGroupGuid} is mounted", poolGroupGuid);
                return Result<bool>.Error($"Error checking mount status: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Basic information about a storage pool
    /// </summary>
    public class PoolInfo
    {
        /// <summary>
        /// The pool group GUID
        /// </summary>
        public Guid PoolGroupGuid { get; set; }
        
        /// <summary>
        /// The label of the pool
        /// </summary>
        public string Label { get; set; } = string.Empty;
        
        /// <summary>
        /// The mount path of the pool
        /// </summary>
        public string MountPath { get; set; } = string.Empty;
        
        /// <summary>
        /// Whether the pool is currently mounted
        /// </summary>
        public bool IsMounted { get; set; }
        
        /// <summary>
        /// When the pool was created
        /// </summary>
        public DateTime CreatedAt { get; set; }
        
        /// <summary>
        /// Number of drives in the pool
        /// </summary>
        public int DriveCount { get; set; }
        
        /// <summary>
        /// Current status of the pool (active, inactive, degraded, rebuilding, etc.)
        /// </summary>
        public string Status { get; set; } = string.Empty;
        
        /// <summary>
        /// RAID level (e.g., "raid1", "raid5")
        /// </summary>
        public string RaidLevel { get; set; } = string.Empty;
        
        /// <summary>
        /// If the pool is rebuilding, the progress percentage
        /// </summary>
        public string? RebuildProgress { get; set; }
    }

    /// <summary>
    /// Size information for a storage pool
    /// </summary>
    public class PoolSizeInfo
    {
        /// <summary>
        /// The pool group GUID
        /// </summary>
        public Guid PoolGroupGuid { get; set; }
        
        /// <summary>
        /// The label of the pool
        /// </summary>
        public string Label { get; set; } = string.Empty;
        
        /// <summary>
        /// The mount path of the pool
        /// </summary>
        public string MountPath { get; set; } = string.Empty;
        
        /// <summary>
        /// Total size of the pool in bytes
        /// </summary>
        public long Size { get; set; }
        
        /// <summary>
        /// Used space in the pool in bytes
        /// </summary>
        public long Used { get; set; }
        
        /// <summary>
        /// Available space in the pool in bytes
        /// </summary>
        public long Available { get; set; }
        
        /// <summary>
        /// Usage percentage as a string (e.g., "45.2%")
        /// </summary>
        public string UsePercent { get; set; } = string.Empty;
        
        /// <summary>
        /// Optional error message if size information could not be retrieved
        /// </summary>
        public string? Error { get; set; }
    }

    /// <summary>
    /// Health status for a storage pool
    /// </summary>
    public enum PoolHealthStatus
    {
        /// <summary>
        /// Health status is unknown
        /// </summary>
        Unknown,
        
        /// <summary>
        /// Pool is healthy
        /// </summary>
        Healthy,
        
        /// <summary>
        /// Pool is degraded (e.g., missing a drive in a RAID array)
        /// </summary>
        Degraded,
        
        /// <summary>
        /// Pool is rebuilding or resyncing
        /// </summary>
        Rebuilding,
        
        /// <summary>
        /// Pool has failed
        /// </summary>
        Failed
    }

    /// <summary>
    /// Health information for a storage pool
    /// </summary>
    public class PoolHealthInfo
    {
        /// <summary>
        /// The pool group GUID
        /// </summary>
        public Guid PoolGroupGuid { get; set; }
        
        /// <summary>
        /// Overall health status
        /// </summary>
        public PoolHealthStatus Status { get; set; }
        
        /// <summary>
        /// Detailed status message
        /// </summary>
        public string StatusDetails { get; set; } = string.Empty;
        
        /// <summary>
        /// RAID level (e.g., "raid1", "raid5")
        /// </summary>
        public string RaidLevel { get; set; } = string.Empty;
        
        /// <summary>
        /// Number of active devices in the RAID array
        /// </summary>
        public int ActiveDevices { get; set; }
        
        /// <summary>
        /// Total number of devices in the RAID array
        /// </summary>
        public int TotalDevices { get; set; }
        
        /// <summary>
        /// When the health status was last updated
        /// </summary>
        public DateTime LastUpdated { get; set; }
    }

    /// <summary>
    /// Summary of a drive in a pool
    /// </summary>
    public class PoolDriveInfo
    {
        /// <summary>
        /// The disk ID name
        /// </summary>
        public string DiskIdName { get; set; } = string.Empty;
        
        /// <summary>
        /// User-assigned label for the drive
        /// </summary>
        public string Label { get; set; } = string.Empty;
        
        /// <summary>
        /// Drive serial number
        /// </summary>
        public string SerialNumber { get; set; } = string.Empty;
        
        /// <summary>
        /// Full path to the disk by ID
        /// </summary>
        public string DiskIdPath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Detailed information about a storage pool
    /// </summary>
    public class PoolDetailInfo
    {
        /// <summary>
        /// The pool group GUID
        /// </summary>
        public Guid PoolGroupGuid { get; set; }
        
        /// <summary>
        /// The label of the pool
        /// </summary>
        public string Label { get; set; } = string.Empty;
        
        /// <summary>
        /// The mount path of the pool
        /// </summary>
        public string MountPath { get; set; } = string.Empty;
        
        /// <summary>
        /// Whether the pool is currently mounted
        /// </summary>
        public bool IsMounted { get; set; }
        
        /// <summary>
        /// When the pool was created
        /// </summary>
        public DateTime CreatedAt { get; set; }
        
        /// <summary>
        /// Total size of the pool in bytes
        /// </summary>
        public long Size { get; set; }
        
        /// <summary>
        /// Used space in the pool in bytes
        /// </summary>
        public long Used { get; set; }
        
        /// <summary>
        /// Available space in the pool in bytes
        /// </summary>
        public long Available { get; set; }
        
        /// <summary>
        /// Usage percentage as a string (e.g., "45.2%")
        /// </summary>
        public string UsePercent { get; set; } = string.Empty;
        
        /// <summary>
        /// Number of drives in the pool
        /// </summary>
        public int DriveCount { get; set; }
        
        /// <summary>
        /// Overall health status
        /// </summary>
        public PoolHealthStatus Health { get; set; }
        
        /// <summary>
        /// Detailed health information
        /// </summary>
        public string HealthDetails { get; set; } = string.Empty;
        
        /// <summary>
        /// RAID level (e.g., "raid1", "raid5")
        /// </summary>
        public string RaidLevel { get; set; } = string.Empty;
        
        /// <summary>
        /// Information about drives in the pool
        /// </summary>
        public List<PoolDriveInfo> Drives { get; set; } = new();
    }
}