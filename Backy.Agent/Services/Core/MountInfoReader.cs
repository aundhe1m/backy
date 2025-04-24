using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Backy.Agent.Models;

namespace Backy.Agent.Services.Core
{
    /// <summary>
    /// Interface for reading information about mounted filesystems.
    /// </summary>
    /// <remarks>
    /// This service is responsible for:
    /// - Reading mount information from /proc/mounts or similar sources
    /// - Providing structured data about mounted filesystems
    /// - Getting disk usage information for mount points
    /// - Looking up mount points by device path
    /// - Checking mount status of volumes
    /// 
    /// Centralizes all mount-related operations to avoid duplication in other services.
    /// </remarks>
    public interface IMountInfoReader
    {
        /// <summary>
        /// Gets information about all mounted filesystems
        /// </summary>
        /// <param name="useCache">Whether to use cached information</param>
        /// <returns>A collection of MountInfo objects</returns>
        Task<IEnumerable<MountInfo>> GetMountPointsAsync(bool useCache = true);
        
        /// <summary>
        /// Gets information about a specific mount point
        /// </summary>
        /// <param name="mountPath">The path where the filesystem is mounted</param>
        /// <param name="useCache">Whether to use cached information</param>
        /// <returns>MountInfo for the specified path, or null if not found</returns>
        Task<MountInfo?> GetMountInfoAsync(string mountPath, bool useCache = true);
        
        /// <summary>
        /// Gets information about a filesystem mounted from a specific device
        /// </summary>
        /// <param name="devicePath">The device path (e.g., /dev/sda1)</param>
        /// <param name="useCache">Whether to use cached information</param>
        /// <returns>MountInfo for the specified device, or null if not mounted</returns>
        Task<MountInfo?> GetMountInfoByDeviceAsync(string devicePath, bool useCache = true);
        
        /// <summary>
        /// Gets disk usage information for a mount point using .NET's DriveInfo
        /// </summary>
        /// <param name="mountPath">The path where the filesystem is mounted</param>
        /// <returns>A DriveInfo object with usage information</returns>
        Task<DriveInfo?> GetDriveInfoAsync(string mountPath);
        
        /// <summary>
        /// Checks if a device is currently mounted
        /// </summary>
        /// <param name="devicePath">The device path to check</param>
        /// <returns>True if the device is mounted, false otherwise</returns>
        Task<bool> IsDeviceMountedAsync(string devicePath);
        
        /// <summary>
        /// Checks if a path is a mount point
        /// </summary>
        /// <param name="path">The path to check</param>
        /// <returns>True if the path is a mount point, false otherwise</returns>
        Task<bool> IsMountPointAsync(string path);
        
        /// <summary>
        /// Invalidates the mount info cache to force a fresh read on next request
        /// </summary>
        void InvalidateMountInfoCache();
    }

    /// <summary>
    /// Implementation for reading information about mounted filesystems.
    /// </summary>
    /// <remarks>
    /// This service provides:
    /// - Reading and parsing mount information from /proc/mounts
    /// - Disk space and usage information via .NET DriveInfo
    /// - Mount point resolution and lookup capabilities
    /// - Status checking for mountpoints
    /// 
    /// Uses the FileSystemInfoService for reading system files and
    /// provides a clean abstraction for working with mount information.
    /// </remarks>
    public class MountInfoReader : IMountInfoReader
    {
        private readonly ILogger<MountInfoReader> _logger;
        private readonly IFileSystemInfoService _fileSystemInfoService;
        private readonly ISystemCommandService _commandService;
        private readonly IMemoryCache _cache;
        private readonly AgentSettings _settings;
        
        private const string MOUNTS_FILE_PATH = "/proc/mounts";
        private const string MOUNT_INFO_CACHE_KEY = "MountInfoList";
        
        public MountInfoReader(
            ILogger<MountInfoReader> logger,
            IFileSystemInfoService fileSystemInfoService,
            ISystemCommandService commandService,
            IMemoryCache cache,
            IOptions<AgentSettings> options)
        {
            _logger = logger;
            _fileSystemInfoService = fileSystemInfoService;
            _commandService = commandService;
            _cache = cache;
            _settings = options.Value;
        }
        
        /// <inheritdoc />
        public async Task<IEnumerable<MountInfo>> GetMountPointsAsync(bool useCache = true)
        {
            // Check cache first if requested
            if (useCache && _cache.TryGetValue(MOUNT_INFO_CACHE_KEY, out List<MountInfo>? cachedInfo) && cachedInfo != null)
            {
                _logger.LogDebug("Retrieved mount information from cache");
                return cachedInfo;
            }
            
            _logger.LogDebug("Reading mount information from {FilePath}", MOUNTS_FILE_PATH);
            
            try
            {
                // Use file-based approach as primary method
                string mountsContent = await _fileSystemInfoService.ReadFileAsync(MOUNTS_FILE_PATH, false);
                
                if (string.IsNullOrEmpty(mountsContent))
                {
                    // Fall back to command-based approach if file read fails
                    _logger.LogWarning("Could not read {FilePath} directly, falling back to command execution", MOUNTS_FILE_PATH);
                    var result = await _commandService.ExecuteCommandAsync("cat /proc/mounts");
                    
                    if (!result.Success)
                    {
                        _logger.LogError("Failed to read mounts using command fallback: {Error}", result.Error);
                        return new List<MountInfo>();
                    }
                    
                    mountsContent = result.Output;
                }
                
                // Parse the mounts content
                var mountInfoList = ParseMountsContent(mountsContent);
                
                // Cache the result with expiration based on settings
                _cache.Set(
                    MOUNT_INFO_CACHE_KEY,
                    mountInfoList,
                    new MemoryCacheEntryOptions().SetAbsoluteExpiration(
                        TimeSpan.FromSeconds(_settings.FileCacheTimeToLiveSeconds)
                    )
                );
                
                return mountInfoList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading or parsing mount information");
                return new List<MountInfo>();
            }
        }
        
        /// <inheritdoc />
        public async Task<MountInfo?> GetMountInfoAsync(string mountPath, bool useCache = true)
        {
            // Normalize the path by resolving any symlinks and removing trailing slashes
            mountPath = NormalizePath(mountPath);
            
            // Get all mount points
            var mountPoints = await GetMountPointsAsync(useCache);
            
            // Find the mount point for the given path
            return mountPoints.FirstOrDefault(m => NormalizePath(m.MountPoint) == mountPath);
        }
        
        /// <inheritdoc />
        public async Task<MountInfo?> GetMountInfoByDeviceAsync(string devicePath, bool useCache = true)
        {
            // Normalize the device path by resolving any symlinks
            devicePath = await ResolveDevicePath(devicePath);
            
            // Get all mount points
            var mountPoints = await GetMountPointsAsync(useCache);
            
            // Find the mount point for the given device
            return mountPoints.FirstOrDefault(m => m.Device == devicePath);
        }
        
        /// <inheritdoc />
        public Task<DriveInfo?> GetDriveInfoAsync(string mountPath)
        {
            try
            {
                // Normalize the path
                mountPath = NormalizePath(mountPath);
                
                // Check if the path exists
                if (!Directory.Exists(mountPath))
                {
                    _logger.LogWarning("Mount path does not exist: {MountPath}", mountPath);
                    return Task.FromResult<DriveInfo?>(null);
                }
                
                // Get the DriveInfo for the mount point
                var driveInfo = new DriveInfo(mountPath);
                
                // Check if the drive is ready
                if (!driveInfo.IsReady)
                {
                    _logger.LogWarning("Drive is not ready for mount path: {MountPath}", mountPath);
                    return Task.FromResult<DriveInfo?>(null);
                }
                
                return Task.FromResult<DriveInfo?>(driveInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting drive information for {MountPath}", mountPath);
                return Task.FromResult<DriveInfo?>(null);
            }
        }
        
        /// <inheritdoc />
        public async Task<bool> IsDeviceMountedAsync(string devicePath)
        {
            // Resolve the device path
            devicePath = await ResolveDevicePath(devicePath);
            
            // Get all mount points
            var mountPoints = await GetMountPointsAsync();
            
            // Check if any mount point is using the given device
            return mountPoints.Any(m => m.Device == devicePath);
        }
        
        /// <inheritdoc />
        public async Task<bool> IsMountPointAsync(string path)
        {
            // Normalize the path
            path = NormalizePath(path);
            
            // Get all mount points
            var mountPoints = await GetMountPointsAsync();
            
            // Check if the path is a mount point
            return mountPoints.Any(m => NormalizePath(m.MountPoint) == path);
        }
        
        /// <inheritdoc />
        public void InvalidateMountInfoCache()
        {
            _cache.Remove(MOUNT_INFO_CACHE_KEY);
            _logger.LogDebug("Invalidated mount info cache");
        }
        
        /// <summary>
        /// Parses the content of /proc/mounts into a list of MountInfo objects
        /// </summary>
        private List<MountInfo> ParseMountsContent(string content)
        {
            var mountInfoList = new List<MountInfo>();
            
            if (string.IsNullOrWhiteSpace(content))
            {
                return mountInfoList;
            }
            
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                try
                {
                    // Each line in /proc/mounts has the format:
                    // device mountpoint filesystem options dump fsck
                    var fields = line.Split(new[] { ' ' }, 6, StringSplitOptions.RemoveEmptyEntries);
                    
                    if (fields.Length < 4)
                    {
                        continue;
                    }
                    
                    // Decode escaped characters in device and mount point
                    string device = DecodeEscapedCharacters(fields[0]);
                    string mountPoint = DecodeEscapedCharacters(fields[1]);
                    string filesystem = fields[2];
                    string options = fields[3];
                    
                    // Skip pseudo filesystems if needed
                    if (ShouldSkipFilesystem(filesystem))
                    {
                        continue;
                    }
                    
                    var mountInfo = new MountInfo
                    {
                        Device = device,
                        MountPoint = mountPoint,
                        FilesystemType = filesystem,
                        Options = options,
                        IsReadOnly = options.Contains("ro,") || options.EndsWith("ro")
                    };
                    
                    mountInfoList.Add(mountInfo);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error parsing mount information line: {Line}", line);
                }
            }
            
            return mountInfoList;
        }
        
        /// <summary>
        /// Decodes escaped octal sequences like \040 (space) in device and mount point strings
        /// </summary>
        private string DecodeEscapedCharacters(string input)
        {
            // Replace octal escape sequences (\040 = space, \011 = tab, etc.)
            return Regex.Replace(input, @"\\(\d{3})", match => 
            {
                // Convert octal to decimal and then to char
                if (int.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.AllowOctalSpecifier, null, out int ascii))
                {
                    return ((char)ascii).ToString();
                }
                return match.Value;
            });
        }
        
        /// <summary>
        /// Determines if a filesystem type should be skipped
        /// </summary>
        private bool ShouldSkipFilesystem(string filesystem)
        {
            // List of pseudo filesystems that we might want to skip
            var pseudoFilesystems = new[] 
            { 
                "proc", "sysfs", "devpts", "devtmpfs", "tmpfs", "securityfs", "cgroup", 
                "pstore", "debugfs", "configfs", "selinuxfs", "autofs", "mqueue", "hugetlbfs",
                "fusectl", "rpc_pipefs", "binfmt_misc", "fuse.gvfsd-fuse", "efivarfs"
            };
            
            // Only skip if configured to do so
            if (_settings.IncludePseudoFilesystems == false)
            {
                return pseudoFilesystems.Contains(filesystem);
            }
            
            return false;
        }
        
        /// <summary>
        /// Normalizes a path by removing trailing slashes and resolving symlinks
        /// </summary>
        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }
            
            // Remove trailing slashes except for root
            path = path == "/" ? path : path.TrimEnd('/');
            
            try
            {
                // Try to resolve the real path if it exists
                if (Directory.Exists(path))
                {
                    var info = new DirectoryInfo(path);
                    return info.FullName;
                }
                
                return path;
            }
            catch
            {
                // If there's any error, just return the original path
                return path;
            }
        }
        
        /// <summary>
        /// Resolves a device path to its canonical form by following symlinks
        /// </summary>
        private async Task<string> ResolveDevicePath(string devicePath)
        {
            try
            {
                if (string.IsNullOrEmpty(devicePath))
                {
                    return devicePath;
                }
                
                // If it's already a real path, return it
                if (File.Exists(devicePath) && !IsSymlink(devicePath))
                {
                    return devicePath;
                }
                
                // Use readlink -f to follow all symlinks to the real device
                var result = await _commandService.ExecuteCommandAsync($"readlink -f {devicePath}");
                
                if (result.Success && !string.IsNullOrEmpty(result.Output))
                {
                    return result.Output.Trim();
                }
                
                return devicePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving device path: {DevicePath}", devicePath);
                return devicePath;
            }
        }
        
        /// <summary>
        /// Checks if a path is a symlink
        /// </summary>
        private bool IsSymlink(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return info.Attributes.HasFlag(FileAttributes.ReparsePoint);
            }
            catch
            {
                // If there's any error, assume it's not a symlink
                return false;
            }
        }
    }
    
    /// <summary>
    /// Represents information about a mounted filesystem
    /// </summary>
    public class MountInfo
    {
        /// <summary>
        /// The device that is mounted (e.g., /dev/sda1)
        /// </summary>
        public string Device { get; set; } = string.Empty;
        
        /// <summary>
        /// The path where the filesystem is mounted (e.g., /mnt/data)
        /// </summary>
        public string MountPoint { get; set; } = string.Empty;
        
        /// <summary>
        /// The type of the filesystem (e.g., ext4, btrfs)
        /// </summary>
        public string FilesystemType { get; set; } = string.Empty;
        
        /// <summary>
        /// The mount options string (e.g., rw,relatime)
        /// </summary>
        public string Options { get; set; } = string.Empty;
        
        /// <summary>
        /// Whether the filesystem is mounted read-only
        /// </summary>
        public bool IsReadOnly { get; set; }
    }
}