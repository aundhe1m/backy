using System;

namespace Backy.Agent.Models
{
    /// <summary>
    /// Defines the possible statuses of a pool operation
    /// </summary>
    public enum PoolOperationStatus
    {
        /// <summary>
        /// Operation has been created but not yet started
        /// </summary>
        Pending,
        
        /// <summary>
        /// Operation is currently in the queue waiting to be processed
        /// </summary>
        Queued,
        
        /// <summary>
        /// Operation is currently running
        /// </summary>
        Running,
        
        /// <summary>
        /// Operation is paused and can be resumed
        /// </summary>
        Paused,
        
        /// <summary>
        /// Operation has been cancelled
        /// </summary>
        Cancelled,
        
        /// <summary>
        /// Operation completed successfully
        /// </summary>
        Completed,
        
        /// <summary>
        /// Operation failed
        /// </summary>
        Failed,
        
        /// <summary>
        /// Operation timed out
        /// </summary>
        TimedOut
    }
}