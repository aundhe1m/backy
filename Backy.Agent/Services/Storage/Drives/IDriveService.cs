using System.Collections.Generic;
using System.Threading.Tasks;
using Backy.Agent.Models;
using Backy.Agent.Services.Core;

namespace Backy.Agent.Services.Storage.Drives
{
    /// <summary>
    /// Interface for high-level drive operations that combines information retrieval and management.
    /// </summary>
    /// <remarks>
    /// This service is responsible for:
    /// - Providing a unified API for drive operations
    /// - Exposing drive information with appropriate caching
    /// - Managing protected drive status
    /// - Handling drive operation results with proper error reporting
    /// - Enforcing business rules for drive operations
    /// 
    /// Acts as an aggregate facade over lower-level drive services.
    /// </remarks>
    public interface IDriveService
    {
        /// <summary>
        /// Gets all drives in the system
        /// </summary>
        /// <param name="includeDetails">Whether to include detailed information for each drive</param>
        /// <param name="useCache">Whether to use cached information</param>
        /// <returns>A collection of drives with their information</returns>
        Task<Result<IEnumerable<DriveInfo>>> GetDrivesAsync(bool includeDetails = false, bool useCache = true);
        
        /// <summary>
        /// Gets detailed information about a specific drive
        /// </summary>
        /// <param name="diskIdName">The disk ID name</param>
        /// <param name="useCache">Whether to use cached information</param>
        /// <returns>Detailed drive information</returns>
        Task<Result<DriveDetailInfo>> GetDriveDetailsAsync(string diskIdName, bool useCache = true);
        
        /// <summary>
        /// Gets the status of a drive (health, activity, etc.)
        /// </summary>
        /// <param name="diskIdName">The disk ID name</param>
        /// <returns>Drive status information</returns>
        Task<Result<DriveStatus>> GetDriveStatusAsync(string diskIdName);
        
        /// <summary>
        /// Refreshes drive information for all drives or a specific drive
        /// </summary>
        /// <param name="diskIdName">The disk ID name, or null for all drives</param>
        /// <param name="force">Whether to force a refresh even if one is already in progress</param>
        /// <returns>True if the refresh was successful</returns>
        Task<Result<bool>> RefreshDrivesAsync(string? diskIdName = null, bool force = false);
        
        /// <summary>
        /// Formats a drive with a specified filesystem
        /// </summary>
        /// <param name="request">The format request details</param>
        /// <returns>Result of the format operation</returns>
        Task<Result<CommandResponse>> FormatDriveAsync(DriveFormatRequest request);
        
        /// <summary>
        /// Sets drive power management settings
        /// </summary>
        /// <param name="diskIdName">The disk ID name</param>
        /// <param name="settings">The power management settings</param>
        /// <returns>Result of the operation</returns>
        Task<Result<CommandResponse>> SetDrivePowerManagementAsync(string diskIdName, DrivePowerSettings settings);
        
        /// <summary>
        /// Spins down a drive (puts it into standby mode)
        /// </summary>
        /// <param name="diskIdName">The disk ID name</param>
        /// <returns>Result of the operation</returns>
        Task<Result<CommandResponse>> SpinDownDriveAsync(string diskIdName);
        
        /// <summary>
        /// Spins up a drive (brings it out of standby mode)
        /// </summary>
        /// <param name="diskIdName">The disk ID name</param>
        /// <returns>Result of the operation</returns>
        Task<Result<CommandResponse>> SpinUpDriveAsync(string diskIdName);
        
        /// <summary>
        /// Gets processes using a drive
        /// </summary>
        /// <param name="diskIdName">The disk ID name</param>
        /// <returns>Collection of processes using the drive</returns>
        Task<Result<IEnumerable<ProcessInfo>>> GetProcessesUsingDriveAsync(string diskIdName);
        
        /// <summary>
        /// Kills processes using a drive
        /// </summary>
        /// <param name="diskIdName">The disk ID name</param>
        /// <param name="force">Whether to force kill the processes</param>
        /// <returns>Result of the operation including processes that were killed</returns>
        Task<Result<KillResponse>> KillProcessesUsingDriveAsync(string diskIdName, bool force = false);
        
        /// <summary>
        /// Checks if a drive is protected (part of a pool or otherwise in use)
        /// </summary>
        /// <param name="diskIdName">The disk ID name</param>
        /// <returns>True if the drive is protected, otherwise false</returns>
        Task<Result<bool>> IsDriveProtectedAsync(string diskIdName);
    }
}