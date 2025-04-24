using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Backy.Agent.Models;
using Backy.Agent.Services.Core;

namespace Backy.Agent.Services.Storage.Drives
{
    /// <summary>
    /// Interface for providing information about physical drives in the system.
    /// </summary>
    /// <remarks>
    /// This service is responsible for:
    /// - Getting information about all drives in the system
    /// - Looking up specific drives by ID, serial, or path
    /// - Getting detailed information about drives including SMART data
    /// - Checking if drives are in use
    /// - Managing caching of drive information
    /// 
    /// Provides clear separation between cached vs. fresh data retrieval.
    /// </remarks>
    public interface IDriveInfoService
    {
        /// <summary>
        /// Gets information about all drives in the system
        /// </summary>
        /// <param name="useCache">Whether to use cached information</param>
        /// <returns>A collection of DriveInfo objects</returns>
        Task<IEnumerable<DriveInfo>> GetAllDrivesAsync(bool useCache = true);
        
        /// <summary>
        /// Gets information about a specific drive by disk ID name
        /// </summary>
        /// <param name="diskIdName">The disk ID name (e.g., 'scsi-SATA_WDC_WD80EFAX-68K_1234567')</param>
        /// <param name="useCache">Whether to use cached information</param>
        /// <returns>DriveInfo for the specified disk, or null if not found</returns>
        Task<DriveInfo?> GetDriveByDiskIdNameAsync(string diskIdName, bool useCache = true);
        
        /// <summary>
        /// Gets information about a specific drive by serial number
        /// </summary>
        /// <param name="serial">The drive serial number</param>
        /// <param name="useCache">Whether to use cached information</param>
        /// <returns>DriveInfo for the specified serial, or null if not found</returns>
        Task<DriveInfo?> GetDriveBySerialAsync(string serial, bool useCache = true);
        
        /// <summary>
        /// Gets information about a specific drive by device path
        /// </summary>
        /// <param name="devicePath">The device path (e.g., '/dev/sda')</param>
        /// <param name="useCache">Whether to use cached information</param>
        /// <returns>DriveInfo for the specified device path, or null if not found</returns>
        Task<DriveInfo?> GetDriveByDevicePathAsync(string devicePath, bool useCache = true);
        
        /// <summary>
        /// Gets detailed information about a drive including SMART data
        /// </summary>
        /// <param name="diskIdName">The disk ID name</param>
        /// <param name="useCache">Whether to use cached information</param>
        /// <returns>Detailed drive information, or null if not found</returns>
        Task<DriveDetailInfo?> GetDetailedDriveInfoAsync(string diskIdName, bool useCache = true);
        
        /// <summary>
        /// Checks if a drive is in use (mounted or part of a RAID array)
        /// </summary>
        /// <param name="diskIdName">The disk ID name</param>
        /// <returns>True if the drive is in use, false otherwise</returns>
        Task<bool> IsDriveInUseAsync(string diskIdName);
        
        /// <summary>
        /// Invalidates the cache for a specific drive
        /// </summary>
        /// <param name="diskIdName">The disk ID name</param>
        void InvalidateDriveCache(string diskIdName);
        
        /// <summary>
        /// Invalidates the entire drive cache
        /// </summary>
        void InvalidateAllDriveCache();
    }

    /// <summary>
    /// Service for providing information about physical drives in the system.
    /// </summary>
    /// <remarks>
    /// This service:
    /// - Utilizes the DriveMonitoringService for drive mapping
    /// - Provides detailed drive information by running targeted commands
    /// - Caches static drive information for improved performance
    /// - Checks drive usage status in the system
    /// - Manages cache invalidation based on system changes
    /// 
    /// Efficiently retrieves information by using the drive mapping 
    /// maintained by the monitoring service.
    /// </remarks>
    public class DriveInfoService : IDriveInfoService
    {
        private readonly ILogger<DriveInfoService> _logger;
        private readonly IDriveMonitoringService _monitoringService;
        private readonly ISystemCommandService _commandService;
        private readonly IMountInfoReader _mountInfoReader;
        private readonly IMdStatReader _mdStatReader;
        private readonly IMemoryCache _cache;
        private readonly AgentSettings _settings;
        
        // Cache keys
        private const string DRIVE_DETAIL_CACHE_PREFIX = "DriveDetail:";
        
        public DriveInfoService(
            ILogger<DriveInfoService> logger,
            IDriveMonitoringService monitoringService,
            ISystemCommandService commandService,
            IMountInfoReader mountInfoReader,
            IMdStatReader mdStatReader,
            IMemoryCache cache,
            IOptions<AgentSettings> options)
        {
            _logger = logger;
            _monitoringService = monitoringService;
            _commandService = commandService;
            _mountInfoReader = mountInfoReader;
            _mdStatReader = mdStatReader;
            _cache = cache;
            _settings = options.Value;
            
            // Subscribe to drive change events
            _monitoringService.DriveChanged += OnDriveChanged;
        }
        
        /// <inheritdoc />
        public Task<IEnumerable<DriveInfo>> GetAllDrivesAsync(bool useCache = true)
        {
            // Get the current drive mapping
            var mapping = _monitoringService.GetDriveMapping();
            
            // If the mapping is empty or not using cache, force a refresh
            if (mapping.DiskIdNameToDrive.Count == 0 || !useCache)
            {
                bool refreshed = _monitoringService.RefreshDrivesAsync().Result;
                if (!refreshed)
                {
                    _logger.LogWarning("Failed to refresh drive mapping");
                    return Task.FromResult<IEnumerable<DriveInfo>>(new List<DriveInfo>());
                }
                
                // Get the updated mapping
                mapping = _monitoringService.GetDriveMapping();
            }
            
            // Return all drives from the mapping
            return Task.FromResult<IEnumerable<DriveInfo>>(mapping.DiskIdNameToDrive.Values);
        }
        
        /// <inheritdoc />
        public async Task<DriveInfo?> GetDriveByDiskIdNameAsync(string diskIdName, bool useCache = true)
        {
            // Get the current drive mapping
            var mapping = _monitoringService.GetDriveMapping();
            
            // Check if the drive is in the mapping
            if (useCache && mapping.DiskIdNameToDrive.TryGetValue(diskIdName, out var cachedInfo))
            {
                return cachedInfo;
            }
            
            // If not using cache or not in the mapping, force a refresh
            bool refreshed = await _monitoringService.RefreshDrivesAsync();
            if (!refreshed)
            {
                _logger.LogWarning("Failed to refresh drive mapping");
                return null;
            }
            
            // Get the updated mapping
            mapping = _monitoringService.GetDriveMapping();
            
            // Check if the drive is in the updated mapping
            if (mapping.DiskIdNameToDrive.TryGetValue(diskIdName, out var driveInfo))
            {
                return driveInfo;
            }
            
            return null;
        }
        
        /// <inheritdoc />
        public async Task<DriveInfo?> GetDriveBySerialAsync(string serial, bool useCache = true)
        {
            // Get the current drive mapping
            var mapping = _monitoringService.GetDriveMapping();
            
            // Check if the serial is in the mapping
            if (useCache && mapping.SerialToDiskIdName.TryGetValue(serial, out var diskIdName))
            {
                // Get the drive by disk ID name
                if (mapping.DiskIdNameToDrive.TryGetValue(diskIdName, out var cachedInfo))
                {
                    return cachedInfo;
                }
            }
            
            // If not using cache or not in the mapping, force a refresh
            bool refreshed = await _monitoringService.RefreshDrivesAsync();
            if (!refreshed)
            {
                _logger.LogWarning("Failed to refresh drive mapping");
                return null;
            }
            
            // Get the updated mapping
            mapping = _monitoringService.GetDriveMapping();
            
            // Check if the serial is in the updated mapping
            if (mapping.SerialToDiskIdName.TryGetValue(serial, out var updatedDiskIdName))
            {
                // Get the drive by disk ID name
                if (mapping.DiskIdNameToDrive.TryGetValue(updatedDiskIdName, out var driveInfo))
                {
                    return driveInfo;
                }
            }
            
            return null;
        }
        
        /// <inheritdoc />
        public async Task<DriveInfo?> GetDriveByDevicePathAsync(string devicePath, bool useCache = true)
        {
            // Get the current drive mapping
            var mapping = _monitoringService.GetDriveMapping();
            
            // Check if the device path is in the mapping
            if (useCache && mapping.DevicePathToDiskIdName.TryGetValue(devicePath, out var diskIdName))
            {
                // Get the drive by disk ID name
                if (mapping.DiskIdNameToDrive.TryGetValue(diskIdName, out var cachedInfo))
                {
                    return cachedInfo;
                }
            }
            
            // If not using cache or not in the mapping, force a refresh
            bool refreshed = await _monitoringService.RefreshDrivesAsync();
            if (!refreshed)
            {
                _logger.LogWarning("Failed to refresh drive mapping");
                return null;
            }
            
            // Get the updated mapping
            mapping = _monitoringService.GetDriveMapping();
            
            // Check if the device path is in the updated mapping
            if (mapping.DevicePathToDiskIdName.TryGetValue(devicePath, out var updatedDiskIdName))
            {
                // Get the drive by disk ID name
                if (mapping.DiskIdNameToDrive.TryGetValue(updatedDiskIdName, out var driveInfo))
                {
                    return driveInfo;
                }
            }
            
            return null;
        }
        
        /// <inheritdoc />
        public async Task<DriveDetailInfo?> GetDetailedDriveInfoAsync(string diskIdName, bool useCache = true)
        {
            // Check cache first if requested
            string cacheKey = $"{DRIVE_DETAIL_CACHE_PREFIX}{diskIdName}";
            if (useCache && _cache.TryGetValue(cacheKey, out DriveDetailInfo? cachedInfo) && cachedInfo != null)
            {
                _logger.LogDebug("Retrieved detailed drive info from cache for {DiskIdName}", diskIdName);
                return cachedInfo;
            }
            
            // Get the basic drive info
            var basicInfo = await GetDriveByDiskIdNameAsync(diskIdName, useCache);
            if (basicInfo == null)
            {
                _logger.LogWarning("Drive not found: {DiskIdName}", diskIdName);
                return null;
            }
            
            // Create a detail info object from the basic info
            var detailInfo = DriveDetailInfo.FromBasicInfo(basicInfo);
            
            try
            {
                // Run smartctl to get detailed information
                var result = await _commandService.ExecuteCommandAsync($"smartctl -j -a {basicInfo.DevicePath}");
                if (result.Success)
                {
                    try
                    {
                        // Parse the JSON output
                        var smartData = System.Text.Json.JsonSerializer.Deserialize<SmartctlOutput>(result.Output);
                        if (smartData != null)
                        {
                            // Extract model information
                            detailInfo.ModelFamily = smartData.ModelFamily ?? string.Empty;
                            detailInfo.ModelName = smartData.ModelName ?? string.Empty;
                            detailInfo.FirmwareVersion = smartData.FirmwareVersion ?? string.Empty;
                            
                            // Extract capacity information if available
                            if (smartData.UserCapacity != null)
                            {
                                detailInfo.Capacity = smartData.UserCapacity.Bytes ?? 0;
                            }
                            
                            // Extract SMART health status
                            detailInfo.SmartStatus = smartData.SmartStatus?.Passed ?? false;
                            
                            // Extract power-on hours if available
                            if (smartData.Attributes?.Attributes != null)
                            {
                                var powerOnHoursAttr = smartData.Attributes.Attributes
                                    .FirstOrDefault(a => a.Name?.Contains("Power_On_Hours") == true);
                                
                                if (powerOnHoursAttr != null && powerOnHoursAttr.Value != null)
                                {
                                    detailInfo.PowerOnHours = powerOnHoursAttr.Value.Value;
                                }
                                
                                // Temperature information
                                var tempAttr = smartData.Attributes.Attributes
                                    .FirstOrDefault(a => a.Name?.Contains("Temperature") == true);
                                
                                if (tempAttr != null && tempAttr.Value != null)
                                {
                                    detailInfo.Temperature = tempAttr.Value.Value;
                                }
                            }
                            
                            // Store temperature from smartctl temperature value if attributes didn't have it
                            if (detailInfo.Temperature == 0 && smartData.Temperature != null)
                            {
                                detailInfo.Temperature = smartData.Temperature.Current;
                            }
                        }
                    }
                    catch (System.Text.Json.JsonException ex)
                    {
                        _logger.LogError(ex, "Failed to parse smartctl output for {DiskIdName}", diskIdName);
                    }
                }
                else
                {
                    _logger.LogWarning("Failed to run smartctl for {DiskIdName}: {Error}", 
                        diskIdName, result.Error);
                }
                
                // Check if drive is in use by an MD array
                var mdStatInfo = await _mdStatReader.GetMdStatInfoAsync();
                foreach (var array in mdStatInfo.Arrays.Values)
                {
                    if (array.Devices.Contains(basicInfo.DeviceName))
                    {
                        detailInfo.IsPartOfRaidArray = true;
                        detailInfo.RaidArrayNames.Add(array.DeviceName);
                    }
                }
                
                // Cache the result with expiration based on settings
                _cache.Set(
                    cacheKey,
                    detailInfo,
                    new MemoryCacheEntryOptions().SetAbsoluteExpiration(
                        TimeSpan.FromSeconds(_settings.SmartDataCacheTimeToLiveSeconds)
                    )
                );
                
                return detailInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting detailed drive info for {DiskIdName}", diskIdName);
                return DriveDetailInfo.FromBasicInfo(basicInfo);
            }
        }
        
        /// <inheritdoc />
        public async Task<bool> IsDriveInUseAsync(string diskIdName)
        {
            try
            {
                // Get the drive info
                var driveInfo = await GetDriveByDiskIdNameAsync(diskIdName);
                if (driveInfo == null)
                {
                    _logger.LogWarning("Drive not found: {DiskIdName}", diskIdName);
                    return false;
                }
                
                // Check if any partitions are mounted
                if (driveInfo.Partitions.Any(p => p.IsMounted))
                {
                    return true;
                }
                
                // Check if device itself is mounted
                var mountInfo = await _mountInfoReader.GetMountInfoByDeviceAsync(driveInfo.DevicePath);
                if (mountInfo != null)
                {
                    return true;
                }
                
                // Check if drive is in use by an MD array
                var mdStatInfo = await _mdStatReader.GetMdStatInfoAsync();
                foreach (var array in mdStatInfo.Arrays.Values)
                {
                    if (array.Devices.Contains(driveInfo.DeviceName))
                    {
                        return true;
                    }
                }
                
                // Not in use
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if drive is in use: {DiskIdName}", diskIdName);
                return false;
            }
        }
        
        /// <inheritdoc />
        public void InvalidateDriveCache(string diskIdName)
        {
            // Invalidate detailed drive info cache
            string cacheKey = $"{DRIVE_DETAIL_CACHE_PREFIX}{diskIdName}";
            _cache.Remove(cacheKey);
            
            _logger.LogDebug("Invalidated drive cache for {DiskIdName}", diskIdName);
        }
        
        /// <inheritdoc />
        public void InvalidateAllDriveCache()
        {
            // Force a refresh of the drive mapping
            _monitoringService.RefreshDrivesAsync(true);
            
            _logger.LogDebug("Invalidated all drive caches");
        }
        
        /// <summary>
        /// Handles drive change events and invalidates caches
        /// </summary>
        private void OnDriveChanged(object? sender, DriveChangeEventArgs e)
        {
            // If we know which drive changed, invalidate its cache
            if (!string.IsNullOrEmpty(e.Path))
            {
                InvalidateDriveCache(e.Path);
            }
            else
            {
                // Otherwise invalidate all drive caches
                InvalidateAllDriveCache();
            }
        }

        /// <inheritdoc />
        public async Task<IEnumerable<ProcessInfo>> GetProcessesUsingDriveAsync(string diskIdName)
        {
            try
            {
                // Get the drive info
                var driveInfo = await GetDriveByDiskIdNameAsync(diskIdName);
                if (driveInfo == null)
                {
                    _logger.LogWarning("Drive not found: {DiskIdName}", diskIdName);
                    return Enumerable.Empty<ProcessInfo>();
                }
                
                List<ProcessInfo> processes = new();
                
                // Check processes using the device itself
                var deviceProcesses = await GetProcessesUsingPathAsync(driveInfo.DevicePath);
                processes.AddRange(deviceProcesses);
                
                // Check processes using any partitions
                foreach (var partition in driveInfo.Partitions)
                {
                    var partitionProcesses = await GetProcessesUsingPathAsync(partition.Path);
                    processes.AddRange(partitionProcesses);
                    
                    // Also check mount points if mounted
                    if (partition.IsMounted && !string.IsNullOrEmpty(partition.MountPoint))
                    {
                        var mountPointProcesses = await GetProcessesUsingPathAsync(partition.MountPoint);
                        processes.AddRange(mountPointProcesses);
                    }
                }
                
                // Remove duplicates based on PID
                return processes
                    .GroupBy(p => p.PID)
                    .Select(g => g.First())
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting processes using drive: {DiskIdName}", diskIdName);
                return Enumerable.Empty<ProcessInfo>();
            }
        }
        
        /// <summary>
        /// Gets all processes using a given path
        /// </summary>
        private async Task<IEnumerable<ProcessInfo>> GetProcessesUsingPathAsync(string path)
        {
            try
            {
                // Run lsof to find processes using the path
                var result = await _commandService.ExecuteCommandAsync($"lsof -t {path}");
                if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
                {
                    return Enumerable.Empty<ProcessInfo>();
                }
                
                // Parse the PIDs from the output (one per line)
                var pids = result.Output
                    .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => int.TryParse(line.Trim(), out int pid) ? pid : -1)
                    .Where(pid => pid > 0)
                    .ToList();
                
                if (pids.Count == 0)
                {
                    return Enumerable.Empty<ProcessInfo>();
                }
                
                // Get detailed process info for each PID
                List<ProcessInfo> processes = new();
                foreach (int pid in pids)
                {
                    // Get command and user for the process
                    var cmdResult = await _commandService.ExecuteCommandAsync($"ps -o cmd=,user= -p {pid}");
                    if (cmdResult.Success && !string.IsNullOrWhiteSpace(cmdResult.Output))
                    {
                        string[] parts = cmdResult.Output.Trim().Split(new[] { ' ' }, 2);
                        string user = parts.Length > 1 ? parts[0] : string.Empty;
                        string command = parts.Length > 1 ? parts[1] : parts[0];
                        
                        processes.Add(new ProcessInfo
                        {
                            PID = pid,
                            Command = command,
                            User = user,
                            Path = path
                        });
                    }
                }
                
                return processes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting processes using path: {Path}", path);
                return Enumerable.Empty<ProcessInfo>();
            }
        }
    }
}