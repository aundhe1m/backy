using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Backy.Agent.Models;
using Backy.Agent.Services.Core;
using Backy.Agent.Services.Storage.Drives;
using Backy.Agent.Services.Storage.Metadata;

namespace Backy.Agent.Services.Storage.Pools
{
    /// <summary>
    /// Provides RAID pool management operations and information access.
    /// </summary>
    /// <remarks>
    /// This service:
    /// - Acts as a facade over specialized pool services
    /// - Implements business logic for pool operations
    /// - Delegates to IPoolInfoService for information retrieval
    /// - Manages pool metadata through IPoolMetadataService
    /// 
    /// Implements high-level pool management while delegating implementation details
    /// to specialized services.
    /// </remarks>
    public class PoolService : IPoolService
    {
        private readonly ILogger<PoolService> _logger;
        private readonly IPoolInfoService _poolInfoService;
        private readonly IPoolMetadataService _poolMetadataService;
        private readonly IDriveInfoService _driveInfoService;
        private readonly ISystemCommandService _commandService;
        
        public PoolService(
            ILogger<PoolService> logger,
            IPoolInfoService poolInfoService, 
            IPoolMetadataService poolMetadataService,
            IDriveInfoService driveInfoService,
            ISystemCommandService commandService)
        {
            _logger = logger;
            _poolInfoService = poolInfoService;
            _poolMetadataService = poolMetadataService;
            _driveInfoService = driveInfoService;
            _commandService = commandService;
        }
        
        // Delegate to IPoolInfoService for information retrieval methods
        public Task<Result<PoolSizeInfo>> GetPoolSizeInfoAsync(Guid poolGroupGuid) => 
            _poolInfoService.GetPoolSizeInfoAsync(poolGroupGuid);
        
        public Task<Result<IEnumerable<PoolSizeInfo>>> GetAllPoolSizesAsync() => 
            _poolInfoService.GetAllPoolSizesAsync();
        
        public Task<Result<PoolHealthInfo>> GetPoolHealthInfoAsync(Guid poolGroupGuid) => 
            _poolInfoService.GetPoolHealthInfoAsync(poolGroupGuid);
        
        public Task<Result<PoolDetailInfo>> GetPoolDetailInfoAsync(Guid poolGroupGuid) => 
            _poolInfoService.GetPoolDetailInfoAsync(poolGroupGuid);
        
        public Task<Result<bool>> PoolExistsAsync(Guid poolGroupGuid) => 
            _poolInfoService.PoolExistsAsync(poolGroupGuid);
        
        public Task<Result<IEnumerable<PoolInfo>>> GetAllPoolsAsync() => 
            _poolInfoService.GetAllPoolsAsync();
        
        public Task<Result<PoolInfo>> GetPoolInfoAsync(Guid poolGroupGuid) => 
            _poolInfoService.GetPoolInfoAsync(poolGroupGuid);
        
        public Task<Result<bool>> IsPoolMountedAsync(Guid poolGroupGuid) => 
            _poolInfoService.IsPoolMountedAsync(poolGroupGuid);
        
        /// <inheritdoc />
        public Task<Result<IEnumerable<PoolInfo>>> GetPoolsAsync()
        {
            return GetAllPoolsAsync();
        }
        
        /// <inheritdoc />
        public async Task<Result<PoolCreationResponse>> CreatePoolAsync(PoolCreationRequest request)
        {
            try
            {
                _logger.LogInformation("Initiating pool creation process for {DriveCount} drives with RAID1", 
                    request.Drives.Count);
                
                // Validation
                if (request.Drives == null || !request.Drives.Any())
                {
                    return Result<PoolCreationResponse>.Error("No drives specified for pool creation");
                }
                
                if (string.IsNullOrWhiteSpace(request.Label))
                {
                    return Result<PoolCreationResponse>.Error("Pool label cannot be empty");
                }
                
                if (string.IsNullOrWhiteSpace(request.MountPath))
                {
                    return Result<PoolCreationResponse>.Error("Mount path cannot be empty");
                }
                
                // Check drive validity
                foreach (var driveId in request.Drives)
                {
                    var driveResult = await _driveInfoService.GetDriveInfoAsync(driveId);
                    if (!driveResult.Success)
                    {
                        return Result<PoolCreationResponse>.Error($"Drive {driveId} not found or invalid: {driveResult.ErrorMessage}");
                    }
                    
                    var driveInfo = driveResult.Data;
                    
                    // Check if drive is already in use
                    if (driveInfo.InUse)
                    {
                        return Result<PoolCreationResponse>.Error($"Drive {driveId} ({driveInfo.SerialNumber}) is already in use");
                    }
                    
                    // Check if drive has partitions
                    if (driveInfo.HasPartitions)
                    {
                        return Result<PoolCreationResponse>.Error($"Drive {driveId} ({driveInfo.SerialNumber}) already has partitions");
                    }
                }
                
                // Check if mount path exists and is empty
                if (Directory.Exists(request.MountPath))
                {
                    // Check if directory is empty
                    if (Directory.EnumerateFileSystemEntries(request.MountPath).Any())
                    {
                        return Result<PoolCreationResponse>.Error($"Mount path {request.MountPath} is not empty");
                    }
                }
                else
                {
                    // Create the mount directory
                    try
                    {
                        Directory.CreateDirectory(request.MountPath);
                        _logger.LogInformation("Created mount directory at {MountPath}", request.MountPath);
                    }
                    catch (Exception ex)
                    {
                        return Result<PoolCreationResponse>.Error($"Failed to create mount directory: {ex.Message}");
                    }
                }
                
                // Generate a pool GUID if not provided
                var poolGroupGuid = request.PoolGroupGuid != Guid.Empty ? 
                    request.PoolGroupGuid : Guid.NewGuid();
                
                // Always use RAID1 for the level
                const string raidLevel = "raid1";
                
                // Collect drive information for metadata
                var drivesMetadata = new Dictionary<string, DriveMetadata>();
                foreach (var driveId in request.Drives)
                {
                    var driveResult = await _driveInfoService.GetDriveInfoAsync(driveId);
                    var driveInfo = driveResult.Data;
                    
                    drivesMetadata[driveId] = new DriveMetadata
                    {
                        SerialNumber = driveInfo.SerialNumber,
                        Label = driveInfo.Model,
                        DiskIdPath = driveInfo.Path,
                        DeviceName = driveInfo.DeviceName,
                        AddedAt = DateTime.UtcNow
                    };
                }
                
                // Create metadata first
                var metadata = new PoolMetadata
                {
                    PoolGroupGuid = poolGroupGuid,
                    Label = request.Label,
                    MountPath = request.MountPath,
                    RaidLevel = raidLevel,
                    CreatedAt = DateTime.UtcNow,
                    IsMounted = false, // Will be set to true after successful mounting
                    Drives = drivesMetadata
                };
                
                var saveResult = await _poolMetadataService.SavePoolMetadataAsync(metadata);
                if (!saveResult.Success)
                {
                    return Result<PoolCreationResponse>.Error($"Failed to save pool metadata: {saveResult.ErrorMessage}");
                }
                
                // Convert the GUID to the mdadm UUID format (no dashes)
                string mdadmUuid = poolGroupGuid.ToString("N");
                string devicePath = $"/dev/md/{mdadmUuid}";
                
                // Get drive device paths
                List<string> driveDevicePaths = new List<string>();
                foreach (var driveId in request.Drives)
                {
                    var driveResult = await _driveInfoService.GetDriveInfoAsync(driveId);
                    driveDevicePaths.Add(driveResult.Data.Path);
                }
                
                // Create the RAID array
                string createCommand = $"mdadm --create {devicePath} --name={request.Label} --level=1 --uuid={mdadmUuid} --raid-devices={driveDevicePaths.Count} {string.Join(" ", driveDevicePaths)} --run --force";
                _logger.LogInformation("Creating RAID array with command: {Command}", createCommand);
                
                var createResult = await _commandService.ExecuteCommandAsync(createCommand, true);
                if (!createResult.Success)
                {
                    // Remove the metadata since creation failed
                    await _poolMetadataService.RemovePoolMetadataAsync(poolGroupGuid);
                    return Result<PoolCreationResponse>.Error($"Failed to create RAID array: {createResult.Error}");
                }
                
                // Wait for the device to be fully available
                bool deviceReady = false;
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    if (File.Exists(devicePath))
                    {
                        deviceReady = true;
                        break;
                    }
                    await Task.Delay(1000);
                }
                
                if (!deviceReady)
                {
                    await _poolMetadataService.RemovePoolMetadataAsync(poolGroupGuid);
                    return Result<PoolCreationResponse>.Error("Timeout waiting for RAID device to become available");
                }
                
                // Format the array with ext4
                string formatCommand = $"mkfs.ext4 -L {request.Label} {devicePath}";
                _logger.LogInformation("Formatting RAID array with command: {Command}", formatCommand);
                
                var formatResult = await _commandService.ExecuteCommandAsync(formatCommand, true);
                if (!formatResult.Success)
                {
                    // Try to stop the array
                    await _commandService.ExecuteCommandAsync($"mdadm --stop {devicePath}", true);
                    await _poolMetadataService.RemovePoolMetadataAsync(poolGroupGuid);
                    return Result<PoolCreationResponse>.Error($"Failed to format RAID array: {formatResult.Error}");
                }
                
                // Mount the array
                string mountCommand = $"mount {devicePath} {request.MountPath}";
                _logger.LogInformation("Mounting RAID array with command: {Command}", mountCommand);
                
                var mountResult = await _commandService.ExecuteCommandAsync(mountCommand, true);
                if (!mountResult.Success)
                {
                    // Try to stop the array
                    await _commandService.ExecuteCommandAsync($"mdadm --stop {devicePath}", true);
                    await _poolMetadataService.RemovePoolMetadataAsync(poolGroupGuid);
                    return Result<PoolCreationResponse>.Error($"Failed to mount RAID array: {mountResult.Error}");
                }
                
                // Update metadata with mounted state
                metadata.IsMounted = true;
                await _poolMetadataService.UpdatePoolMetadataAsync(metadata);
                
                _logger.LogInformation("Successfully created and mounted RAID array for pool {PoolGroupGuid}", poolGroupGuid);
                
                return Result<PoolCreationResponse>.Success(new PoolCreationResponse
                {
                    PoolGroupGuid = poolGroupGuid,
                    MountPath = request.MountPath,
                    Status = "Active"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating pool");
                return Result<PoolCreationResponse>.Error($"Failed to create pool: {ex.Message}");
            }
        }
        
        /// <inheritdoc />
        public async Task<Result<CommandResponse>> MountPoolAsync(Guid poolGroupGuid, string? mountPath = null)
        {
            try
            {
                _logger.LogInformation("Mounting pool {PoolGroupGuid}", poolGroupGuid);
                
                // Check if pool exists
                var existsResult = await PoolExistsAsync(poolGroupGuid);
                if (!existsResult.Success)
                {
                    return Result<CommandResponse>.Error($"Failed to check if pool exists: {existsResult.ErrorMessage}");
                }
                
                if (!existsResult.Data)
                {
                    return Result<CommandResponse>.Error($"Pool {poolGroupGuid} does not exist");
                }
                
                // Get pool metadata
                var metadataResult = await _poolMetadataService.GetPoolMetadataAsync(poolGroupGuid);
                if (!metadataResult.Success)
                {
                    return Result<CommandResponse>.Error($"Failed to retrieve pool metadata: {metadataResult.ErrorMessage}");
                }
                
                var metadata = metadataResult.Data;
                
                // Check if already mounted
                var mountedResult = await IsPoolMountedAsync(poolGroupGuid);
                if (mountedResult.Success && mountedResult.Data)
                {
                    _logger.LogWarning("Pool {PoolGroupGuid} is already mounted at {MountPath}", 
                        poolGroupGuid, metadata.MountPath);
                    
                    return Result<CommandResponse>.Success(new CommandResponse
                    {
                        Success = true,
                        Message = $"Pool is already mounted at {metadata.MountPath}"
                    });
                }
                
                // Use provided mount path or the one from metadata
                string effectiveMountPath = mountPath ?? metadata.MountPath;
                if (string.IsNullOrEmpty(effectiveMountPath))
                {
                    return Result<CommandResponse>.Error("No mount path specified");
                }
                
                // Ensure mount directory exists
                if (!Directory.Exists(effectiveMountPath))
                {
                    try
                    {
                        Directory.CreateDirectory(effectiveMountPath);
                        _logger.LogInformation("Created mount directory at {MountPath}", effectiveMountPath);
                    }
                    catch (Exception ex)
                    {
                        return Result<CommandResponse>.Error($"Failed to create mount directory: {ex.Message}");
                    }
                }
                
                // Convert the GUID to the mdadm UUID format (no dashes)
                string mdadmUuid = poolGroupGuid.ToString("N");
                string devicePath = $"/dev/md/{mdadmUuid}";
                
                // Check if the device exists
                if (!File.Exists(devicePath))
                {
                    // Try to find the device in /dev/mdX format
                    var mdStatInfo = await _commandService.ExecuteCommandAsync("cat /proc/mdstat", true);
                    string deviceName = null;
                    
                    if (mdStatInfo.Success)
                    {
                        // Look for a line with this UUID
                        foreach (var line in mdStatInfo.Output.Split('\n'))
                        {
                            if (line.Contains(mdadmUuid, StringComparison.OrdinalIgnoreCase))
                            {
                                var parts = line.Split(new[] { ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length > 0 && parts[0].StartsWith("md"))
                                {
                                    deviceName = parts[0];
                                    break;
                                }
                            }
                        }
                    }
                    
                    if (deviceName != null)
                    {
                        devicePath = $"/dev/{deviceName}";
                    }
                    else
                    {
                        // Try to assemble the array if it's not found
                        var assembleCommand = $"mdadm --assemble --uuid={mdadmUuid} {devicePath}";
                        var assembleResult = await _commandService.ExecuteCommandAsync(assembleCommand, true);
                        
                        if (!assembleResult.Success)
                        {
                            return Result<CommandResponse>.Error($"Device for pool {poolGroupGuid} not found and could not be assembled");
                        }
                        
                        // Wait for the device to become available
                        bool deviceReady = false;
                        for (int attempt = 0; attempt < 10; attempt++)
                        {
                            if (File.Exists(devicePath))
                            {
                                deviceReady = true;
                                break;
                            }
                            await Task.Delay(1000);
                        }
                        
                        if (!deviceReady)
                        {
                            return Result<CommandResponse>.Error($"Timeout waiting for device {devicePath} to become available after assembly");
                        }
                    }
                }
                
                // Mount the pool
                var mountCommand = $"mount {devicePath} {effectiveMountPath}";
                var mountResult = await _commandService.ExecuteCommandAsync(mountCommand, true);
                
                if (!mountResult.Success)
                {
                    return Result<CommandResponse>.Error($"Failed to mount pool: {mountResult.Error}");
                }
                
                // Update metadata with mount information
                metadata.MountPath = effectiveMountPath;
                metadata.IsMounted = true;
                await _poolMetadataService.UpdatePoolMetadataAsync(metadata);
                
                _logger.LogInformation("Successfully mounted pool {PoolGroupGuid} at {MountPath}", 
                    poolGroupGuid, effectiveMountPath);
                
                return Result<CommandResponse>.Success(new CommandResponse
                {
                    Success = true,
                    Message = $"Pool mounted at {effectiveMountPath}",
                    Output = mountResult.Output
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error mounting pool {PoolGroupGuid}", poolGroupGuid);
                return Result<CommandResponse>.Error($"Failed to mount pool: {ex.Message}");
            }
        }
        
        /// <inheritdoc />
        public async Task<Result<CommandResponse>> UnmountPoolAsync(Guid poolGroupGuid)
        {
            try
            {
                _logger.LogInformation("Unmounting pool {PoolGroupGuid}", poolGroupGuid);
                
                // Check if pool exists
                var existsResult = await PoolExistsAsync(poolGroupGuid);
                if (!existsResult.Success)
                {
                    return Result<CommandResponse>.Error($"Failed to check if pool exists: {existsResult.ErrorMessage}");
                }
                
                if (!existsResult.Data)
                {
                    return Result<CommandResponse>.Error($"Pool {poolGroupGuid} does not exist");
                }
                
                // Get pool metadata
                var metadataResult = await _poolMetadataService.GetPoolMetadataAsync(poolGroupGuid);
                if (!metadataResult.Success)
                {
                    return Result<CommandResponse>.Error($"Failed to retrieve pool metadata: {metadataResult.ErrorMessage}");
                }
                
                var metadata = metadataResult.Data;
                
                // Check if the pool is mounted
                var mountedResult = await IsPoolMountedAsync(poolGroupGuid);
                if (!mountedResult.Success)
                {
                    return Result<CommandResponse>.Error($"Failed to check mount status: {mountedResult.ErrorMessage}");
                }
                
                if (!mountedResult.Data)
                {
                    _logger.LogWarning("Pool {PoolGroupGuid} is not mounted", poolGroupGuid);
                    
                    // Update metadata to reflect unmounted state
                    metadata.IsMounted = false;
                    await _poolMetadataService.UpdatePoolMetadataAsync(metadata);
                    
                    return Result<CommandResponse>.Success(new CommandResponse
                    {
                        Success = true,
                        Message = "Pool is already unmounted"
                    });
                }
                
                // Unmount the pool
                var unmountCommand = $"umount {metadata.MountPath}";
                var unmountResult = await _commandService.ExecuteCommandAsync(unmountCommand, true);
                
                if (!unmountResult.Success)
                {
                    // Try with force option if regular unmount fails
                    unmountCommand = $"umount -f {metadata.MountPath}";
                    unmountResult = await _commandService.ExecuteCommandAsync(unmountCommand, true);
                    
                    if (!unmountResult.Success)
                    {
                        return Result<CommandResponse>.Error($"Failed to unmount pool: {unmountResult.Error}");
                    }
                }
                
                // Update metadata to reflect unmounted state
                metadata.IsMounted = false;
                await _poolMetadataService.UpdatePoolMetadataAsync(metadata);
                
                _logger.LogInformation("Successfully unmounted pool {PoolGroupGuid} from {MountPath}", 
                    poolGroupGuid, metadata.MountPath);
                
                return Result<CommandResponse>.Success(new CommandResponse
                {
                    Success = true,
                    Message = $"Pool unmounted from {metadata.MountPath}",
                    Output = unmountResult.Output
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unmounting pool {PoolGroupGuid}", poolGroupGuid);
                return Result<CommandResponse>.Error($"Failed to unmount pool: {ex.Message}");
            }
        }
        
        /// <inheritdoc />
        public async Task<Result<CommandResponse>> RemovePoolAsync(Guid poolGroupGuid)
        {
            try
            {
                _logger.LogInformation("Removing pool {PoolGroupGuid}", poolGroupGuid);
                
                // Check if pool exists
                var existsResult = await PoolExistsAsync(poolGroupGuid);
                if (!existsResult.Success)
                {
                    return Result<CommandResponse>.Error($"Failed to check if pool exists: {existsResult.ErrorMessage}");
                }
                
                if (!existsResult.Data)
                {
                    return Result<CommandResponse>.Error($"Pool {poolGroupGuid} does not exist");
                }
                
                // Get pool metadata
                var metadataResult = await _poolMetadataService.GetPoolMetadataAsync(poolGroupGuid);
                if (!metadataResult.Success)
                {
                    return Result<CommandResponse>.Error($"Failed to retrieve pool metadata: {metadataResult.ErrorMessage}");
                }
                
                var metadata = metadataResult.Data;
                
                // Ensure pool is unmounted
                var mountedResult = await IsPoolMountedAsync(poolGroupGuid);
                if (mountedResult.Success && mountedResult.Data)
                {
                    var unmountResult = await UnmountPoolAsync(poolGroupGuid);
                    if (!unmountResult.Success)
                    {
                        return Result<CommandResponse>.Error($"Failed to unmount pool before removal: {unmountResult.ErrorMessage}");
                    }
                }
                
                // Convert the GUID to the mdadm UUID format (no dashes)
                string mdadmUuid = poolGroupGuid.ToString("N");
                string devicePath = $"/dev/md/{mdadmUuid}";
                
                // Check if the device exists
                if (!File.Exists(devicePath))
                {
                    // Try to find the device in /dev/mdX format
                    var mdStatInfo = await _commandService.ExecuteCommandAsync("cat /proc/mdstat", true);
                    string deviceName = null;
                    
                    if (mdStatInfo.Success)
                    {
                        // Look for a line with this UUID
                        foreach (var line in mdStatInfo.Output.Split('\n'))
                        {
                            if (line.Contains(mdadmUuid, StringComparison.OrdinalIgnoreCase))
                            {
                                var parts = line.Split(new[] { ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length > 0 && parts[0].StartsWith("md"))
                                {
                                    deviceName = parts[0];
                                    break;
                                }
                            }
                        }
                    }
                    
                    if (deviceName != null)
                    {
                        devicePath = $"/dev/{deviceName}";
                    }
                }
                
                // Stop the RAID array
                if (File.Exists(devicePath))
                {
                    string stopCommand = $"mdadm --stop {devicePath}";
                    var stopResult = await _commandService.ExecuteCommandAsync(stopCommand, true);
                    
                    if (!stopResult.Success)
                    {
                        _logger.LogWarning("Failed to stop RAID array: {Error}", stopResult.Error);
                        // Continue anyway to clean up metadata
                    }
                }
                
                // Zero the superblocks on all member drives
                foreach (var driveEntry in metadata.Drives)
                {
                    string diskIdPath = driveEntry.Value.DiskIdPath;
                    string zeroCommand = $"mdadm --zero-superblock {diskIdPath}";
                    var zeroResult = await _commandService.ExecuteCommandAsync(zeroCommand, true);
                    
                    if (!zeroResult.Success)
                    {
                        _logger.LogWarning("Failed to zero superblock on drive {DiskIdPath}: {Error}", 
                            diskIdPath, zeroResult.Error);
                        // Continue to clean up other drives
                    }
                }
                
                // Remove pool metadata
                var removeResult = await _poolMetadataService.RemovePoolMetadataAsync(poolGroupGuid);
                if (!removeResult.Success)
                {
                    return Result<CommandResponse>.Error($"Failed to remove pool metadata: {removeResult.ErrorMessage}");
                }
                
                _logger.LogInformation("Successfully removed pool {PoolGroupGuid}", poolGroupGuid);
                
                return Result<CommandResponse>.Success(new CommandResponse
                {
                    Success = true,
                    Message = $"Pool {poolGroupGuid} removed successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing pool {PoolGroupGuid}", poolGroupGuid);
                return Result<CommandResponse>.Error($"Failed to remove pool: {ex.Message}");
            }
        }
    }
}