using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Backy.Agent.Models;

namespace Backy.Agent.Services
{
    public interface IDriveInfoService
    {
        /// <summary>
        /// Gets disk space information for a mount point
        /// </summary>
        /// <param name="mountPoint">The path where the filesystem is mounted</param>
        /// <returns>Tuple with Size, Used, Available, and Use Percentage</returns>
        Task<(long Size, long Used, long Available, string UsePercent)> GetDiskSpaceInfoAsync(string mountPoint);
        
        /// <summary>
        /// Gets all mounted filesystems
        /// </summary>
        /// <returns>List of MountInfo objects with details about each mounted filesystem</returns>
        Task<List<MountInfo>> GetMountedFilesystemsAsync();
        
        /// <summary>
        /// Checks if a path is a mount point
        /// </summary>
        /// <param name="path">Path to check</param>
        /// <returns>True if the path is a mount point</returns>
        bool IsMountPoint(string path);
    }

    public class DriveInfoService : IDriveInfoService
    {
        private readonly ILogger<DriveInfoService> _logger;
        private readonly IFileSystemInfoService _fileSystemInfoService;
        private readonly ISystemCommandService _commandService;
        
        private const string MOUNTS_FILE_PATH = "/proc/mounts";
        
        public DriveInfoService(
            ILogger<DriveInfoService> logger,
            IFileSystemInfoService fileSystemInfoService,
            ISystemCommandService commandService)
        {
            _logger = logger;
            _fileSystemInfoService = fileSystemInfoService;
            _commandService = commandService;
        }
        
        /// <inheritdoc />
        public Task<(long Size, long Used, long Available, string UsePercent)> GetDiskSpaceInfoAsync(string mountPoint)
        {
            try
            {
                // Use DriveInfo for getting disk space information
                var driveInfo = new DriveInfo(mountPoint);
                
                if (!driveInfo.IsReady)
                {
                    _logger.LogWarning("Drive is not ready: {MountPoint}", mountPoint);
                    return Task.FromResult((0L, 0L, 0L, "0%"));
                }
                
                long size = driveInfo.TotalSize;
                long available = driveInfo.AvailableFreeSpace;
                long totalFree = driveInfo.TotalFreeSpace;
                long used = size - totalFree;
                
                // Calculate percentage used
                double percentUsed = size > 0 ? (double)used / size * 100 : 0;
                string usePercent = $"{percentUsed:F2}%";
                
                _logger.LogDebug("Percent used: {PercentUsed}", percentUsed);
                _logger.LogDebug("Disk space for {MountPoint}: Size={Size}, Used={Used}, Available={Available}, UsePercent={UsePercent}", 
                    mountPoint, size, used, available, usePercent);
                
                return Task.FromResult((size, used, available, usePercent));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting disk space info for {MountPoint}", mountPoint);
                return Task.FromResult((0L, 0L, 0L, "0%"));
            }
        }

        /// <inheritdoc />
        public async Task<List<MountInfo>> GetMountedFilesystemsAsync()
        {
            var mounts = new List<MountInfo>();
            
            try
            {
                // Read /proc/mounts file for a list of all mounted filesystems
                string mountsContent = await _fileSystemInfoService.ReadFileAsync(MOUNTS_FILE_PATH);
                
                // Parse each line
                var lines = mountsContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split(' ');
                    if (parts.Length >= 4)
                    {
                        var mount = new MountInfo
                        {
                            Device = parts[0],
                            MountPoint = parts[1].Replace("\\040", " "), // Replace escaped spaces
                            FilesystemType = parts[2],
                            Options = parts[3]
                        };
                        
                        mounts.Add(mount);
                    }
                }
                
                _logger.LogDebug("Retrieved {Count} mounted filesystems", mounts.Count);
                return mounts;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading mounted filesystems");
                return mounts;
            }
        }

        /// <inheritdoc />
        public bool IsMountPoint(string path)
        {
            try
            {
                // Normalize path
                path = Path.GetFullPath(path);
                
                var driveInfos = DriveInfo.GetDrives();
                
                // Check if the path matches any of the system's mount points
                return driveInfos.Any(d => string.Equals(d.Name.TrimEnd('/'), path.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(d.RootDirectory.FullName.TrimEnd('/'), path.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if {Path} is a mount point", path);
                return false;
            }
        }
    }
}