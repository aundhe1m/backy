using Backy.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Backy.Services
{
    /// <summary>
    /// Interface for the Backy Agent client.
    /// </summary>
    public interface IBackyAgentClient
    {
        /// <summary>
        /// Gets the current connection health status of the agent.
        /// </summary>
        Task<(bool IsConnected, string StatusMessage)> GetConnectionStatusAsync();
        
        /// <summary>
        /// Gets a list of all drives in the system.
        /// </summary>
        Task<List<Drive>> GetDrivesAsync();
        
        /// <summary>
        /// Gets status information for a specific drive.
        /// </summary>
        /// <param name="serial">The serial number of the drive.</param>
        Task<DriveStatus> GetDriveStatusAsync(string serial);
        
        /// <summary>
        /// Creates a new pool from the selected drives.
        /// </summary>
        /// <param name="request">The request containing pool information and drive serials.</param>
        Task<(bool Success, string Message, List<string> Outputs)> CreatePoolAsync(CreatePoolRequest request);
        
        /// <summary>
        /// Gets detailed information about a specific pool.
        /// </summary>
        /// <param name="poolGroupGuid">The GUID of the pool group.</param>
        Task<(bool Success, string Message, string Output)> GetPoolDetailAsync(Guid poolGroupGuid);
        
        /// <summary>
        /// Lists all pools in the system.
        /// </summary>
        Task<List<PoolInfo>> GetPoolsAsync();
        
        /// <summary>
        /// Mounts a pool.
        /// </summary>
        /// <param name="poolGroupGuid">The GUID of the pool group to mount.</param>
        /// <param name="mountPath">Optional custom mount path. If not provided, the default path will be used.</param>
        Task<(bool Success, string Message)> MountPoolAsync(Guid poolGroupGuid, string? mountPath = null);
        
        /// <summary>
        /// Unmounts a pool.
        /// </summary>
        /// <param name="poolGroupGuid">The GUID of the pool group to unmount.</param>
        Task<(bool Success, string Message)> UnmountPoolAsync(Guid poolGroupGuid);
        
        /// <summary>
        /// Removes a pool group.
        /// </summary>
        /// <param name="poolGroupGuid">The GUID of the pool group to remove.</param>
        Task<(bool Success, string Message)> RemovePoolGroupAsync(Guid poolGroupGuid);
        
        /// <summary>
        /// Gets size information about a mount point.
        /// </summary>
        /// <param name="mountPoint">The mount point path.</param>
        Task<(long Size, long Used, long Available, string UsePercent)> GetMountPointSizeAsync(string mountPoint);
        
        /// <summary>
        /// Gets a list of processes using a mount point.
        /// </summary>
        /// <param name="mountPoint">The mount point path.</param>
        Task<List<ProcessInfo>> GetProcessesUsingMountPointAsync(string mountPoint);
        
        /// <summary>
        /// Force adds a drive to a pool.
        /// </summary>
        /// <param name="driveId">The ID of the drive to add.</param>
        /// <param name="poolGroupGuid">The GUID of the pool group to add the drive to.</param>
        /// <param name="devPath">The device path of the drive.</param>
        Task<(bool Success, string Message)> ForceAddDriveAsync(int driveId, Guid poolGroupGuid, string devPath);
        
        /// <summary>
        /// Kills processes using a pool and performs an action.
        /// </summary>
        /// <param name="request">The request containing process IDs and the action to perform.</param>
        Task<(bool Success, string Message, List<string> Outputs)> KillProcessesAsync(KillProcessesRequest request);
    }
    
    /// <summary>
    /// Represents the status of a drive as returned by the Backy Agent.
    /// </summary>
    public class DriveStatus
    {
        public string Status { get; set; } = "unknown";
        public bool InPool { get; set; } = false;
        public string? PoolId { get; set; } = null;
        public string? MountPoint { get; set; } = null;
        public List<ProcessInfo> Processes { get; set; } = new List<ProcessInfo>();
    }
}