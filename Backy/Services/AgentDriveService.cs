using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;
using Backy.Data;
using Backy.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backy.Services
{
    /// <summary>
    /// Defines the contract for drive-related operations.
    /// </summary>
    public interface IAppDriveService
    {
        string FetchPoolStatus(int poolGroupId);
        (long Size, long Used, long Available, string UsePercent) GetMountPointSize(string mountPoint);
        Task<(bool Success, string Message)> ProtectDriveAsync(string serial);
        Task<(bool Success, string Message)> UnprotectDriveAsync(string serial);
        Task<(bool Success, string Message, List<string> Outputs)> CreatePoolAsync(CreatePoolRequest request);
        Task<(bool Success, string Message)> UnmountPoolAsync(Guid poolGroupGuid);
        Task<(bool Success, string Message)> RemovePoolGroupAsync(Guid poolGroupGuid);
        Task<(bool Success, string Message)> MountPoolAsync(Guid poolGroupGuid);
        Task<(bool Success, string Message)> RenamePoolGroupAsync(RenamePoolRequest request);
        Task<(bool Success, string Message, string Output)> GetPoolDetailAsync(Guid poolGroupGuid);
        Task<(bool Success, string Message)> ForceAddDriveAsync(int driveId, Guid poolGroupGuid, string devPath);
        Task<(bool Success, string Message, List<string> Outputs)> KillProcessesAsync(KillProcessesRequest request);
        Task<List<Drive>> UpdateActiveDrivesAsync();
        Task<List<ProcessInfo>> GetProcessesUsingMountPointAsync(string mountPoint);
        Task<List<PoolInfo>> GetPoolsAsync();
        Task UpdatePoolSizeMetricsAsync(Guid poolGroupGuid);
        Task<(bool Success, string Message, List<string> Outputs)> GetPoolOutputsAsync(Guid poolGroupGuid);
        Task<PoolGroup?> MonitorPoolCreationWithPollingAsync(Guid poolGroupGuid, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Implementation of IAppDriveService that delegates operations to the Backy Agent via the API client.
    /// </summary>
    public class AgentAppDriveService : IAppDriveService
    {
        private readonly IBackyAgentClient _agentClient;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AgentAppDriveService> _logger;

        public AgentAppDriveService(
            IBackyAgentClient agentClient,
            ApplicationDbContext context,
            ILogger<AgentAppDriveService> logger)
        {
            _agentClient = agentClient ?? throw new ArgumentNullException(nameof(agentClient));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Fetches the status of a pool group based on its ID.
        /// </summary>
        public string FetchPoolStatus(int poolGroupId)
        {
            // Find the pool group GUID based on poolGroupId
            var poolGroup = _context.PoolGroups.FirstOrDefault(pg => pg.PoolGroupId == poolGroupId);
            if (poolGroup == null)
            {
                return "Offline";
            }
            
            // Call the agent to get the pool detail using the GUID
            var result = _agentClient.GetPoolDetailAsync(poolGroup.PoolGroupGuid).GetAwaiter().GetResult();
            if (!result.Success)
            {
                return "Offline";
            }
            
            try
            {
                var poolDetail = System.Text.Json.JsonSerializer.Deserialize<PoolDetailResponse>(
                    result.Output,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    
                return poolDetail?.Status ?? "Unknown";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing pool status for pool ID {PoolGroupId}, GUID {PoolGroupGuid}", 
                    poolGroupId, poolGroup.PoolGroupGuid);
                return "Error";
            }
        }

        /// <summary>
        /// Retrieves the size details of a specified mount point.
        /// </summary>
        public (long Size, long Used, long Available, string UsePercent) GetMountPointSize(string mountPoint)
        {
            try
            {
                var result = _agentClient.GetMountPointSizeAsync(mountPoint).GetAwaiter().GetResult();
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting mount point size for {MountPoint}", mountPoint);
                return (0, 0, 0, "0%");
            }
        }

        /// <summary>
        /// Protects a drive by adding it to the list of protected drives.
        /// </summary>
        public async Task<(bool Success, string Message)> ProtectDriveAsync(string serial)
        {
            var drive = await _context.ProtectedDrives.FirstOrDefaultAsync(d => d.Serial == serial);
            if (drive == null)
            {
                // Find the drive in the active drives list
                var activeDrives = await UpdateActiveDrivesAsync();
                var activeDrive = activeDrives.FirstOrDefault(d => d.Serial == serial);
                if (activeDrive == null)
                {
                    return (false, "Drive not found.");
                }

                drive = new ProtectedDrive
                {
                    Serial = serial,
                    Vendor = activeDrive.Vendor,
                    Model = activeDrive.Model,
                    Name = activeDrive.Name,
                    Label = activeDrive.Label,
                };
                _context.ProtectedDrives.Add(drive);
                await _context.SaveChangesAsync();
                return (true, "Drive protected successfully.");
            }
            return (false, "Drive is already protected.");
        }

        /// <summary>
        /// Unprotects a drive by removing it from the list of protected drives.
        /// </summary>
        public async Task<(bool Success, string Message)> UnprotectDriveAsync(string serial)
        {
            var drive = await _context.ProtectedDrives.FirstOrDefaultAsync(d => d.Serial == serial);
            if (drive != null)
            {
                _context.ProtectedDrives.Remove(drive);
                await _context.SaveChangesAsync();
                return (true, "Drive unprotected successfully.");
            }
            return (false, "Drive not found in protected list.");
        }

        /// <summary>
        /// Creates a new pool based on the provided request data with asynchronous processing.
        /// </summary>
        public async Task<(bool Success, string Message, List<string> Outputs)> CreatePoolAsync(CreatePoolRequest request)
        {
            if (string.IsNullOrEmpty(request.PoolLabel) || request.DriveSerials == null || request.DriveSerials.Count == 0)
            {
                return (false, "Pool Label and at least one drive must be selected.", new List<string>());
            }

            // Safety check to prevent operating on protected drives
            var protectedSerials = _context.ProtectedDrives.Select(pd => pd.Serial).ToHashSet();
            if (request.DriveSerials.Any(s => protectedSerials.Contains(s)))
            {
                return (false, "One or more selected drives are protected.", new List<string>());
            }

            // Get the active drives information for use in creating the database records
            var activeDrives = await UpdateActiveDrivesAsync();

            try 
            {
                // Delegate to the agent client to create the pool asynchronously
                var result = await _agentClient.CreatePoolAsync(request);
                
                if (result.Success)
                {
                    // Create a placeholder PoolGroup record in the database
                    var poolGroup = new PoolGroup
                    {
                        PoolGroupGuid = result.PoolGroupGuid, // Use the GUID from the async response
                        GroupLabel = request.PoolLabel,
                        MountPath = $"/mnt/backy/{result.PoolGroupGuid}",
                        PoolEnabled = false, // Will be enabled when creation completes
                        PoolStatus = "creating", // Status indicating the pool is being created
                        Size = 0,
                        Used = 0,
                        Available = 0, 
                        UsePercent = "0%",
                        Drives = new List<PoolDrive>()
                    };
                    
                    // Add drives to the pool group
                    foreach (var serial in request.DriveSerials)
                    {
                        var activeDrive = activeDrives.FirstOrDefault(d => d.Serial == serial);
                        if (activeDrive == null)
                        {
                            _logger.LogWarning($"Could not find active drive with serial {serial}");
                            continue;
                        }
                        
                        // Determine the label for this drive
                        string label;
                        if (request.DriveLabels != null && request.DriveLabels.TryGetValue(serial, out var providedLabel) && !string.IsNullOrWhiteSpace(providedLabel))
                        {
                            label = providedLabel.Trim();
                        }
                        else
                        {
                            // Assign a default label
                            var driveIndex = request.DriveSerials.IndexOf(serial) + 1;
                            label = $"{request.PoolLabel}-{driveIndex}";
                        }
                        
                        var poolDrive = new PoolDrive
                        {
                            Serial = serial,
                            Label = label,
                            Vendor = activeDrive.Vendor,
                            Model = activeDrive.Model,
                            Size = activeDrive.Size,
                            IsConnected = true,
                            IsMounted = false, // Not yet mounted
                            DevPath = activeDrive.IdLink,
                            PoolGroupGuid = result.PoolGroupGuid,
                            PoolGroup = poolGroup
                        };
                        
                        poolGroup.Drives.Add(poolDrive);
                    }
                    
                    _context.PoolGroups.Add(poolGroup);
                    await _context.SaveChangesAsync();
                    
                    return (true, $"Pool '{request.PoolLabel}' creation started.", result.Outputs);
                }
                
                return (false, result.Message, result.Outputs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating pool or saving pool data to database.");
                return (false, $"Error creating pool: {ex.Message}", new List<string>());
            }
        }

        /// <summary>
        /// Unmounts a pool.
        /// </summary>
        public async Task<(bool Success, string Message)> UnmountPoolAsync(Guid poolGroupGuid)
        {
            // Delegate to the agent client
            var result = await _agentClient.UnmountPoolAsync(poolGroupGuid);
            
            if (result.Success)
            {
                // Update the database to reflect that the pool is unmounted
                var poolGroup = await _context.PoolGroups
                    .Include(pg => pg.Drives)
                    .FirstOrDefaultAsync(pg => pg.PoolGroupGuid == poolGroupGuid);
                    
                if (poolGroup != null)
                {
                    foreach (var drive in poolGroup.Drives)
                    {
                        drive.IsMounted = false;
                    }
                    
                    poolGroup.PoolEnabled = false;
                    await _context.SaveChangesAsync();
                }
                
                return (true, "Pool unmounted successfully.");
            }
            
            return (false, result.Message);
        }

        /// <summary>
        /// Removes a pool group.
        /// </summary>
        public async Task<(bool Success, string Message)> RemovePoolGroupAsync(Guid poolGroupGuid)
        {
            var poolGroup = await _context.PoolGroups
                .Include(pg => pg.Drives)
                .FirstOrDefaultAsync(pg => pg.PoolGroupGuid == poolGroupGuid);

            if (poolGroup == null)
            {
                return (false, "Pool group not found.");
            }

            if (!poolGroup.PoolEnabled)
            {
                // Pool is disabled; remove directly from the database
                _context.PoolGroups.Remove(poolGroup);
                await _context.SaveChangesAsync();
                return (true, "Pool group removed successfully.");
            }
            else
            {
                // Pool is enabled; need to use the agent to unmount and remove
                var result = await _agentClient.RemovePoolGroupAsync(poolGroupGuid);
                
                if (result.Success)
                {
                    // Remove the PoolGroup from the database
                    _context.PoolGroups.Remove(poolGroup);
                    await _context.SaveChangesAsync();
                    return (true, "Pool group removed successfully.");
                }
                
                return (false, result.Message);
            }
        }

        /// <summary>
        /// Mounts a pool.
        /// </summary>
        public async Task<(bool Success, string Message)> MountPoolAsync(Guid poolGroupGuid)
        {
            // Get the pool group from database to get its GUID
            var poolGroup = await _context.PoolGroups
                .FirstOrDefaultAsync(pg => pg.PoolGroupGuid == poolGroupGuid);
                
            if (poolGroup == null)
            {
                return (false, "Pool group not found.");
            }
            
            // Construct the proper mount path using the pool group GUID
            string mountPath = $"/mnt/backy/{poolGroupGuid}";
            
            // Delegate to the agent client with the explicit mount path
            var result = await _agentClient.MountPoolAsync(poolGroupGuid, mountPath);
            
            if (result.Success)
            {
                // Update the database to reflect that the pool is mounted
                poolGroup = await _context.PoolGroups
                    .Include(pg => pg.Drives)
                    .FirstOrDefaultAsync(pg => pg.PoolGroupGuid == poolGroupGuid);
                    
                if (poolGroup != null)
                {
                    foreach (var drive in poolGroup.Drives)
                    {
                        drive.IsMounted = true;
                    }
                    
                    poolGroup.PoolEnabled = true;
                    poolGroup.MountPath = mountPath; // Update the mount path in the database
                    await _context.SaveChangesAsync();
                }
                
                return (true, "Pool mounted successfully.");
            }
            
            return (false, result.Message);
        }

        /// <summary>
        /// Renames a pool group.
        /// </summary>
        public async Task<(bool Success, string Message)> RenamePoolGroupAsync(RenamePoolRequest request)
        {
            var poolGroup = await _context
                .PoolGroups.Include(pg => pg.Drives)
                .FirstOrDefaultAsync(pg => pg.PoolGroupGuid == request.PoolGroupGuid);

            if (poolGroup == null)
            {
                return (false, "Pool group not found.");
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Update pool label
                poolGroup.GroupLabel = request.NewPoolLabel;

                // Update drive labels
                foreach (var driveLabel in request.DriveLabels)
                {
                    var drive = poolGroup.Drives.FirstOrDefault(d => d.Id == driveLabel.DriveId);
                    if (drive != null)
                    {
                        drive.Label = driveLabel.Label?.Trim() ?? drive.Label;
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return (true, "Pool and drive labels updated successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error renaming pool.");
                await transaction.RollbackAsync();
                return (false, "An error occurred while renaming the pool.");
            }
        }

        /// <summary>
        /// Gets detailed information about a pool.
        /// </summary>
        public async Task<(bool Success, string Message, string Output)> GetPoolDetailAsync(Guid poolGroupGuid)
        {
            // Delegate to the agent client
            var result = await _agentClient.GetPoolDetailAsync(poolGroupGuid);
            
            // If successful, update the size information in the database
            if (result.Success)
            {
                try 
                {
                    _logger.LogInformation("Raw API response for pool {PoolGroupGuid}: {Response}", poolGroupGuid, result.Output);
                        
                    // Define JSON options with case-insensitive property names
                    var jsonOptions = new System.Text.Json.JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true
                    };
                    
                    var poolDetail = System.Text.Json.JsonSerializer.Deserialize<PoolDetailResponse>(
                        result.Output, jsonOptions);
                        
                    if (poolDetail != null)
                    {
                        // Update the database with the size information
                        var poolGroup = await _context.PoolGroups
                            .FirstOrDefaultAsync(pg => pg.PoolGroupGuid == poolGroupGuid);
                            
                        if (poolGroup != null)
                        {
                            // Extract size metrics explicitly from the dynamic object to avoid case sensitivity issues
                            poolGroup.Size = poolDetail.Size;
                            poolGroup.Used = poolDetail.Used;
                            poolGroup.Available = poolDetail.Available;
                            poolGroup.UsePercent = poolDetail.UsePercent;
                            
                            // Save changes
                            await _context.SaveChangesAsync();
                            
                            _logger.LogInformation("Updated size metrics for pool {PoolGroupGuid}: Size={Size}, Used={Used}, Available={Available}, UsePercent={UsePercent}", 
                                poolGroupGuid, poolGroup.Size, poolGroup.Used, poolGroup.Available, poolGroup.UsePercent);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating pool size information in database for pool {PoolGroupGuid}", poolGroupGuid);
                    // Don't return an error, as the API call was successful
                }
            }
            
            return result;
        }

        /// <summary>
        /// Forcefully adds a drive to a pool.
        /// </summary>
        public async Task<(bool Success, string Message)> ForceAddDriveAsync(int driveId, Guid poolGroupGuid, string devPath)
        {
            // Delegate to the agent client
            var result = await _agentClient.ForceAddDriveAsync(driveId, poolGroupGuid, devPath);
            
            if (result.Success)
            {
                // Update the drive's status in the database
                var drive = await _context.PoolDrives.FirstOrDefaultAsync(d => d.Id == driveId);
                if (drive != null)
                {
                    drive.IsMounted = true;
                    await _context.SaveChangesAsync();
                }
            }
            
            return result;
        }

        /// <summary>
        /// Kills processes using a pool and performs an action.
        /// </summary>
        public async Task<(bool Success, string Message, List<string> Outputs)> KillProcessesAsync(KillProcessesRequest request)
        {
            if (request == null)
            {
                return (false, "Invalid request data.", new List<string>());
            }

            if (request.PoolGroupGuid == Guid.Empty)
            {
                return (false, "Invalid Pool Group GUID.", new List<string>());
            }

            // Delegate to the agent client
            var result = await _agentClient.KillProcessesAsync(request);
            
            if (result.Success)
            {
                if (request.Action.Equals("UnmountPool", StringComparison.OrdinalIgnoreCase))
                {
                    // Update the database to reflect that the pool is unmounted
                    var poolGroup = await _context.PoolGroups
                        .Include(pg => pg.Drives)
                        .FirstOrDefaultAsync(pg => pg.PoolGroupGuid == request.PoolGroupGuid);
                        
                    if (poolGroup != null)
                    {
                        foreach (var drive in poolGroup.Drives)
                        {
                            drive.IsMounted = false;
                        }
                        
                        poolGroup.PoolEnabled = false;
                        await _context.SaveChangesAsync();
                    }
                    
                    return (true, "Pool unmounted successfully after killing processes.", result.Outputs);
                }
                else if (request.Action.Equals("RemovePoolGroup", StringComparison.OrdinalIgnoreCase))
                {
                    // Remove the PoolGroup from the database
                    var poolGroup = await _context.PoolGroups
                        .FirstOrDefaultAsync(pg => pg.PoolGroupGuid == request.PoolGroupGuid);
                        
                    if (poolGroup != null)
                    {
                        _context.PoolGroups.Remove(poolGroup);
                        await _context.SaveChangesAsync();
                    }
                    
                    return (true, "Pool group removed successfully after killing processes.", result.Outputs);
                }
            }
            
            return result;
        }

        /// <summary>
        /// Updates the list of active drives.
        /// </summary>
        public async Task<List<Drive>> UpdateActiveDrivesAsync()
        {
            try
            {
                // Get all drives from the agent
                var activeDrives = await _agentClient.GetDrivesAsync();
                
                // Get protected drives from the database
                var protectedDrives = await _context.ProtectedDrives.ToListAsync();
                var protectedSerials = protectedDrives.Select(d => d.Serial).ToHashSet();
                
                // Mark protected drives
                foreach (var drive in activeDrives)
                {
                    if (protectedSerials.Contains(drive.Serial))
                    {
                        drive.IsProtected = true;
                    }
                }
                
                return activeDrives;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating active drives.");
                return new List<Drive>();
            }
        }

        /// <summary>
        /// Gets a list of processes using a mount point.
        /// </summary>
        public async Task<List<ProcessInfo>> GetProcessesUsingMountPointAsync(string mountPoint)
        {
            // Delegate to the agent client
            return await _agentClient.GetProcessesUsingMountPointAsync(mountPoint);
        }

        /// <summary>
        /// Gets all pools from the Backy Agent API
        /// </summary>
        /// <returns>A list of pool information from the agent</returns>
        public async Task<List<PoolInfo>> GetPoolsAsync()
        {
            try
            {
                // Delegate to the agent client
                return await _agentClient.GetPoolsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get pools from Backy Agent API");
                return new List<PoolInfo>();
            }
        }

        /// <summary>
        /// Updates the size metrics of a pool group by fetching the latest data from the mounted filesystem.
        /// </summary>
        /// <param name="poolGroupGuid">The GUID of the pool group to update.</param>
        public async Task UpdatePoolSizeMetricsAsync(Guid poolGroupGuid)
        {
            try
            {
                var poolGroup = await _context.PoolGroups.FirstOrDefaultAsync(pg => pg.PoolGroupGuid == poolGroupGuid);
                if (poolGroup == null)
                {
                    _logger.LogWarning($"Pool group with GUID {poolGroupGuid} not found.");
                    return;
                }

                // Skip if the pool is not enabled or the mount path is empty
                if (!poolGroup.PoolEnabled || string.IsNullOrEmpty(poolGroup.MountPath))
                {
                    _logger.LogInformation($"Pool {poolGroup.GroupLabel} is not enabled or has no mount path. Skipping size metrics update.");
                    return;
                }

                // Get pool details from the agent which includes size metrics
                var result = await _agentClient.GetPoolDetailAsync(poolGroupGuid);
                if (!result.Success)
                {
                    _logger.LogWarning($"Failed to get pool details for {poolGroupGuid}: {result.Message}");
                    return;
                }

                try
                {
                    // Parse the JSON response using the strongly-typed model
                    var jsonOptions = new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    
                    var poolDetail = JsonSerializer.Deserialize<PoolDetailResponse>(result.Output, jsonOptions);
                    
                    if (poolDetail != null)
                    {
                        // Update the pool group with the pool details
                        poolGroup.Size = poolDetail.Size;
                        poolGroup.Used = poolDetail.Used;
                        poolGroup.Available = poolDetail.Available;
                        poolGroup.UsePercent = poolDetail.UsePercent;
                        
                        // Save changes
                        await _context.SaveChangesAsync();
                        
                        _logger.LogInformation("Updated size metrics for pool {PoolGroupGuid}: Size={Size}, Used={Used}, Available={Available}, UsePercent={UsePercent}", 
                            poolGroupGuid, poolGroup.Size, poolGroup.Used, poolGroup.Available, poolGroup.UsePercent);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error parsing pool details response for pool group {poolGroupGuid}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating size metrics for pool group {poolGroupGuid}");
            }
        }

        /// <summary>
        /// Gets the command outputs from an asynchronous pool creation operation.
        /// </summary>
        /// <param name="poolGroupGuid">The GUID of the pool group</param>
        /// <returns>A tuple with success status, message, and command outputs</returns>
        public async Task<(bool Success, string Message, List<string> Outputs)> GetPoolOutputsAsync(Guid poolGroupGuid)
        {
            try
            {
                // Delegate to the agent client
                var result = await _agentClient.GetPoolCreationOutputsAsync(poolGroupGuid);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pool outputs for pool group {PoolGroupGuid}", poolGroupGuid);
                return (false, $"Error retrieving pool outputs: {ex.Message}", new List<string>());
            }
        }

        /// <summary>
        /// Monitors the creation of a pool with exponential backoff polling.
        /// </summary>
        /// <param name="poolGroupGuid">The GUID of the pool group to monitor</param>
        /// <param name="cancellationToken">A cancellation token to allow cancelling the monitoring</param>
        /// <returns>The completed PoolGroup if successful, or null if the pool creation failed or was cancelled</returns>
        public async Task<PoolGroup?> MonitorPoolCreationWithPollingAsync(Guid poolGroupGuid, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting to monitor pool creation for pool {PoolGroupGuid}", poolGroupGuid);
            
            int retryCount = 0;
            const int maxRetries = 60; // Maximum number of retries
            TimeSpan delay = TimeSpan.FromSeconds(1); // Starting delay
            const double backoffMultiplier = 1.5; // Exponential backoff multiplier
            const int maxDelaySeconds = 30; // Maximum delay in seconds
            
            // Reference to the pool group in the database
            PoolGroup? poolGroup = await _context.PoolGroups
                .Include(pg => pg.Drives)
                .FirstOrDefaultAsync(pg => pg.PoolGroupGuid == poolGroupGuid);
                
            if (poolGroup == null)
            {
                _logger.LogWarning("Pool group {PoolGroupGuid} not found in database for monitoring", poolGroupGuid);
                return null;
            }
            
            while (retryCount < maxRetries && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogDebug("Checking pool status for {PoolGroupGuid}, attempt {RetryCount}", poolGroupGuid, retryCount + 1);
                    
                    // Get the current pool status from the agent
                    var result = await _agentClient.GetPoolDetailAsync(poolGroupGuid);
                    
                    if (result.Success)
                    {
                        // Parse the JSON response using the strongly-typed model
                        var jsonOptions = new System.Text.Json.JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        };
                        
                        var poolDetail = JsonSerializer.Deserialize<PoolDetailResponse>(result.Output, jsonOptions);
                        
                        if (poolDetail != null)
                        {
                            _logger.LogInformation("Pool {PoolGroupGuid} has status: {Status}", poolGroupGuid, poolDetail.Status);
                            
                            // Update the database with the latest pool details
                            poolGroup.Size = poolDetail.Size;
                            poolGroup.Used = poolDetail.Used;
                            poolGroup.Available = poolDetail.Available;
                            poolGroup.UsePercent = poolDetail.UsePercent;
                            poolGroup.PoolStatus = poolDetail.Status;
                            
                            // If the pool is no longer creating, update the database and return
                            if (!string.Equals(poolDetail.Status, "creating", StringComparison.OrdinalIgnoreCase))
                            {
                                // Update drive statuses
                                if (poolDetail.Drives != null && poolDetail.Drives.Any())
                                {
                                    foreach (var driveStatus in poolDetail.Drives)
                                    {
                                        var drive = poolGroup.Drives.FirstOrDefault(d => d.Serial == driveStatus.Serial);
                                        if (drive != null)
                                        {
                                            drive.IsConnected = true;
                                            drive.IsMounted = true;
                                        }
                                    }
                                }
                                
                                // Update pool status
                                poolGroup.PoolEnabled = string.Equals(poolDetail.Status, "active", StringComparison.OrdinalIgnoreCase);
                                await _context.SaveChangesAsync();
                                
                                if (string.Equals(poolDetail.Status, "active", StringComparison.OrdinalIgnoreCase))
                                {
                                    _logger.LogInformation("Pool {PoolGroupGuid} creation completed successfully", poolGroupGuid);
                                    return poolGroup;
                                }
                                else if (string.Equals(poolDetail.Status, "failed", StringComparison.OrdinalIgnoreCase))
                                {
                                    _logger.LogWarning("Pool {PoolGroupGuid} creation failed", poolGroupGuid);
                                    return null;
                                }
                            }
                            
                            // Save the updated metrics even if we're still in "creating" state
                            await _context.SaveChangesAsync();
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Failed to get pool details: {Message}", result.Message);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking pool status for {PoolGroupGuid}", poolGroupGuid);
                }
                
                // Increment retry count and delay with exponential backoff
                retryCount++;
                
                // Apply exponential backoff with a maximum delay
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * backoffMultiplier, maxDelaySeconds));
                
                _logger.LogDebug("Waiting {DelaySeconds} seconds before next status check for pool {PoolGroupGuid}", 
                    delay.TotalSeconds, poolGroupGuid);
                
                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    _logger.LogInformation("Pool creation monitoring was cancelled for {PoolGroupGuid}", poolGroupGuid);
                    break;
                }
            }
            
            // If we've reached here, either the max retries were exceeded or the operation was cancelled
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Pool creation monitoring was cancelled for {PoolGroupGuid}", poolGroupGuid);
            }
            else
            {
                _logger.LogWarning("Maximum monitoring attempts ({MaxRetries}) reached for pool {PoolGroupGuid}", 
                    maxRetries, poolGroupGuid);
            }
            
            return null;
        }
    }
}