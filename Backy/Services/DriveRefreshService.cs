using Backy.Data;
using Backy.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Backy.Services
{
    /// <summary>
    /// Background service that periodically refreshes drive information from the Backy Agent
    /// and updates the database to provide a more responsive user experience.
    /// </summary>
    public class DriveRefreshService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DriveRefreshService> _logger;
        private readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(1);

        public DriveRefreshService(
            IServiceScopeFactory scopeFactory,
            ILogger<DriveRefreshService> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogDebug("Drive Refresh Service is starting.");

            // Initial refresh on startup
            await RefreshDriveDataAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Wait for the refresh interval
                    await Task.Delay(_refreshInterval, stoppingToken);
                    
                    // Refresh the data
                    await RefreshDriveDataAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Normal shutdown, don't log as error
                    _logger.LogInformation("Drive Refresh Service is stopping.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Drive Refresh Service.");
                }
            }
        }

        /// <summary>
        /// Refreshes drive data from the Backy Agent and updates the database.
        /// </summary>
        private async Task RefreshDriveDataAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogDebug("Refreshing drive data...");
                
                // Create a new scope for dependency injection
                using var scope = _scopeFactory.CreateScope();
                
                // Get the required services
                var AppDriveService = scope.ServiceProvider.GetRequiredService<IAppDriveService>();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                
                // Get the active drives from the Backy Agent
                var activeDrives = await AppDriveService.UpdateActiveDrivesAsync();
                _logger.LogDebug($"Retrieved {activeDrives.Count} active drives from the agent.");
                
                // Get existing pool groups from the database
                var poolGroups = await dbContext.PoolGroups.Include(pg => pg.Drives).ToListAsync(stoppingToken);
                _logger.LogDebug($"Found {poolGroups.Count} pool groups in the database.");
                
                // Get protected drives from the database
                var protectedDrives = await dbContext.ProtectedDrives.ToListAsync(stoppingToken);
                _logger.LogDebug($"Found {protectedDrives.Count} protected drives in the database.");
                
                // Track if any changes were made that need to be saved
                bool changesMade = false;

                try {
                    // Get pool information from the Agent API
                    var existingPools = await AppDriveService.GetPoolsAsync();
                    var existingPoolGuids = existingPools.Select(p => p.PoolGroupGuid).ToHashSet();
                    
                    // Mark pools that don't exist on the system as offline
                    // Skip pools that are in "creating" state since they might not be in the agent API yet
                    foreach (var pool in poolGroups.Where(p => p.State != "creating"))
                    {
                        if (!existingPoolGuids.Contains(pool.PoolGroupGuid))
                        {
                            _logger.LogWarning($"Pool {pool.GroupLabel} (ID: {pool.PoolGroupId}) exists in database but not on system. Marking as offline.");
                            pool.PoolStatus = "Offline";
                            pool.PoolEnabled = false;
                            changesMade = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving pools from Agent API. Will proceed with existing database pools.");
                }

                // Update size metrics for all enabled pools
                foreach (var pool in poolGroups.Where(p => p.PoolEnabled && !string.IsNullOrEmpty(p.MountPath)))
                {
                    try
                    {
                        await AppDriveService.UpdatePoolSizeMetricsAsync(pool.PoolGroupGuid);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error updating size metrics for pool {pool.GroupLabel} (GUID: {pool.PoolGroupGuid})");
                    }
                }

                // Collection of serial numbers for drives in pools
                var pooledDriveSerials = poolGroups.SelectMany(pg => pg.Drives).Select(d => d.Serial).ToHashSet();
                
                // Collection of serial numbers for protected drives
                var protectedSerials = protectedDrives.Select(pd => pd.Serial).ToHashSet();

                // Process each pool group
                foreach (var pool in poolGroups)
                {
                    // Check for duplicate drives and remove duplicates
                    var duplicateDriveIds = pool.Drives
                        .GroupBy(d => d.Id)
                        .Where(g => g.Count() > 1)
                        .Select(g => g.Key)
                        .ToList();

                    if (duplicateDriveIds.Any())
                    {
                        _logger.LogWarning($"Pool {pool.GroupLabel} has duplicate drive IDs: {string.Join(", ", duplicateDriveIds)}");
                        
                        // Remove duplicate drives
                        pool.Drives = pool.Drives
                        .GroupBy(d => d.Id)
                        .Select(g => g.First())
                        .ToList();
                        changesMade = true;
                    }

                    // Check if all drives are connected
                    bool allConnected = true;
                    foreach (var drive in pool.Drives)
                    {
                        // Find matching active drive
                        var activeDrive = activeDrives.FirstOrDefault(d => d.Serial == drive.Serial);
                        if (activeDrive != null)
                        {
                            // Update drive properties if they've changed
                            if (!drive.IsConnected || drive.Vendor != activeDrive.Vendor || drive.Model != activeDrive.Model ||
                            drive.IsMounted != activeDrive.IsMounted || drive.DevPath != activeDrive.IdLink ||
                            drive.Size != activeDrive.Size)
                            {
                                drive.IsConnected = true;
                                drive.Vendor = activeDrive.Vendor;
                                drive.Model = activeDrive.Model;
                                drive.IsMounted = activeDrive.IsMounted;
                                drive.DevPath = activeDrive.IdLink;
                                drive.Size = activeDrive.Size;
                                changesMade = true;
                            }
                        }
                        else
                        {
                            // Mark drive as disconnected if not found in active drives
                            if (drive.IsConnected)
                            {
                                drive.IsConnected = false;
                                changesMade = true;
                            }
                            allConnected = false;
                        }
                    }

                    // Update pool connection status
                    pool.AllDrivesConnected = allConnected;

                    // Skip status update for pools in "creating" state
                    if (pool.State == "creating")
                    {
                        _logger.LogDebug($"Skipping status update for pool {pool.GroupLabel} (GUID: {pool.PoolGroupGuid}) in 'creating' state");
                        continue;
                    }

                    // Update pool status
                    if (pool.PoolEnabled)
                    {
                        try
                        {
                            // Skip status fetch for pools we already know are offline
                            if (pool.PoolStatus == "Offline")
                            {
                                continue;
                            }
                        
                            // Fetch Pool Status with a timeout
                            var statusTask = Task.Run(() => AppDriveService.FetchPoolStatus(pool.PoolGroupId));
                            var timeoutTask = Task.Delay(5000); // 5 seconds timeout
                            
                            if (await Task.WhenAny(statusTask, timeoutTask) == timeoutTask)
                            {
                                // Status fetch timed out
                                _logger.LogWarning($"Status fetch for pool {pool.GroupLabel} (GUID: {pool.PoolGroupGuid}) timed out. Marking as Offline.");
                                if (pool.PoolStatus != "Offline")
                                {
                                    pool.PoolStatus = "Offline";
                                    changesMade = true;
                                }
                            }
                            else
                            {
                                // Status fetch completed
                                var status = await statusTask;
                                if (pool.PoolStatus != status)
                                {
                                    pool.PoolStatus = status;
                                    changesMade = true;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error fetching pool status for pool {pool.GroupLabel}. Marking as Offline.");
                            if (pool.PoolStatus != "Offline")
                            {
                                pool.PoolStatus = "Offline";
                                changesMade = true;
                            }
                        }
                    }
                    else
                    {
                        // If pool is disabled, set status to Offline
                        if (pool.PoolStatus != "Offline")
                        {
                            pool.PoolStatus = "Offline";
                            changesMade = true;
                        }
                    }
                }

                // Save changes to database if any were made
                if (changesMade)
                {
                    await dbContext.SaveChangesAsync(stoppingToken);
                    _logger.LogDebug("Drive data refresh completed with updates. Changes saved to the database.");
                }
                else
                {
                    _logger.LogDebug("Drive data refresh completed. No changes detected.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing drive data.");
            }
        }
    }
}