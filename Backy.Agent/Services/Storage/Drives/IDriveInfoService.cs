using System.Collections.Generic;
using System.Threading.Tasks;
using Backy.Agent.Models;
using Backy.Agent.Services.Core;

namespace Backy.Agent.Services.Storage.Drives
{
    /// <summary>
    /// Interface for retrieving information about physical drives in the system.
    /// </summary>
    /// <remarks>
    /// This service is responsible for:
    /// - Retrieving information about physical drives with caching support
    /// - Providing detailed drive information including partitions
    /// - Looking up drives by various identifiers (serial, disk-id, device path)
    /// - Checking drive usage status
    /// - Managing drive information cache
    /// 
    /// Provides a consistent API for drive information with clear cache control.
    /// </remarks>
    public interface IDriveInfoService
    {
        /// <summary>
        /// Gets information about all drives in the system
        /// </summary>
        /// <param name="useCache">Whether to use cached information</param>
        /// <returns>Collection of drive information objects</returns>
        Task<IEnumerable<DriveInfo>> GetAllDrivesAsync(bool useCache = true);
        
        /// <summary>
        /// Gets a drive by its disk ID name
        /// </summary>
        /// <param name="diskIdName">The disk ID name</param>
        /// <param name="useCache">Whether to use cached information</param>
        /// <returns>Drive information, or null if not found</returns>
        Task<DriveInfo?> GetDriveByDiskIdNameAsync(string diskIdName, bool useCache = true);
        
        /// <summary>
        /// Gets a drive by its serial number
        /// </summary>
        /// <param name="serial">The drive serial number</param>
        /// <param name="useCache">Whether to use cached information</param>
        /// <returns>Drive information, or null if not found</returns>
        Task<DriveInfo?> GetDriveBySerialAsync(string serial, bool useCache = true);
        
        /// <summary>
        /// Gets a drive by its device path
        /// </summary>
        /// <param name="devicePath">The device path (e.g., /dev/sda)</param>
        /// <param name="useCache">Whether to use cached information</param>
        /// <returns>Drive information, or null if not found</returns>
        Task<DriveInfo?> GetDriveByDevicePathAsync(string devicePath, bool useCache = true);
        
        /// <summary>
        /// Gets detailed drive information including SMART data
        /// </summary>
        /// <param name="diskIdName">The disk ID name</param>
        /// <param name="useCache">Whether to use cached information</param>
        /// <returns>Detailed drive information, or null if not found</returns>
        Task<DriveDetailInfo?> GetDetailedDriveInfoAsync(string diskIdName, bool useCache = true);
        
        /// <summary>
        /// Checks if a drive is in use (mounted or part of a RAID array)
        /// </summary>
        /// <param name="diskIdName">The disk ID name</param>
        /// <returns>True if the drive is in use, otherwise false</returns>
        Task<bool> IsDriveInUseAsync(string diskIdName);
        
        /// <summary>
        /// Gets processes that are using a drive
        /// </summary>
        /// <param name="diskIdName">The disk ID name</param>
        /// <returns>Collection of process information objects</returns>
        Task<IEnumerable<ProcessInfo>> GetProcessesUsingDriveAsync(string diskIdName);
        
        /// <summary>
        /// Invalidates the cache for a specific drive
        /// </summary>
        /// <param name="diskIdName">The disk ID name</param>
        void InvalidateDriveCache(string diskIdName);
        
        /// <summary>
        /// Invalidates the cache for all drives
        /// </summary>
        void InvalidateAllDriveCache();
    }
}