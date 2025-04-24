using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Backy.Agent.Models;
using Backy.Agent.Services.Core;
using Backy.Agent.Services.Storage.Drives;
using Backy.Agent.Services.Storage.Metadata;

namespace Backy.Agent.Services.Storage.Pools
{
    /// <summary>
    /// Manages asynchronous pool operations for tracking long-running tasks.
    /// </summary>
    /// <remarks>
    /// This service:
    /// - Maintains a queue of pending pool operations
    /// - Tracks the status and progress of ongoing operations
    /// - Provides methods to query operation status
    /// - Handles operation timeouts and cancellation
    /// - Stores operation history for auditing
    /// 
    /// Implements a producer-consumer pattern for processing pool operations
    /// asynchronously without blocking the API.
    /// </remarks>
    public class PoolOperationManager : IPoolOperationManager
    {
        private readonly ILogger<PoolOperationManager> _logger;
        private readonly ISystemCommandService _commandService;
        private readonly IPoolMetadataService _metadataService;
        
        // Thread-safe collection to store operation status
        private readonly ConcurrentDictionary<Guid, PoolOperation> _operations = new ConcurrentDictionary<Guid, PoolOperation>();
        
        // Dictionary of cancellation tokens for operations
        private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellationTokens = new ConcurrentDictionary<Guid, CancellationTokenSource>();
        
        // Operation processing queue
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        
        public PoolOperationManager(
            ILogger<PoolOperationManager> logger,
            ISystemCommandService commandService,
            IPoolMetadataService metadataService)
        {
            _logger = logger;
            _commandService = commandService;
            _metadataService = metadataService;
        }
        
        /// <inheritdoc />
        public async Task<PoolOperation> RegisterOperationAsync(Guid poolGroupGuid, PoolOperationType operationType, 
            string description, bool canBeCancelled = true)
        {
            var operationId = Guid.NewGuid();
            
            var operation = new PoolOperation
            {
                OperationId = operationId,
                PoolGroupGuid = poolGroupGuid,
                OperationType = operationType,
                Description = description,
                Status = PoolOperationStatus.Pending,
                StartTime = DateTime.UtcNow,
                CanBeCancelled = canBeCancelled
            };
            
            _operations[operationId] = operation;
            
            if (canBeCancelled)
            {
                _cancellationTokens[operationId] = new CancellationTokenSource();
            }
            
            _logger.LogInformation("Registered new operation: {OperationType} ({OperationId}) for pool {PoolGroupGuid}: {Description}",
                operationType, operationId, poolGroupGuid, description);
            
            return operation;
        }
        
        /// <inheritdoc />
        public async Task<bool> UpdateOperationStatusAsync(Guid operationId, PoolOperationStatus status, 
            string? statusMessage = null, int? progressPercentage = null)
        {
            if (!_operations.TryGetValue(operationId, out var operation))
            {
                _logger.LogWarning("Attempted to update nonexistent operation: {OperationId}", operationId);
                return false;
            }
            
            operation.Status = status;
            
            if (statusMessage != null)
            {
                operation.StatusMessage = statusMessage;
            }
            
            if (progressPercentage.HasValue)
            {
                operation.ProgressPercentage = progressPercentage.Value;
            }
            
            operation.LastUpdated = DateTime.UtcNow;
            
            _logger.LogDebug("Updated operation {OperationId} status to {Status}, progress: {Progress}%, message: {Message}", 
                operationId, status, operation.ProgressPercentage, statusMessage ?? "No message");
            
            return true;
        }
        
        /// <inheritdoc />
        public async Task<bool> CompleteOperationAsync(Guid operationId, bool success, string? resultMessage = null, 
            object? detailedResult = null)
        {
            if (!_operations.TryGetValue(operationId, out var operation))
            {
                _logger.LogWarning("Attempted to complete nonexistent operation: {OperationId}", operationId);
                return false;
            }
            
            operation.Status = success ? PoolOperationStatus.Completed : PoolOperationStatus.Failed;
            operation.EndTime = DateTime.UtcNow;
            operation.Success = success;
            
            if (resultMessage != null)
            {
                operation.ResultMessage = resultMessage;
            }
            
            if (detailedResult != null)
            {
                operation.DetailedResult = detailedResult;
            }
            
            // Remove cancellation token if it exists
            if (_cancellationTokens.TryRemove(operationId, out var tokenSource))
            {
                tokenSource.Dispose();
            }
            
            _logger.LogInformation("Completed operation {OperationId} with {Result}: {Message}", 
                operationId, success ? "success" : "failure", resultMessage ?? "No message");
            
            return true;
        }
        
        /// <inheritdoc />
        public async Task<PoolOperation?> GetOperationAsync(Guid operationId)
        {
            if (_operations.TryGetValue(operationId, out var operation))
            {
                return operation;
            }
            
            return null;
        }
        
        /// <inheritdoc />
        public async Task<IEnumerable<PoolOperation>> GetOperationsForPoolAsync(Guid poolGroupGuid, bool includeCompleted = false)
        {
            return _operations.Values
                .Where(o => o.PoolGroupGuid == poolGroupGuid)
                .Where(o => includeCompleted || (o.Status != PoolOperationStatus.Completed && o.Status != PoolOperationStatus.Failed))
                .OrderByDescending(o => o.StartTime)
                .ToList();
        }
        
        /// <inheritdoc />
        public async Task<IEnumerable<PoolOperation>> GetAllOperationsAsync(bool includeCompleted = false)
        {
            return _operations.Values
                .Where(o => includeCompleted || (o.Status != PoolOperationStatus.Completed && o.Status != PoolOperationStatus.Failed))
                .OrderByDescending(o => o.StartTime)
                .ToList();
        }
        
        /// <inheritdoc />
        public async Task<bool> CancelOperationAsync(Guid operationId)
        {
            if (!_operations.TryGetValue(operationId, out var operation))
            {
                _logger.LogWarning("Attempted to cancel nonexistent operation: {OperationId}", operationId);
                return false;
            }
            
            if (!operation.CanBeCancelled)
            {
                _logger.LogWarning("Attempted to cancel operation that cannot be cancelled: {OperationId}", operationId);
                return false;
            }
            
            if (operation.Status == PoolOperationStatus.Completed || 
                operation.Status == PoolOperationStatus.Failed || 
                operation.Status == PoolOperationStatus.Cancelled)
            {
                _logger.LogWarning("Attempted to cancel operation that is already in final state: {OperationId}, {Status}", 
                    operationId, operation.Status);
                return false;
            }
            
            if (_cancellationTokens.TryGetValue(operationId, out var tokenSource))
            {
                try
                {
                    tokenSource.Cancel();
                    
                    operation.Status = PoolOperationStatus.Cancelled;
                    operation.EndTime = DateTime.UtcNow;
                    operation.ResultMessage = "Operation was cancelled by user request";
                    
                    _logger.LogInformation("Cancelled operation: {OperationId}", operationId);
                    
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error cancelling operation: {OperationId}", operationId);
                    return false;
                }
            }
            
            _logger.LogWarning("No cancellation token found for operation: {OperationId}", operationId);
            return false;
        }
        
        /// <inheritdoc />
        public async Task<int> CleanupCompletedOperationsAsync(TimeSpan olderThan)
        {
            var cutoffTime = DateTime.UtcNow - olderThan;
            var count = 0;
            
            var operationsToRemove = _operations.Values
                .Where(o => (o.Status == PoolOperationStatus.Completed || 
                           o.Status == PoolOperationStatus.Failed || 
                           o.Status == PoolOperationStatus.Cancelled) && 
                           o.EndTime.HasValue && 
                           o.EndTime.Value < cutoffTime)
                .ToList();
            
            foreach (var operation in operationsToRemove)
            {
                if (_operations.TryRemove(operation.OperationId, out _))
                {
                    count++;
                }
                
                // Also remove any associated cancellation token
                if (_cancellationTokens.TryRemove(operation.OperationId, out var tokenSource))
                {
                    tokenSource.Dispose();
                }
            }
            
            _logger.LogInformation("Cleaned up {Count} completed operations older than {OlderThan}", 
                count, olderThan);
            
            return count;
        }
        
        /// <summary>
        /// Gets the current status of an operation
        /// </summary>
        public async Task<Result<PoolOperationStatus>> GetOperationStatusAsync(Guid operationId)
        {
            var operation = await GetOperationAsync(operationId);
            
            if (operation == null)
            {
                return Result<PoolOperationStatus>.Error($"Operation {operationId} not found");
            }
            
            var status = new PoolOperationStatus
            {
                OperationId = operation.OperationId,
                PoolGroupGuid = operation.PoolGroupGuid,
                OperationType = operation.OperationType,
                Status = operation.Status,
                Description = operation.Description,
                StatusMessage = operation.StatusMessage,
                ProgressPercentage = operation.ProgressPercentage,
                StartTime = operation.StartTime,
                LastUpdated = operation.LastUpdated,
                EndTime = operation.EndTime,
                Success = operation.Success,
                ResultMessage = operation.ResultMessage,
                CanBeCancelled = operation.CanBeCancelled
            };
            
            return Result<PoolOperationStatus>.Success(status);
        }
        
        /// <summary>
        /// Creates a new pool asynchronously
        /// </summary>
        public async Task CreatePoolAsync(Guid operationId, Guid poolGroupGuid, string raidLevel, List<string> drives, 
            string mountPath, PoolMetadata metadata)
        {
            try
            {
                // Use the provided operation ID or create a new one
                var operation = await GetOperationAsync(operationId);
                if (operation == null)
                {
                    operation = await RegisterOperationAsync(
                        poolGroupGuid,
                        PoolOperationType.CreatePool,
                        $"Creating {raidLevel} pool with {drives.Count} drives"
                    );
                    operationId = operation.OperationId;
                }
                
                await UpdateOperationStatusAsync(
                    operationId,
                    PoolOperationStatus.Running,
                    "Preparing to create RAID array",
                    0
                );
                
                // Get cancellation token
                CancellationToken cancellationToken = CancellationToken.None;
                if (_cancellationTokens.TryGetValue(operationId, out var tokenSource))
                {
                    cancellationToken = tokenSource.Token;
                }
                
                // Convert the GUID to the mdadm UUID format (no dashes)
                string mdadmUuid = poolGroupGuid.ToString("N");
                
                // Create the array
                string devicePaths = string.Join(" ", drives.Select(d => $"/dev/disk/by-id/{d}"));
                string deviceCount = drives.Count.ToString();
                
                // Determine the RAID level command parameters
                string raidParam;
                switch (raidLevel.ToLowerInvariant())
                {
                    case "raid0":
                        raidParam = "--level=0";
                        break;
                    case "raid1":
                        raidParam = "--level=1";
                        break;
                    case "raid5":
                        raidParam = "--level=5";
                        break;
                    case "raid6":
                        raidParam = "--level=6";
                        break;
                    case "raid10":
                        raidParam = "--level=10";
                        break;
                    default:
                        await CompleteOperationAsync(
                            operationId,
                            false,
                            $"Unsupported RAID level: {raidLevel}"
                        );
                        return;
                }
                
                // Update status
                await UpdateOperationStatusAsync(
                    operationId,
                    PoolOperationStatus.Running,
                    "Creating RAID array",
                    10
                );
                
                // Create the array
                string createCommand = $"mdadm --create /dev/md/{mdadmUuid} {raidParam} --raid-devices={deviceCount} " +
                                      $"--name={mdadmUuid} {devicePaths}";
                
                var createResult = await _commandService.ExecuteCommandAsync(createCommand, true);
                
                if (!createResult.Success)
                {
                    _logger.LogError("Failed to create RAID array: {Error}", createResult.Error);
                    await CompleteOperationAsync(
                        operationId,
                        false,
                        $"Failed to create RAID array: {createResult.Error}"
                    );
                    return;
                }
                
                // Wait for array to be assembled
                await Task.Delay(2000, cancellationToken);
                
                // Update status
                await UpdateOperationStatusAsync(
                    operationId,
                    PoolOperationStatus.Running,
                    "Creating filesystem",
                    50
                );
                
                // Create filesystem
                string devicePath = $"/dev/md/{mdadmUuid}";
                string mkfsCommand = $"mkfs.ext4 -L \"{metadata.Label}\" {devicePath}";
                
                var mkfsResult = await _commandService.ExecuteCommandAsync(mkfsCommand, true);
                
                if (!mkfsResult.Success)
                {
                    _logger.LogError("Failed to create filesystem: {Error}", mkfsResult.Error);
                    await CompleteOperationAsync(
                        operationId,
                        false,
                        $"Failed to create filesystem: {mkfsResult.Error}"
                    );
                    return;
                }
                
                // Update status
                await UpdateOperationStatusAsync(
                    operationId,
                    PoolOperationStatus.Running,
                    "Mounting filesystem",
                    80
                );
                
                // Ensure mount directory exists
                if (!Directory.Exists(mountPath))
                {
                    Directory.CreateDirectory(mountPath);
                }
                
                // Mount the filesystem
                string mountCommand = $"mount {devicePath} {mountPath}";
                
                var mountResult = await _commandService.ExecuteCommandAsync(mountCommand, true);
                
                if (!mountResult.Success)
                {
                    _logger.LogError("Failed to mount filesystem: {Error}", mountResult.Error);
                    await CompleteOperationAsync(
                        operationId,
                        false,
                        $"Failed to mount filesystem: {mountResult.Error}"
                    );
                    return;
                }
                
                // Update metadata
                metadata.IsMounted = true;
                await _metadataService.UpdatePoolMetadataAsync(metadata);
                
                // Update status
                await UpdateOperationStatusAsync(
                    operationId,
                    PoolOperationStatus.Running,
                    "Updating system configuration",
                    90
                );
                
                // Update mdadm.conf to ensure array is assembled on boot
                string updateConfCommand = $"mdadm --detail --scan >> /etc/mdadm/mdadm.conf";
                var updateConfResult = await _commandService.ExecuteCommandAsync(updateConfCommand, true);
                
                if (!updateConfResult.Success)
                {
                    _logger.LogWarning("Failed to update mdadm.conf: {Error}", updateConfResult.Error);
                    // Non-critical error, continue
                }
                
                // Complete the operation
                await CompleteOperationAsync(
                    operationId,
                    true,
                    $"Successfully created and mounted {raidLevel} pool at {mountPath}"
                );
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Pool creation operation {OperationId} was cancelled", operationId);
                
                // The operation was already marked as cancelled in the CancelOperationAsync method
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during pool creation operation {OperationId}", operationId);
                
                await CompleteOperationAsync(
                    operationId,
                    false,
                    $"Error during pool creation: {ex.Message}"
                );
            }
        }
        
        /// <summary>
        /// Removes a pool asynchronously
        /// </summary>
        public async Task RemovePoolAsync(Guid operationId, Guid poolGroupGuid, PoolMetadata metadata)
        {
            try
            {
                // Use the provided operation ID or create a new one
                var operation = await GetOperationAsync(operationId);
                if (operation == null)
                {
                    operation = await RegisterOperationAsync(
                        poolGroupGuid,
                        PoolOperationType.RemovePool,
                        $"Removing pool {metadata.Label} ({poolGroupGuid})"
                    );
                    operationId = operation.OperationId;
                }
                
                await UpdateOperationStatusAsync(
                    operationId,
                    PoolOperationStatus.Running,
                    "Preparing to remove pool",
                    0
                );
                
                // Get cancellation token
                CancellationToken cancellationToken = CancellationToken.None;
                if (_cancellationTokens.TryGetValue(operationId, out var tokenSource))
                {
                    cancellationToken = tokenSource.Token;
                }
                
                // Convert the GUID to the mdadm UUID format (no dashes)
                string mdadmUuid = poolGroupGuid.ToString("N");
                string devicePath = $"/dev/md/{mdadmUuid}";
                
                // Update status
                await UpdateOperationStatusAsync(
                    operationId,
                    PoolOperationStatus.Running,
                    "Stopping RAID array",
                    50
                );
                
                // Stop the array
                string stopCommand = $"mdadm --stop {devicePath}";
                var stopResult = await _commandService.ExecuteCommandAsync(stopCommand, true);
                
                if (!stopResult.Success)
                {
                    _logger.LogError("Failed to stop RAID array: {Error}", stopResult.Error);
                    await CompleteOperationAsync(
                        operationId,
                        false,
                        $"Failed to stop RAID array: {stopResult.Error}"
                    );
                    return;
                }
                
                // Update status
                await UpdateOperationStatusAsync(
                    operationId,
                    PoolOperationStatus.Running,
                    "Zeroing superblocks on drives",
                    70
                );
                
                // Zero the superblocks on all drives
                foreach (var drive in metadata.Drives)
                {
                    string deviceId = drive.Key;
                    string diskPath = $"/dev/disk/by-id/{deviceId}";
                    
                    string zeroCommand = $"mdadm --zero-superblock {diskPath}";
                    var zeroResult = await _commandService.ExecuteCommandAsync(zeroCommand, true);
                    
                    if (!zeroResult.Success)
                    {
                        _logger.LogWarning("Failed to zero superblock on drive {DeviceId}: {Error}", 
                            deviceId, zeroResult.Error);
                        // Non-critical error, continue with other drives
                    }
                }
                
                // Update status
                await UpdateOperationStatusAsync(
                    operationId,
                    PoolOperationStatus.Running,
                    "Removing pool metadata",
                    90
                );
                
                // Remove the pool metadata
                var removeResult = await _metadataService.RemovePoolMetadataAsync(poolGroupGuid);
                
                if (!removeResult.Success)
                {
                    _logger.LogError("Failed to remove pool metadata: {Error}", removeResult.ErrorMessage);
                    await CompleteOperationAsync(
                        operationId,
                        false,
                        $"Failed to remove pool metadata: {removeResult.ErrorMessage}"
                    );
                    return;
                }
                
                // Complete the operation
                await CompleteOperationAsync(
                    operationId,
                    true,
                    $"Successfully removed pool {metadata.Label} ({poolGroupGuid})"
                );
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Pool removal operation {OperationId} was cancelled", operationId);
                
                // The operation was already marked as cancelled in the CancelOperationAsync method
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during pool removal operation {OperationId}", operationId);
                
                await CompleteOperationAsync(
                    operationId,
                    false,
                    $"Error during pool removal: {ex.Message}"
                );
            }
        }
    }
}