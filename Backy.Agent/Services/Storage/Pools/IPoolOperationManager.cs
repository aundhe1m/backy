using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Backy.Agent.Models;

namespace Backy.Agent.Services.Storage.Pools
{
    /// <summary>
    /// Interface for managing asynchronous pool operations.
    /// </summary>
    /// <remarks>
    /// This service is responsible for:
    /// - Tracking long-running pool operations
    /// - Providing status updates for ongoing operations
    /// - Managing operation queues and priorities
    /// - Handling operation failures and retries
    /// - Maintaining operation history
    /// 
    /// Enables fire-and-forget pool operations with status tracking.
    /// </remarks>
    public interface IPoolOperationManager
    {
        // Pool operation management methods will be defined here
    }
}