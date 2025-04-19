// filepath: /home/aundhe1m/backy/Backy.Agent/Services/PoolOperationManager.cs
using Backy.Agent.Models;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Backy.Agent.Services;

/// <summary>
/// Service for managing pool operations as background tasks.
/// Provides methods to start, track, and retrieve results of asynchronous pool operations.
/// </summary>
public interface IPoolOperationManager
{
    /// <summary>
    /// Start a pool creation operation in the background
    /// </summary>
    /// <param name="request">The pool creation request</param>
    /// <returns>The pool group GUID that can be used to track the operation</returns>
    Task<Guid> StartPoolCreationAsync(PoolCreationRequest request);
    
    /// <summary>
    /// Get the current status of a pool operation
    /// </summary>
    /// <param name="poolGroupGuid">The pool group GUID</param>
    /// <returns>The operation status or null if not found</returns>
    Task<PoolOperationStatus?> GetOperationStatusAsync(Guid poolGroupGuid);
    
    /// <summary>
    /// Get the command outputs for a pool operation
    /// </summary>
    /// <param name="poolGroupGuid">The pool group GUID</param>
    /// <returns>The command outputs or null if not found</returns>
    Task<List<string>?> GetOperationOutputsAsync(Guid poolGroupGuid);
    
    /// <summary>
    /// Clean up old operation statuses
    /// </summary>
    Task CleanupOldOperationsAsync(TimeSpan olderThan);
}

public class PoolOperationManager : IPoolOperationManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PoolOperationManager> _logger;
    private readonly ConcurrentDictionary<Guid, PoolOperationStatus> _operationStatus = new();
    
    public PoolOperationManager(IServiceProvider serviceProvider, ILogger<PoolOperationManager> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }
    
    public Task<Guid> StartPoolCreationAsync(PoolCreationRequest request)
    {
        // Validate the request
        if (string.IsNullOrWhiteSpace(request.Label))
        {
            throw new ArgumentException("Pool label is required", nameof(request.Label));
        }

        if (request.DriveSerials == null || !request.DriveSerials.Any())
        {
            throw new ArgumentException("At least one drive is required", nameof(request.DriveSerials));
        }

        if (string.IsNullOrWhiteSpace(request.MountPath))
        {
            throw new ArgumentException("Mount path is required", nameof(request.MountPath));
        }

        if (!request.MountPath.StartsWith("/"))
        {
            throw new ArgumentException("Mount path must be absolute", nameof(request.MountPath));
        }
        
        // Generate or use the provided pool group GUID
        Guid poolGroupGuid;
        if (request is PoolCreationRequestExtended extendedRequest && 
            extendedRequest.PoolGroupGuid.HasValue && 
            extendedRequest.PoolGroupGuid.Value != Guid.Empty)
        {
            poolGroupGuid = extendedRequest.PoolGroupGuid.Value;
        }
        else
        {
            poolGroupGuid = Guid.NewGuid();
        }
        
        // Create initial operation status
        var status = new PoolOperationStatus
        {
            PoolGroupGuid = poolGroupGuid,
            Status = "creating",
            MountPath = request.MountPath
        };
        
        _operationStatus[poolGroupGuid] = status;
        
        // Start the pool creation task in the background
        _ = Task.Run(async () =>
        {
            // Create a scope to resolve scoped services
            using (var scope = _serviceProvider.CreateScope())
            {
                // Resolve the pool service from the DI container within this scope
                var poolService = scope.ServiceProvider.GetRequiredService<IPoolService>();
                
                try
                {
                    // Convert the request to extended request with the pool group GUID if needed
                    PoolCreationRequestExtended extendedRequest;
                    if (request is PoolCreationRequestExtended existingExtended)
                    {
                        extendedRequest = existingExtended;
                        if (extendedRequest.PoolGroupGuid == null || extendedRequest.PoolGroupGuid == Guid.Empty)
                        {
                            extendedRequest.PoolGroupGuid = poolGroupGuid;
                        }
                    }
                    else
                    {
                        extendedRequest = new PoolCreationRequestExtended
                        {
                            Label = request.Label,
                            DriveSerials = request.DriveSerials,
                            DriveLabels = request.DriveLabels,
                            MountPath = request.MountPath,
                            PoolGroupGuid = poolGroupGuid
                        };
                    }
                    
                    // Call the pool service to create the pool
                    var result = await poolService.CreatePoolAsync(extendedRequest);
                    
                    // Update the operation status with the result
                    if (result.Success)
                    {
                        if (_operationStatus.TryGetValue(poolGroupGuid, out var currentStatus))
                        {
                            currentStatus.CommandOutputs = result.Outputs;
                            currentStatus.MdDeviceName = result.MdDeviceName;
                            currentStatus.MountPath = result.MountPath;
                            
                            // Before marking as complete, verify metadata is accessible
                            // Wait for metadata to be available
                            await EnsureMetadataIsAccessibleAsync(poolService, poolGroupGuid);
                            
                            // Now that we've confirmed metadata is accessible, mark as active
                            currentStatus.Status = "active";
                            currentStatus.CompletedAt = DateTime.UtcNow;
                        }
                        
                        _logger.LogInformation("Pool {PoolGroupGuid} created successfully", poolGroupGuid);
                    }
                    else
                    {
                        if (_operationStatus.TryGetValue(poolGroupGuid, out var currentStatus))
                        {
                            currentStatus.Status = "failed";
                            currentStatus.ErrorMessage = result.Message;
                            currentStatus.CommandOutputs = result.Outputs;
                            currentStatus.CompletedAt = DateTime.UtcNow;
                        }
                        
                        _logger.LogError("Failed to create pool {PoolGroupGuid}: {ErrorMessage}", 
                            poolGroupGuid, result.Message);
                    }
                }
                catch (Exception ex)
                {
                    // Handle any exceptions during pool creation
                    if (_operationStatus.TryGetValue(poolGroupGuid, out var currentStatus))
                    {
                        currentStatus.Status = "failed";
                        currentStatus.ErrorMessage = $"Error creating pool: {ex.Message}";
                        currentStatus.CompletedAt = DateTime.UtcNow;
                    }
                    
                    _logger.LogError(ex, "Error creating pool {PoolGroupGuid}", poolGroupGuid);
                }
            }
        });
        
        return Task.FromResult(poolGroupGuid);
    }
    
    // Helper method to ensure metadata is accessible before marking the pool as active
    private async Task EnsureMetadataIsAccessibleAsync(IPoolService poolService, Guid poolGroupGuid)
    {
        const int maxRetries = 10;
        const int delayMs = 200;
        
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            var metadataResult = await poolService.GetPoolDetailByGuidAsync(poolGroupGuid);
            if (metadataResult.Success)
            {
                _logger.LogInformation("Metadata for pool {PoolGroupGuid} is now accessible after {Attempts} attempts", 
                    poolGroupGuid, attempt + 1);
                return;
            }
            
            // Metadata not yet accessible, wait before retrying
            _logger.LogDebug("Waiting for metadata to be accessible for pool {PoolGroupGuid}, attempt {Attempt}/{MaxRetries}",
                poolGroupGuid, attempt + 1, maxRetries);
                
            await Task.Delay(delayMs);
        }
        
        _logger.LogWarning("Metadata for pool {PoolGroupGuid} still not accessible after {MaxRetries} attempts", 
            poolGroupGuid, maxRetries);
    }
    
    public Task<PoolOperationStatus?> GetOperationStatusAsync(Guid poolGroupGuid)
    {
        _operationStatus.TryGetValue(poolGroupGuid, out var status);
        return Task.FromResult(status);
    }
    
    public Task<List<string>?> GetOperationOutputsAsync(Guid poolGroupGuid)
    {
        if (_operationStatus.TryGetValue(poolGroupGuid, out var status))
        {
            return Task.FromResult<List<string>?>(status.CommandOutputs);
        }
        
        return Task.FromResult<List<string>?>(null);
    }
    
    public Task CleanupOldOperationsAsync(TimeSpan olderThan)
    {
        var cutoffTime = DateTime.UtcNow.Subtract(olderThan);
        
        // Find all completed operations older than the specified time
        var keysToRemove = _operationStatus
            .Where(kv => kv.Value.CompletedAt.HasValue && kv.Value.CompletedAt.Value < cutoffTime)
            .Select(kv => kv.Key)
            .ToList();
        
        // Remove them from the dictionary
        foreach (var key in keysToRemove)
        {
            _operationStatus.TryRemove(key, out _);
            _logger.LogInformation("Cleaned up operation status for pool {PoolGroupGuid}", key);
        }
        
        return Task.CompletedTask;
    }
}