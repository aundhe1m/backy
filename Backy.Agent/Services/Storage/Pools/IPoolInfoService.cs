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
        // Pool information retrieval methods will be defined here
    }
}