using System;

namespace Backy.Agent.Models
{
    /// <summary>
    /// Defines the different types of operations that can be performed on storage pools
    /// </summary>
    public enum PoolOperationType
    {
        /// <summary>
        /// Creating a new pool
        /// </summary>
        Create,
        
        /// <summary>
        /// Mounting an existing pool
        /// </summary>
        Mount,
        
        /// <summary>
        /// Unmounting a pool
        /// </summary>
        Unmount,
        
        /// <summary>
        /// Expanding a pool with additional drives
        /// </summary>
        Expand,
        
        /// <summary>
        /// Scrubbing a pool to check for and fix errors
        /// </summary>
        Scrub,
        
        /// <summary>
        /// Adding a drive to a pool
        /// </summary>
        AddDrive,
        
        /// <summary>
        /// Removing a drive from a pool
        /// </summary>
        RemoveDrive,
        
        /// <summary>
        /// Rebalancing data across drives in a pool
        /// </summary>
        Rebalance,
        
        /// <summary>
        /// Checking a pool for errors
        /// </summary>
        Check,
        
        /// <summary>
        /// Repairing a pool
        /// </summary>
        Repair,
        
        /// <summary>
        /// General maintenance operation
        /// </summary>
        Maintenance,
        
        /// <summary>
        /// Custom operation not covered by other types
        /// </summary>
        Custom
    }
}