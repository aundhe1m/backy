using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Backy.Agent.Models;
using Backy.Agent.Services.Core;

namespace Backy.Agent.Services.Storage.Drives
{
    /// <summary>
    /// Interface for operations on physical drives in the system.
    /// </summary>
    /// <remarks>
    /// This service is responsible for:
    /// - Getting aggregated drive information
    /// - Performing drive operations like power management
    /// - Retrieving drive health and status information
    /// - Formatting drives for use in storage pools
    /// - Power state management of drives
    /// 
    /// Provides a layer of abstraction over the underlying system commands
    /// needed to perform drive operations.
    /// </remarks>
    public interface IDriveService
    {
        /// <summary>
        /// Gets all drives in the system
        /// </summary>
        /// <param name="includeDetails">Whether to include detailed information for each drive</param>
        /// <param name="useCache">Whether to use cached information</param>
        /// <returns>A collection of drives with their information</returns>
        Task<Result<IEnumerable<DriveInfo>>> GetDrivesAsync(bool includeDetails = false, bool useCache = true);
        
        /// <summary>
        /// Gets detailed information about a specific drive
        /// </summary>
        /// <param name="diskIdName">The disk ID name</param>
        /// <param name="useCache">Whether to use cached information</param>
        /// <returns>Detailed drive information</returns>
        Task<Result<DriveDetailInfo>> GetDriveDetailsAsync(string diskIdName, bool useCache = true);
        
        /// <summary>
        /// Gets the status of a drive (health, activity, etc.)
        /// </summary>
        /// <param name="diskIdName">The disk ID name</param>
        /// <returns>Drive status information</returns>
        Task<Result<DriveStatus>> GetDriveStatusAsync(string diskIdName);
        
        /// <summary>
        /// Refreshes drive information for all drives or a specific drive
        /// </summary>
        /// <param name="diskIdName">The disk ID name, or null for all drives</param>
        /// <param name="force">Whether to force a refresh even if one is already in progress</param>
        /// <returns>True if the refresh was successful</returns>
        Task<Result<bool>> RefreshDrivesAsync(string? diskIdName = null, bool force = false);
        
        /// <summary>
        /// Formats a drive with a specified filesystem
        /// </summary>
        /// <param name="request">The format request details</param>
        /// <returns>Result of the format operation</returns>
        Task<Result<CommandResponse>> FormatDriveAsync(DriveFormatRequest request);
        
        /// <summary>
        /// Sets drive power management settings
        /// </summary>
        /// <param name="diskIdName">The disk ID name</param>
        /// <param name="settings">The power management settings</param>
        /// <returns>Result of the operation</returns>
        Task<Result<CommandResponse>> SetDrivePowerManagementAsync(string diskIdName, DrivePowerSettings settings);
        
        /// <summary>
        /// Spins down a drive (puts it into standby mode)
        /// </summary>
        /// <param name="diskIdName">The disk ID name</param>
        /// <returns>Result of the operation</returns>
        Task<Result<CommandResponse>> SpinDownDriveAsync(string diskIdName);
        
        /// <summary>
        /// Spins up a drive (brings it out of standby mode)
        /// </summary>
        /// <param name="diskIdName">The disk ID name</param>
        /// <returns>Result of the operation</returns>
        Task<Result<CommandResponse>> SpinUpDriveAsync(string diskIdName);
    }

    /// <summary>
    /// Provides high-level drive operations that combines information retrieval and management.
    /// </summary>
    /// <remarks>
    /// This service:
    /// - Acts as a facade over lower-level drive services
    /// - Implements the business logic for drive operations
    /// - Manages protected drive status
    /// - Provides result objects with appropriate error information
    /// - Enforces validation and safety rules for drive operations
    /// 
    /// Delegates to specialized services for implementation details while providing
    /// a simplified API for consumers.
    /// </remarks>
    public class DriveService : IDriveService
    {
        private readonly ILogger<DriveService> _logger;
        private readonly ISystemCommandService _commandService;
        private readonly IDriveInfoService _driveInfoService;
        private readonly IDriveMonitoringService _driveMonitoringService;
        private readonly IMdStatReader _mdStatReader;
        private readonly AgentSettings _settings;
        private readonly IMountInfoReader _mountInfoReader;
        
        public DriveService(
            ILogger<DriveService> logger,
            ISystemCommandService commandService,
            IDriveInfoService driveInfoService,
            IDriveMonitoringService driveMonitoringService,
            IMdStatReader mdStatReader,
            IOptions<AgentSettings> options,
            IMountInfoReader mountInfoReader)
        {
            _logger = logger;
            _commandService = commandService;
            _driveInfoService = driveInfoService;
            _driveMonitoringService = driveMonitoringService;
            _mdStatReader = mdStatReader;
            _settings = options.Value;
            _mountInfoReader = mountInfoReader;
        }
        
        /// <inheritdoc />
        public async Task<Result<IEnumerable<DriveInfo>>> GetDrivesAsync(bool includeDetails = false, bool useCache = true)
        {
            try
            {
                // Get basic drive information from the drive info service
                var drives = await _driveInfoService.GetAllDrivesAsync(useCache);
                
                if (!includeDetails)
                {
                    return Result<IEnumerable<DriveInfo>>.Success(drives);
                }
                
                // If details are requested, get them for each drive
                var drivesWithDetails = new List<DriveInfo>();
                foreach (var drive in drives)
                {
                    var detailResult = await GetDriveDetailsAsync(drive.DiskIdName, useCache);
                    if (detailResult.Success && detailResult.Data != null)
                    {
                        drivesWithDetails.Add(detailResult.Data);
                    }
                    else
                    {
                        // If we can't get details, use the basic info
                        drivesWithDetails.Add(drive);
                    }
                }
                
                return Result<IEnumerable<DriveInfo>>.Success(drivesWithDetails);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting drives");
                return Result<IEnumerable<DriveInfo>>.Error($"Error getting drives: {ex.Message}");
            }
        }
        
        /// <inheritdoc />
        public async Task<Result<DriveDetailInfo>> GetDriveDetailsAsync(string diskIdName, bool useCache = true)
        {
            try
            {
                // Get detailed drive information from the drive info service
                var driveDetail = await _driveInfoService.GetDetailedDriveInfoAsync(diskIdName, useCache);
                
                if (driveDetail == null)
                {
                    return Result<DriveDetailInfo>.Error($"Drive not found: {diskIdName}");
                }
                
                return Result<DriveDetailInfo>.Success(driveDetail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting drive details for {DiskIdName}", diskIdName);
                return Result<DriveDetailInfo>.Error($"Error getting drive details: {ex.Message}");
            }
        }
        
        /// <inheritdoc />
        public async Task<Result<DriveStatus>> GetDriveStatusAsync(string diskIdName)
        {
            try
            {
                // Get the basic drive info
                var driveInfo = await _driveInfoService.GetDriveByDiskIdNameAsync(diskIdName);
                if (driveInfo == null)
                {
                    return Result<DriveStatus>.Error($"Drive not found: {diskIdName}");
                }
                
                // Check if the drive is in use
                bool inUse = await _driveInfoService.IsDriveInUseAsync(diskIdName);
                
                // Get SMART status
                var driveDetail = await _driveInfoService.GetDetailedDriveInfoAsync(diskIdName);
                bool isHealthy = driveDetail?.SmartStatus ?? true; // Assume healthy if no SMART data
                
                // Check if part of a RAID array
                bool inRaidArray = false;
                var mdStatInfo = await _mdStatReader.GetMdStatInfoAsync();
                foreach (var array in mdStatInfo.Arrays.Values)
                {
                    if (array.Devices.Contains(driveInfo.DeviceName))
                    {
                        inRaidArray = true;
                        break;
                    }
                }
                
                // Check power status (standby or active)
                bool isStandby = await IsDriveInStandbyAsync(driveInfo.DevicePath);
                
                // Create the status object
                var status = new DriveStatus
                {
                    DiskIdName = diskIdName,
                    DevicePath = driveInfo.DevicePath,
                    IsHealthy = isHealthy,
                    InUse = inUse,
                    InRaidArray = inRaidArray,
                    IsStandby = isStandby,
                    LastChecked = DateTime.UtcNow
                };
                
                return Result<DriveStatus>.Success(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting drive status for {DiskIdName}", diskIdName);
                return Result<DriveStatus>.Error($"Error getting drive status: {ex.Message}");
            }
        }
        
        /// <inheritdoc />
        public async Task<Result<bool>> RefreshDrivesAsync(string? diskIdName = null, bool force = false)
        {
            try
            {
                if (string.IsNullOrEmpty(diskIdName))
                {
                    // Refresh all drives
                    bool refreshed = await _driveInfoService.GetAllDrivesAsync(false) != null;
                    return Result<bool>.Success(refreshed);
                }
                else
                {
                    // Refresh a specific drive
                    _driveInfoService.InvalidateDriveCache(diskIdName);
                    var drive = await _driveInfoService.GetDriveByDiskIdNameAsync(diskIdName, false);
                    
                    if (drive == null)
                    {
                        return Result<bool>.Error($"Drive not found: {diskIdName}");
                    }
                    
                    return Result<bool>.Success(true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing drives");
                return Result<bool>.Error($"Error refreshing drives: {ex.Message}");
            }
        }
        
        /// <inheritdoc />
        public async Task<Result<CommandResponse>> FormatDriveAsync(DriveFormatRequest request)
        {
            try
            {
                // Validate request
                if (string.IsNullOrEmpty(request.DiskIdName))
                {
                    return Result<CommandResponse>.Error("Disk ID name is required");
                }
                
                if (string.IsNullOrEmpty(request.FilesystemType))
                {
                    return Result<CommandResponse>.Error("Filesystem type is required");
                }
                
                // Get the drive info
                var driveInfo = await _driveInfoService.GetDriveByDiskIdNameAsync(request.DiskIdName);
                if (driveInfo == null)
                {
                    return Result<CommandResponse>.Error($"Drive not found: {request.DiskIdName}");
                }
                
                // Check if the drive is in use
                bool inUse = await _driveInfoService.IsDriveInUseAsync(request.DiskIdName);
                if (inUse && !request.Force)
                {
                    return Result<CommandResponse>.Error($"Drive is in use: {request.DiskIdName}. Use 'Force' option to override");
                }
                
                // Build the format command based on the filesystem type
                string formatCommand;
                switch (request.FilesystemType.ToLower())
                {
                    case "ext4":
                        formatCommand = $"mkfs.ext4 -F {(request.Label != null ? $"-L {request.Label}" : "")} {driveInfo.DevicePath}";
                        break;
                    case "xfs":
                        formatCommand = $"mkfs.xfs -f {(request.Label != null ? $"-L {request.Label}" : "")} {driveInfo.DevicePath}";
                        break;
                    case "btrfs":
                        formatCommand = $"mkfs.btrfs -f {(request.Label != null ? $"-L {request.Label}" : "")} {driveInfo.DevicePath}";
                        break;
                    default:
                        return Result<CommandResponse>.Error($"Unsupported filesystem type: {request.FilesystemType}");
                }
                
                // Execute the format command
                _logger.LogInformation("Formatting drive {DiskIdName} ({DevicePath}) with {FilesystemType}", 
                    request.DiskIdName, driveInfo.DevicePath, request.FilesystemType);
                
                var result = await _commandService.ExecuteCommandAsync(formatCommand, true);
                
                if (!result.Success)
                {
                    return Result<CommandResponse>.Error($"Format failed: {result.Error}");
                }
                
                // Refresh drive information
                _driveInfoService.InvalidateDriveCache(request.DiskIdName);
                
                return Result<CommandResponse>.Success(new CommandResponse
                {
                    Success = true,
                    Message = $"Drive {request.DiskIdName} ({driveInfo.DevicePath}) formatted with {request.FilesystemType}",
                    CommandOutput = result.Output
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error formatting drive {DiskIdName}", request.DiskIdName);
                return Result<CommandResponse>.Error($"Error formatting drive: {ex.Message}");
            }
        }
        
        /// <inheritdoc />
        public async Task<Result<CommandResponse>> SetDrivePowerManagementAsync(string diskIdName, DrivePowerSettings settings)
        {
            try
            {
                // Get the drive info
                var driveInfo = await _driveInfoService.GetDriveByDiskIdNameAsync(diskIdName);
                if (driveInfo == null)
                {
                    return Result<CommandResponse>.Error($"Drive not found: {diskIdName}");
                }
                
                // Build the hdparm command
                List<string> hdparmOptions = new();
                
                if (settings.StandbyTimeout.HasValue)
                {
                    // Convert minutes to hdparm's format (multiples of 5 seconds)
                    int standbyTimeoutInFiveSecondUnits = (int)(settings.StandbyTimeout.Value * 60 / 5);
                    hdparmOptions.Add($"-S {standbyTimeoutInFiveSecondUnits}");
                }
                
                if (settings.AdvancedPowerManagement.HasValue)
                {
                    // APM values range from 1-255
                    hdparmOptions.Add($"-B {settings.AdvancedPowerManagement.Value}");
                }
                
                if (settings.DisableAcousticManagement.HasValue && settings.DisableAcousticManagement.Value)
                {
                    hdparmOptions.Add("-M 254"); // Disable acoustic management
                }
                
                if (settings.SpindownTimeout.HasValue)
                {
                    // Spindown timeout in multiples of 5 seconds
                    int spindownTimeoutInFiveSecondUnits = (int)(settings.SpindownTimeout.Value * 60 / 5);
                    hdparmOptions.Add($"-S {spindownTimeoutInFiveSecondUnits}");
                }
                
                if (hdparmOptions.Count == 0)
                {
                    return Result<CommandResponse>.Error("No power management settings specified");
                }
                
                // Execute the hdparm command
                string hdparmCommand = $"hdparm {string.Join(" ", hdparmOptions)} {driveInfo.DevicePath}";
                _logger.LogInformation("Setting power management for drive {DiskIdName} ({DevicePath})", 
                    diskIdName, driveInfo.DevicePath);
                
                var result = await _commandService.ExecuteCommandAsync(hdparmCommand, true);
                
                if (!result.Success)
                {
                    return Result<CommandResponse>.Error($"Setting power management failed: {result.Error}");
                }
                
                return Result<CommandResponse>.Success(new CommandResponse
                {
                    Success = true,
                    Message = $"Power management settings applied to {diskIdName} ({driveInfo.DevicePath})",
                    CommandOutput = result.Output
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting power management for drive {DiskIdName}", diskIdName);
                return Result<CommandResponse>.Error($"Error setting power management: {ex.Message}");
            }
        }
        
        /// <inheritdoc />
        public async Task<Result<CommandResponse>> SpinDownDriveAsync(string diskIdName)
        {
            try
            {
                // Get the drive info
                var driveInfo = await _driveInfoService.GetDriveByDiskIdNameAsync(diskIdName);
                if (driveInfo == null)
                {
                    return Result<CommandResponse>.Error($"Drive not found: {diskIdName}");
                }
                
                // Check if the drive is in use
                bool inUse = await _driveInfoService.IsDriveInUseAsync(diskIdName);
                if (inUse)
                {
                    return Result<CommandResponse>.Error($"Cannot spin down drive {diskIdName} because it is in use");
                }
                
                // Execute the hdparm command to spin down the drive
                string spindownCommand = $"hdparm -y {driveInfo.DevicePath}";
                _logger.LogInformation("Spinning down drive {DiskIdName} ({DevicePath})", 
                    diskIdName, driveInfo.DevicePath);
                
                var result = await _commandService.ExecuteCommandAsync(spindownCommand, true);
                
                if (!result.Success)
                {
                    return Result<CommandResponse>.Error($"Spinning down drive failed: {result.Error}");
                }
                
                return Result<CommandResponse>.Success(new CommandResponse
                {
                    Success = true,
                    Message = $"Drive {diskIdName} ({driveInfo.DevicePath}) spun down",
                    CommandOutput = result.Output
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error spinning down drive {DiskIdName}", diskIdName);
                return Result<CommandResponse>.Error($"Error spinning down drive: {ex.Message}");
            }
        }
        
        /// <inheritdoc />
        public async Task<Result<CommandResponse>> SpinUpDriveAsync(string diskIdName)
        {
            try
            {
                // Get the drive info
                var driveInfo = await _driveInfoService.GetDriveByDiskIdNameAsync(diskIdName);
                if (driveInfo == null)
                {
                    return Result<CommandResponse>.Error($"Drive not found: {diskIdName}");
                }
                
                // Execute a command that will cause the drive to spin up
                // Reading from the drive is usually enough to wake it
                string wakeCommand = $"dd if={driveInfo.DevicePath} of=/dev/null bs=512 count=1";
                _logger.LogInformation("Spinning up drive {DiskIdName} ({DevicePath})", 
                    diskIdName, driveInfo.DevicePath);
                
                var result = await _commandService.ExecuteCommandAsync(wakeCommand, true);
                
                // Check if the drive is still in standby mode
                bool isStandby = await IsDriveInStandbyAsync(driveInfo.DevicePath);
                
                if (isStandby)
                {
                    return Result<CommandResponse>.Error("Failed to spin up drive, still in standby mode");
                }
                
                return Result<CommandResponse>.Success(new CommandResponse
                {
                    Success = true,
                    Message = $"Drive {diskIdName} ({driveInfo.DevicePath}) spun up",
                    CommandOutput = result.Output
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error spinning up drive {DiskIdName}", diskIdName);
                return Result<CommandResponse>.Error($"Error spinning up drive: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Checks if a drive is in standby mode
        /// </summary>
        private async Task<bool> IsDriveInStandbyAsync(string devicePath)
        {
            try
            {
                // Use hdparm to check the drive status
                var result = await _commandService.ExecuteCommandAsync($"hdparm -C {devicePath}");
                
                if (!result.Success)
                {
                    _logger.LogWarning("Failed to check drive standby status: {Error}", result.Error);
                    return false; // Assume not in standby if we can't check
                }
                
                // Check if the output contains "standby" or "sleeping"
                return result.Output.Contains("standby") || result.Output.Contains("sleeping");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking drive standby status for {DevicePath}", devicePath);
                return false; // Assume not in standby if there's an error
            }
        }
    }
    
    /// <summary>
    /// Request parameters for formatting a drive
    /// </summary>
    public class DriveFormatRequest
    {
        /// <summary>
        /// The disk ID name of the drive to format
        /// </summary>
        public string DiskIdName { get; set; } = string.Empty;
        
        /// <summary>
        /// The filesystem type to format the drive with (ext4, xfs, btrfs, etc.)
        /// </summary>
        public string FilesystemType { get; set; } = string.Empty;
        
        /// <summary>
        /// Optional label for the filesystem
        /// </summary>
        public string? Label { get; set; }
        
        /// <summary>
        /// Whether to force the format even if the drive is in use
        /// </summary>
        public bool Force { get; set; }
    }
    
    /// <summary>
    /// Drive power management settings
    /// </summary>
    public class DrivePowerSettings
    {
        /// <summary>
        /// Standby timeout in minutes (0 to disable)
        /// </summary>
        public int? StandbyTimeout { get; set; }
        
        /// <summary>
        /// Advanced Power Management value (1-255)
        /// 1-127: Maximum power savings, but lowest performance
        /// 128-254: Intermediate power management with good performance
        /// 255: Maximum performance, but no power management
        /// </summary>
        public int? AdvancedPowerManagement { get; set; }
        
        /// <summary>
        /// Whether to disable acoustic management
        /// </summary>
        public bool? DisableAcousticManagement { get; set; }
        
        /// <summary>
        /// Spindown timeout in minutes (0 to disable)
        /// </summary>
        public int? SpindownTimeout { get; set; }
    }
    
    /// <summary>
    /// Status information for a drive
    /// </summary>
    public class DriveStatus
    {
        /// <summary>
        /// The disk ID name
        /// </summary>
        public string DiskIdName { get; set; } = string.Empty;
        
        /// <summary>
        /// The device path
        /// </summary>
        public string DevicePath { get; set; } = string.Empty;
        
        /// <summary>
        /// Whether the drive is healthy according to SMART status
        /// </summary>
        public bool IsHealthy { get; set; }
        
        /// <summary>
        /// Whether the drive is in use (mounted or part of a RAID array)
        /// </summary>
        public bool InUse { get; set; }
        
        /// <summary>
        /// Whether the drive is part of a RAID array
        /// </summary>
        public bool InRaidArray { get; set; }
        
        /// <summary>
        /// Whether the drive is in standby mode
        /// </summary>
        public bool IsStandby { get; set; }
        
        /// <summary>
        /// When the status was last checked
        /// </summary>
        public DateTime LastChecked { get; set; }
    }
}