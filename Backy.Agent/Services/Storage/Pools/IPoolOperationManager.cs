using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Backy.Agent.Models;

namespace Backy.Agent.Services.Storage.Pools
{
    /// <summary>
    /// Defines methods for managing asynchronous pool operations including tracking, status updates, 
    /// operation queues, and failure handling.
    /// </summary>
    public interface IPoolOperationManager
    {
        /// <summary>
        /// Registers a new pool operation and returns its unique identifier
        /// </summary>
        /// <param name="poolGroupGuid">The GUID of the pool group</param>
        /// <param name="operationType">The type of operation</param>
        /// <param name="description">Human-readable description of the operation</param>
        /// <param name="canBeCancelled">Whether the operation can be cancelled</param>
        /// <returns>The newly created operation with its unique identifier</returns>
        Task<PoolOperation> RegisterOperationAsync(Guid poolGroupGuid, PoolOperationType operationType, string description, bool canBeCancelled = true);

        /// <summary>
        /// Updates the status of an existing operation
        /// </summary>
        /// <param name="operationId">The unique identifier of the operation</param>
        /// <param name="status">The new status</param>
        /// <param name="statusMessage">Optional status message providing more details</param>
        /// <param name="progressPercentage">Optional progress percentage (0-100)</param>
        /// <returns>True if the operation was updated successfully, false otherwise</returns>
        Task<bool> UpdateOperationStatusAsync(Guid operationId, PoolOperationStatus status, string? statusMessage = null, int? progressPercentage = null);

        /// <summary>
        /// Completes an operation with the specified result
        /// </summary>
        /// <param name="operationId">The unique identifier of the operation</param>
        /// <param name="success">Whether the operation completed successfully</param>
        /// <param name="resultMessage">Optional result message</param>
        /// <param name="detailedResult">Optional detailed result object</param>
        /// <returns>True if the operation was completed successfully, false otherwise</returns>
        Task<bool> CompleteOperationAsync(Guid operationId, bool success, string? resultMessage = null, object? detailedResult = null);

        /// <summary>
        /// Gets the status of an operation
        /// </summary>
        /// <param name="operationId">The unique identifier of the operation</param>
        /// <returns>The operation if found, null otherwise</returns>
        Task<PoolOperation?> GetOperationAsync(Guid operationId);

        /// <summary>
        /// Gets all operations for a specific pool
        /// </summary>
        /// <param name="poolGroupGuid">The GUID of the pool group</param>
        /// <param name="includeCompleted">Whether to include completed operations</param>
        /// <returns>A list of operations for the specified pool</returns>
        Task<IEnumerable<PoolOperation>> GetOperationsForPoolAsync(Guid poolGroupGuid, bool includeCompleted = false);

        /// <summary>
        /// Gets all active operations
        /// </summary>
        /// <param name="includeCompleted">Whether to include completed operations</param>
        /// <returns>A list of all active operations</returns>
        Task<IEnumerable<PoolOperation>> GetAllOperationsAsync(bool includeCompleted = false);
    }
}