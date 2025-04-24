using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Backy.Agent.Models;

namespace Backy.Agent.Services.Storage.Pools
{
    /// <summary>
    /// Interface for retrieving information about RAID pools in the system.
    /// </summary>
    /// <remarks>
    /// This service is responsible for:
    /// - Retrieving pool information with caching support
    /// - Providing detailed pool status and metrics
    /// - Gathering information about component drives in pools
    /// - Monitoring pool health and status
    /// - Reading pool metadata
    /// 
    /// Focused on read-only operations for pool information.
    /// </remarks>
    public interface IPoolInfoService
    {
        /// <summary>
        /// Gets size and usage information for a specific pool
        /// </summary>
        Task<Result<PoolSizeInfo>> GetPoolSizeInfoAsync(Guid poolGroupGuid);
        
        /// <summary>
        /// Gets size and usage information for all pools
        /// </summary>
        Task<Result<IEnumerable<PoolSizeInfo>>> GetAllPoolSizesAsync();
        
        /// <summary>
        /// Gets health and status information for a specific pool
        /// </summary>
        Task<Result<PoolHealthInfo>> GetPoolHealthInfoAsync(Guid poolGroupGuid);
        
        /// <summary>
        /// Gets detailed information including size, health, and component drives for a pool
        /// </summary>
        Task<Result<PoolDetailInfo>> GetPoolDetailInfoAsync(Guid poolGroupGuid);
        
        /// <summary>
        /// Checks if a pool with the specified GUID exists
        /// </summary>
        Task<Result<bool>> PoolExistsAsync(Guid poolGroupGuid);
        
        /// <summary>
        /// Gets basic information for all pools
        /// </summary>
        Task<Result<IEnumerable<PoolInfo>>> GetAllPoolsAsync();
        
        /// <summary>
        /// Gets basic information for a specific pool
        /// </summary>
        Task<Result<PoolInfo>> GetPoolInfoAsync(Guid poolGroupGuid);
        
        /// <summary>
        /// Checks if a pool is currently mounted
        /// </summary>
        Task<Result<bool>> IsPoolMountedAsync(Guid poolGroupGuid);
    }
}