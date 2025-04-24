using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Backy.Agent.Models;
using Backy.Agent.Services.Storage.Pools;

namespace Backy.Agent.Services.Storage.Pools
{
    /// <summary>
    /// Background service that cleans up old or stale pool operations.
    /// </summary>
    /// <remarks>
    /// This service:
    /// - Runs periodically to check for abandoned or expired operations
    /// - Cleans up operations that have completed but not been collected
    /// - Identifies and logs operations that may have stalled
    /// - Manages operation history retention policy
    /// - Ensures the operation tracking system doesn't grow unbounded
    /// 
    /// Works with PoolOperationManager to keep the operation tracking system
    /// clean and performant.
    /// </remarks>
    public class PoolOperationCleanupService : BackgroundService
    {
        private readonly ILogger<PoolOperationCleanupService> _logger;
        private readonly IPoolOperationManager _poolOperationManager;
        
        // Default cleanup interval and retention settings
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(1);
        private readonly TimeSpan _completedOperationsRetention = TimeSpan.FromDays(7);
        private readonly TimeSpan _failedOperationsRetention = TimeSpan.FromDays(30);
        private readonly TimeSpan _staleOperationThreshold = TimeSpan.FromHours(12);
        
        public PoolOperationCleanupService(
            ILogger<PoolOperationCleanupService> logger,
            IPoolOperationManager poolOperationManager,
            IOptions<PoolServiceOptions> options = null)
        {
            _logger = logger;
            _poolOperationManager = poolOperationManager;
            
            // Apply custom options if provided
            if (options != null)
            {
                var poolOptions = options.Value;
                if (poolOptions.CleanupIntervalMinutes > 0)
                {
                    _cleanupInterval = TimeSpan.FromMinutes(poolOptions.CleanupIntervalMinutes);
                }
                
                if (poolOptions.CompletedOperationRetentionDays > 0)
                {
                    _completedOperationsRetention = TimeSpan.FromDays(poolOptions.CompletedOperationRetentionDays);
                }
                
                if (poolOptions.FailedOperationRetentionDays > 0)
                {
                    _failedOperationsRetention = TimeSpan.FromDays(poolOptions.FailedOperationRetentionDays);
                }
                
                if (poolOptions.StaleOperationThresholdHours > 0)
                {
                    _staleOperationThreshold = TimeSpan.FromHours(poolOptions.StaleOperationThresholdHours);
                }
            }
        }
        
        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Pool operation cleanup service starting");
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PerformCleanup(stoppingToken);
                    
                    // Wait for the next interval
                    await Task.Delay(_cleanupInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Normal shutdown, no need to log
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during pool operation cleanup");
                    
                    // Wait a shorter time before retrying after error
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
            }
            
            _logger.LogInformation("Pool operation cleanup service stopping");
        }
        
        /// <summary>
        /// Performs the cleanup of old operations
        /// </summary>
        private async Task PerformCleanup(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Running pool operation cleanup");
            
            try
            {
                // Get all operations to check for stale operations
                var allOperations = await _poolOperationManager.GetAllOperationsAsync(true);
                var now = DateTime.UtcNow;
                int staleCount = 0;
                
                foreach (var operation in allOperations)
                {
                    // Check for stale operations (running but not updated recently)
                    if (operation.Status == PoolOperationStatus.Running || 
                        operation.Status == PoolOperationStatus.Pending)
                    {
                        var lastUpdateTime = operation.LastUpdated ?? operation.StartTime;
                        var timeSinceUpdate = now - lastUpdateTime;
                        
                        if (timeSinceUpdate > _staleOperationThreshold)
                        {
                            _logger.LogWarning("Detected stale operation: {OperationType} ({OperationId}) for pool {PoolGroupGuid} - " +
                                              "Last updated {TimeSinceUpdate} ago, status: {Status}", 
                                operation.OperationType, operation.OperationId, operation.PoolGroupGuid, 
                                timeSinceUpdate, operation.Status);
                            
                            // Could implement auto-fail or retry logic here for stale operations
                            // For now, just log and count them
                            staleCount++;
                        }
                    }
                }
                
                if (staleCount > 0)
                {
                    _logger.LogWarning("Found {StaleCount} stale operations", staleCount);
                }
                
                // Cleanup completed operations
                int completedCount = await _poolOperationManager.CleanupCompletedOperationsAsync(_completedOperationsRetention);
                
                // Cleanup failed operations (but keep them longer than completed ones)
                int failedCount = await _poolOperationManager.CleanupCompletedOperationsAsync(_failedOperationsRetention);
                
                if (completedCount > 0 || failedCount > 0)
                {
                    _logger.LogInformation("Cleaned up {CompletedCount} completed operations and {FailedCount} failed operations",
                        completedCount, failedCount);
                }
                else
                {
                    _logger.LogDebug("No operations needed cleanup");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during pool operation cleanup");
            }
        }
    }
    
    /// <summary>
    /// Configuration options for pool services
    /// </summary>
    public class PoolServiceOptions
    {
        /// <summary>
        /// Interval in minutes between cleanup operations
        /// </summary>
        public int CleanupIntervalMinutes { get; set; } = 60; // Default: 1 hour
        
        /// <summary>
        /// Number of days to retain completed operations
        /// </summary>
        public int CompletedOperationRetentionDays { get; set; } = 7; // Default: 7 days
        
        /// <summary>
        /// Number of days to retain failed operations
        /// </summary>
        public int FailedOperationRetentionDays { get; set; } = 30; // Default: 30 days
        
        /// <summary>
        /// Number of hours after which an operation is considered stale
        /// </summary>
        public int StaleOperationThresholdHours { get; set; } = 12; // Default: 12 hours
    }
}