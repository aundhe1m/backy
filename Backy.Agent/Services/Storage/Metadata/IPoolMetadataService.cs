using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Backy.Agent.Models;

namespace Backy.Agent.Services.Storage.Metadata
{
    /// <summary>
    /// Interface for managing pool metadata operations.
    /// </summary>
    /// <remarks>
    /// This service is responsible for:
    /// - Reading and writing pool metadata files
    /// 
    /// Provides basic metadata persistence operations.
    /// </remarks>
    public interface IPoolMetadataService
    {
        /// <summary>
        /// Gets all pool metadata
        /// </summary>
        Task<Result<IEnumerable<PoolMetadata>>> GetAllPoolMetadataAsync();
        
        /// <summary>
        /// Gets metadata for a specific pool
        /// </summary>
        Task<Result<PoolMetadata>> GetPoolMetadataAsync(Guid poolGroupGuid);
        
        /// <summary>
        /// Saves pool metadata
        /// </summary>
        Task<Result<bool>> SavePoolMetadataAsync(PoolMetadata metadata);
        
        /// <summary>
        /// Updates an existing pool's metadata
        /// </summary>
        Task<Result<bool>> UpdatePoolMetadataAsync(PoolMetadata metadata);
        
        /// <summary>
        /// Removes pool metadata
        /// </summary>
        Task<Result<bool>> RemovePoolMetadataAsync(Guid poolGroupGuid);
    }
}