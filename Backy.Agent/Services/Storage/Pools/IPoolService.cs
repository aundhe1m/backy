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
        // Pool operation methods will be defined here
    }
}