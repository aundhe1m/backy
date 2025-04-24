using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Backy.Agent.Models;
using Backy.Agent.Services.Core;

namespace Backy.Agent.Services.Storage.Drives
{
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
    /// Interface for monitoring drive changes in the system.
    /// </summary>
    /// <remarks>
    /// This service is responsible for:
    /// - Tracking when drives are added or removed from the system
    /// - Building and maintaining a mapping between different drive identifiers
    /// - Notifying subscribers when drive changes occur
    /// - Providing access to the current drive mapping
    /// 
    /// Uses event-based monitoring instead of timer-based polling for improved efficiency.
    /// </remarks>
    public interface IDriveMonitoringService
    {
        /// <summary>
        /// Event triggered when drives change (added, removed, or modified)
        /// </summary>
        event EventHandler<DriveChangeEventArgs>? DriveChanged;
        
        /// <summary>
        /// Initializes the drive mapping by scanning all drives
        /// </summary>
        /// <returns>True if initialization was successful</returns>
        Task<bool> InitializeDriveMapAsync();
        
        /// <summary>
        /// Refreshes the drive mapping by rescanning all drives
        /// </summary>
        /// <param name="force">Whether to force a refresh even if one is already in progress</param>
        /// <returns>True if refresh was successful or already in progress</returns>
        Task<bool> RefreshDrivesAsync(bool force = false);
        
        /// <summary>
        /// Gets the current drive mapping
        /// </summary>
        /// <returns>The current drive mapping</returns>
        DriveMapping GetDriveMapping();
        
        /// <summary>
        /// The time when drives were last refreshed
        /// </summary>
        DateTime LastRefreshTime { get; }
        
        /// <summary>
        /// Whether a refresh operation is currently in progress
        /// </summary>
        bool IsRefreshing { get; }
    }

    /// <summary>
    /// Background service that monitors drive changes in the system and maintains drive mapping.
    /// </summary>
    /// <remarks>
    /// This service:
    /// - Watches the /dev/disk/by-id directory for changes
    /// - Builds a comprehensive mapping between drive identifiers (path, serial, id)
    /// - Notifies subscribers when drives are added or removed
    /// - Provides the current state of all drives via mapping
    /// - Uses event-based monitoring instead of periodic polling
    /// 
    /// Replaces the timer-based approach with a more efficient event-based
    /// mechanism that only refreshes when actual changes occur.
    /// </remarks>
    public class DriveMonitoringService : BackgroundService, IDriveMonitoringService
    {
        private readonly ILogger<DriveMonitoringService> _logger;
        private readonly ISystemCommandService _commandService;
        private readonly IFileSystemInfoService _fileSystemInfoService;
        private readonly AgentSettings _settings;
        
        private readonly FileSystemWatcher _diskByIdWatcher = new();
        private readonly DriveMapping _driveMapping = new();
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        
        private const string DISK_BY_ID_DIR = "/dev/disk/by-id";
        
        // Track the last refresh time and status
        private DateTime _lastRefreshTime = DateTime.MinValue;
        private bool _isRefreshing = false;
        
        /// <inheritdoc />
        public event EventHandler<DriveChangeEventArgs>? DriveChanged;
        
        /// <inheritdoc />
        public DateTime LastRefreshTime => _lastRefreshTime;
        
        /// <inheritdoc />
        public bool IsRefreshing => _isRefreshing;
        
        public DriveMonitoringService(
            ILogger<DriveMonitoringService> logger,
            ISystemCommandService commandService,
            IFileSystemInfoService fileSystemInfoService,
            IOptions<AgentSettings> options)
        {
            _logger = logger;
            _commandService = commandService;
            _fileSystemInfoService = fileSystemInfoService;
            _settings = options.Value;
        }
        
        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("Starting drive monitoring service");
                
                // Initial mapping
                await InitializeDriveMapAsync();
                
                // Setup directory watcher
                if (Directory.Exists(DISK_BY_ID_DIR))
                {
                    _diskByIdWatcher.Path = DISK_BY_ID_DIR;
                    _diskByIdWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;
                    _diskByIdWatcher.Created += OnDiskByIdChanged;
                    _diskByIdWatcher.Deleted += OnDiskByIdChanged;
                    _diskByIdWatcher.Renamed += OnDiskByIdChanged;
                    _diskByIdWatcher.EnableRaisingEvents = true;
                    
                    _logger.LogInformation("Watching {Directory} for drive changes", DISK_BY_ID_DIR);
                }
                else
                {
                    _logger.LogWarning("{Directory} not found, falling back to periodic refresh", DISK_BY_ID_DIR);
                    
                    // Fall back to timer if directory doesn't exist
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                        await RefreshDrivesAsync();
                    }
                }
                
                // Wait until cancellation requested
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown, don't log exception
                _logger.LogInformation("Drive monitoring service stopping due to cancellation");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in drive monitoring service");
            }
            finally
            {
                // Clean up watcher
                _diskByIdWatcher.EnableRaisingEvents = false;
                _diskByIdWatcher.Created -= OnDiskByIdChanged;
                _diskByIdWatcher.Deleted -= OnDiskByIdChanged;
                _diskByIdWatcher.Renamed -= OnDiskByIdChanged;
                _diskByIdWatcher.Dispose();
            }
        }
        
        /// <inheritdoc />
        public async Task<bool> InitializeDriveMapAsync()
        {
            return await RefreshDrivesAsync(true);
        }
        
        /// <inheritdoc />
        public async Task<bool> RefreshDrivesAsync(bool force = false)
        {
            // Prevent concurrent refreshes unless forced
            if (_isRefreshing && !force)
            {
                _logger.LogDebug("Drive refresh already in progress, skipping");
                return true;
            }
            
            // Acquire lock to prevent concurrent refreshes
            if (!await _refreshLock.WaitAsync(0) && !force)
            {
                _logger.LogDebug("Drive refresh already locked, skipping");
                return true;
            }
            
            try
            {
                _isRefreshing = true;
                _logger.LogDebug("Refreshing drive mapping");
                
                // Clear existing mapping
                _driveMapping.Clear();
                
                // Get all drives using lsblk
                var result = await _commandService.ExecuteCommandAsync(
                    "lsblk -J -b -o NAME,SIZE,TYPE,MOUNTPOINT,UUID,SERIAL,FSTYPE,PATH"
                );
                
                if (!result.Success)
                {
                    _logger.LogError("Failed to get drive information: {Error}", result.Output);
                    return false;
                }
                
                // Parse the lsblk output
                try
                {
                    var lsblkOutput = System.Text.Json.JsonSerializer.Deserialize<LsblkOutput>(result.Output);
                    if (lsblkOutput == null || lsblkOutput.BlockDevices == null)
                    {
                        _logger.LogError("Failed to parse lsblk output");
                        return false;
                    }
                    
                    // Find physical disks (TYPE=disk)
                    var disks = lsblkOutput.BlockDevices
                        .Where(d => d.Type?.ToLower() == "disk")
                        .ToList();
                    
                    _logger.LogDebug("Found {Count} physical disks", disks.Count);
                    
                    // Build mapping for each disk
                    foreach (var disk in disks)
                    {
                        if (string.IsNullOrEmpty(disk.Path) || string.IsNullOrEmpty(disk.Name))
                        {
                            continue;
                        }
                        
                        string devicePath = disk.Path;
                        string deviceName = disk.Name;
                        
                        // Get disk ID links
                        if (Directory.Exists(DISK_BY_ID_DIR))
                        {
                            // Find all symlinks in /dev/disk/by-id that point to this device
                            var diskIdLinks = await GetDiskIdLinksForDeviceAsync(devicePath);
                            
                            foreach (string diskIdName in diskIdLinks)
                            {
                                // Skip some links we don't care about
                                if (diskIdName.StartsWith("wwn-") || diskIdName.StartsWith("dm-name-"))
                                {
                                    continue;
                                }
                                
                                // Create a DriveInfo object for this disk
                                var driveInfo = new DriveInfo
                                {
                                    DeviceName = deviceName,
                                    DevicePath = devicePath,
                                    DiskIdName = diskIdName,
                                    DiskIdPath = Path.Combine(DISK_BY_ID_DIR, diskIdName),
                                    Size = disk.Size,
                                    Serial = disk.Serial ?? string.Empty,
                                    IsBusy = false // We'll set this later
                                };
                                
                                // Check for partitions
                                if (disk.Children != null && disk.Children.Count > 0)
                                {
                                    driveInfo.Partitions = disk.Children.Select(p => new PartitionInfo
                                    {
                                        Name = p.Name ?? string.Empty,
                                        Path = p.Path ?? string.Empty,
                                        Size = p.Size,
                                        Type = p.FsType ?? string.Empty,
                                        MountPoint = p.MountPoint ?? string.Empty,
                                        IsMounted = !string.IsNullOrEmpty(p.MountPoint)
                                    }).ToList();
                                    
                                    // Set IsBusy if any partition is mounted
                                    driveInfo.IsBusy = driveInfo.Partitions.Any(p => p.IsMounted);
                                }
                                
                                // Add to mapping
                                _driveMapping.DiskIdNameToDrive[diskIdName] = driveInfo;
                                _driveMapping.DevicePathToDiskIdName[devicePath] = diskIdName;
                                _driveMapping.DeviceNameToDiskIdName[deviceName] = diskIdName;
                                
                                if (!string.IsNullOrEmpty(disk.Serial))
                                {
                                    _driveMapping.SerialToDiskIdName[disk.Serial] = diskIdName;
                                }
                                
                                // Just use the first valid disk ID link
                                break;
                            }
                        }
                    }
                    
                    // Update the last updated timestamp
                    _driveMapping.LastUpdated = DateTime.Now;
                    _lastRefreshTime = DateTime.Now;
                    
                    _logger.LogInformation("Drive mapping refreshed, found {Count} drives", 
                        _driveMapping.DiskIdNameToDrive.Count);
                    
                    return true;
                }
                catch (System.Text.Json.JsonException ex)
                {
                    _logger.LogError(ex, "Failed to parse lsblk output: {Output}", result.Output);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing drives");
                return false;
            }
            finally
            {
                _isRefreshing = false;
                
                // Release the lock
                _refreshLock.Release();
            }
        }
        
        /// <inheritdoc />
        public DriveMapping GetDriveMapping()
        {
            return _driveMapping;
        }
        
        /// <summary>
        /// Handles changes to the /dev/disk/by-id directory
        /// </summary>
        private async void OnDiskByIdChanged(object sender, FileSystemEventArgs e)
        {
            _logger.LogDebug("Detected change in /dev/disk/by-id: {ChangeType} {Name}", 
                e.ChangeType, e.Name);
            
            // Allow a small delay to let multiple changes settle
            await Task.Delay(2000);
            
            // Refresh drives
            await RefreshDrivesAsync();
            
            // Notify subscribers
            DriveChanged?.Invoke(this, new DriveChangeEventArgs 
            { 
                ChangeType = e.ChangeType,
                Path = e.Name,
                FullPath = e.FullPath
            });
        }
        
        /// <summary>
        /// Gets all disk ID symlinks that point to a specific device
        /// </summary>
        private async Task<List<string>> GetDiskIdLinksForDeviceAsync(string devicePath)
        {
            List<string> diskIdLinks = new();
            
            try
            {
                // List all files in /dev/disk/by-id
                var diskIdFiles = _fileSystemInfoService.GetFiles(DISK_BY_ID_DIR);
                
                foreach (var file in diskIdFiles)
                {
                    string fullPath = Path.Combine(DISK_BY_ID_DIR, file);
                    
                    // Resolve the symlink
                    var result = await _commandService.ExecuteCommandAsync($"readlink -f \"{fullPath}\"");
                    if (result.Success && result.Output.Trim() == devicePath)
                    {
                        diskIdLinks.Add(file);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting disk ID links for {DevicePath}", devicePath);
            }
            
            return diskIdLinks;
        }
    }
}