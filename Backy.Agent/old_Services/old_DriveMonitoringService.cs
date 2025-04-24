using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Backy.Agent.Models;

namespace Backy.Agent.Services
{
    /// <summary>
    /// Background service that periodically refreshes and caches drive information
    /// </summary>
    public class DriveMonitoringService : BackgroundService
    {
        private readonly ILogger<DriveMonitoringService> _logger;
        private readonly ISystemCommandService _commandService;
        private readonly IServiceProvider _serviceProvider;
        private readonly AgentSettings _settings;
        private LsblkOutput _cachedDrives = new LsblkOutput { Blockdevices = new List<BlockDevice>() };
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
        private DateTime _lastRefreshTime = DateTime.MinValue;
        private bool _isRefreshing = false;

        /// <summary>
        /// Gets the timestamp of the last refresh
        /// </summary>
        public DateTime LastRefreshTime => _lastRefreshTime;

        /// <summary>
        /// Gets whether a refresh is currently in progress
        /// </summary>
        public bool IsRefreshing => _isRefreshing;

        public DriveMonitoringService(
            ILogger<DriveMonitoringService> logger,
            ISystemCommandService commandService,
            IServiceProvider serviceProvider,
            IOptions<AgentSettings> options)
        {
            _logger = logger;
            _commandService = commandService;
            _serviceProvider = serviceProvider;
            _settings = options.Value;
        }

        /// <summary>
        /// Background task that periodically refreshes drive information
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Drive monitoring service started");

            try
            {
                // Initial refresh
                await RefreshDrivesAsync();

                // Continue periodic refresh until stopped
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // Refresh every minute

                    // Skip if already refreshing
                    if (!_isRefreshing)
                    {
                        await RefreshDrivesAsync();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
                _logger.LogInformation("Background drive monitoring service shutting down");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in background drive monitoring service");
            }
        }

        /// <summary>
        /// Gets the cached drive information
        /// </summary>
        public LsblkOutput GetCachedDrives()
        {
            return _cachedDrives;
        }

        /// <summary>
        /// Refreshes the drive information cache
        /// </summary>
        public async Task<bool> RefreshDrivesAsync()
        {
            // Use a semaphore to prevent concurrent refreshes
            if (!await _refreshLock.WaitAsync(0))
            {
                _logger.LogDebug("Drive refresh already in progress, skipping");
                return false;
            }

            try
            {
                _isRefreshing = true;
                _logger.LogDebug("Refreshing drive information cache");

                // Execute lsblk command directly
                var result = await _commandService.ExecuteCommandAsync("lsblk -J -b -o NAME,SIZE,TYPE,MOUNTPOINT,UUID,SERIAL,VENDOR,MODEL,FSTYPE,PATH,ID-LINK");

                if (!result.Success)
                {
                    _logger.LogError("Failed to refresh drive information: {Error}", result.Output);
                    return false;
                }

                try
                {
                    // Parse JSON output
                    var lsblkOutput = System.Text.Json.JsonSerializer.Deserialize<LsblkOutput>(result.Output);
                    if (lsblkOutput != null)
                    {
                        if (lsblkOutput.Blockdevices != null)
                        {
                            // Filter to include only disk type devices and their children
                            _logger.LogDebug("Filtering devices to include only disks and their children");
                            lsblkOutput.Blockdevices = lsblkOutput.Blockdevices
                                .Where(d => d.Type?.ToLowerInvariant() == "disk")
                                .ToList();

                            // Apply drive exclusions based on settings
                            if (_settings.ExcludedDrives.Any())
                            {
                                _logger.LogDebug("Filtering out excluded drives during refresh: {ExcludedDrives}", 
                                    string.Join(", ", _settings.ExcludedDrives));
                                
                                // Filter out drives that should be excluded
                                lsblkOutput.Blockdevices = lsblkOutput.Blockdevices
                                    .Where(d => !ExcludeDrive(d))
                                    .ToList();
                            }
                        }
                        
                        _cachedDrives = lsblkOutput;
                        _lastRefreshTime = DateTime.UtcNow;
                        _logger.LogDebug("Drive information cache refreshed successfully with {Count} devices", 
                            _cachedDrives.Blockdevices?.Count ?? 0);
                        return true;
                    }
                    else
                    {
                        _logger.LogWarning("Failed to deserialize lsblk output");
                        return false;
                    }
                }
                catch (System.Text.Json.JsonException ex)
                {
                    _logger.LogError(ex, "Error parsing lsblk JSON output: {Output}", result.Output);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing drive information");
                return false;
            }
            finally
            {
                _isRefreshing = false;
                _refreshLock.Release();
            }
        }

        /// <summary>
        /// Determines whether a drive should be excluded based on the configured exclusion patterns
        /// </summary>
        /// <param name="drive">The drive to check</param>
        /// <returns>True if the drive should be excluded, false otherwise</returns>
        private bool ExcludeDrive(BlockDevice drive)
        {
            if (drive == null || string.IsNullOrEmpty(drive.Name))
                return false;
            
            // Check if drive name (with or without /dev/ prefix) matches any excluded drive
            foreach (var excludedDrive in _settings.ExcludedDrives)
            {
                var normalizedDrivePattern = excludedDrive.TrimEnd('*');
                var normalizedExcludedDrive = excludedDrive.StartsWith("/dev/") ? excludedDrive : $"/dev/{excludedDrive}";
                
                // Check full path match
                if (!string.IsNullOrEmpty(drive.Path) && 
                    (drive.Path.Equals(normalizedExcludedDrive, StringComparison.OrdinalIgnoreCase) ||
                     (excludedDrive.EndsWith("*") && drive.Path.StartsWith(normalizedDrivePattern, StringComparison.OrdinalIgnoreCase))))
                {
                    return true;
                }
                
                // Check name match (with or without /dev/ prefix)
                var driveName = drive.Name;
                var plainExcludedName = excludedDrive.Replace("/dev/", "");
                
                if (driveName.Equals(plainExcludedName, StringComparison.OrdinalIgnoreCase) ||
                    (excludedDrive.EndsWith("*") && driveName.StartsWith(plainExcludedName.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            
            return false;
        }

        /// <summary>
        /// Notifies the service that a drive operation has been performed
        /// </summary>
        public async Task NotifyDriveOperationAsync()
        {
            await RefreshDrivesAsync();
        }
    }
}