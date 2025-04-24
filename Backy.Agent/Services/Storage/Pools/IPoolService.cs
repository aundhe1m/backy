using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Backy.Agent.Models;

namespace Backy.Agent.Services.Storage.Pools
{
    /// <summary>
    /// Interface for RAID pool management operations that combines information retrieval and manipulation.
    /// </summary>
    /// <remarks>
    /// This service is responsible for:
    /// - Providing a unified API for pool operations
    /// - Creating, mounting, unmounting, and removing pools
    /// - Managing pool component devices
    /// - Enforcing business rules for pool operations
    /// - Handling operation results with proper error reporting
    /// 
    /// Acts as an aggregate facade over specialized pool services.
    /// </remarks>
    public interface IPoolService : IPoolInfoService
    {
        /// <summary>
        /// Gets all pools in the system.
        /// </summary>
        Task<Result<IEnumerable<PoolInfo>>> GetPoolsAsync();
        
        /// <summary>
        /// Creates a new RAID pool with the specified drives.
        /// </summary>
        Task<Result<PoolCreationResponse>> CreatePoolAsync(PoolCreationRequest request);
        
        /// <summary>
        /// Mounts an existing pool at the specified path.
        /// </summary>
        Task<Result<CommandResponse>> MountPoolAsync(Guid poolGroupGuid, string? mountPath = null);
        
        /// <summary>
        /// Unmounts a pool.
        /// </summary>
        Task<Result<CommandResponse>> UnmountPoolAsync(Guid poolGroupGuid);
        
        /// <summary>
        /// Removes a pool from the system.
        /// </summary>
        Task<Result<CommandResponse>> RemovePoolAsync(Guid poolGroupGuid);
    }
}