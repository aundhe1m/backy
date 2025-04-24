using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Backy.Agent.Models;

namespace Backy.Agent.Services.Core
{
    /// <summary>
    /// Interface for file system operations with caching and error handling support.
    /// </summary>
    /// <remarks>
    /// This service is responsible for:
    /// - Reading and writing files with caching capabilities
    /// - Checking for file and directory existence
    /// - Listing directory contents
    /// - Reading from special system directories like /proc and /sys
    /// - Managing file operation caching for improved performance
    /// - Watching directories for changes
    /// 
    /// This interface centralizes all file system access, enabling consistent caching,
    /// error handling, and logging across the application.
    /// </remarks>
    public interface IFileSystemInfoService
    {
        /// <summary>
        /// Reads the content of a text file asynchronously, utilizing cache if available
        /// </summary>
        /// <param name="filePath">Path to the file to read</param>
        /// <param name="useCacheIfAvailable">Whether to use cached content if available</param>
        /// <returns>The content of the file as string, or empty string if file doesn't exist</returns>
        Task<string> ReadFileAsync(string filePath, bool useCacheIfAvailable = true);
        
        /// <summary>
        /// Writes content to a file asynchronously
        /// </summary>
        /// <param name="filePath">Path to the file to write</param>
        /// <param name="content">Content to write to the file</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> WriteFileAsync(string filePath, string content);
        
        /// <summary>
        /// Checks if a file exists asynchronously
        /// </summary>
        /// <param name="filePath">Path to check</param>
        /// <returns>True if file exists</returns>
        Task<bool> FileExistsAsync(string filePath);
        
        /// <summary>
        /// Checks if a directory exists asynchronously
        /// </summary>
        /// <param name="dirPath">Path to check</param>
        /// <returns>True if directory exists</returns>
        Task<bool> DirectoryExistsAsync(string dirPath);
        
        /// <summary>
        /// Gets list of directories within a specified path asynchronously
        /// </summary>
        /// <param name="dirPath">Parent directory path</param>
        /// <returns>List of directory names</returns>
        Task<IEnumerable<string>> GetDirectoriesAsync(string dirPath);
        
        /// <summary>
        /// Gets list of files within a specified path asynchronously
        /// </summary>
        /// <param name="dirPath">Parent directory path</param>
        /// <returns>List of file names</returns>
        Task<IEnumerable<string>> GetFilesAsync(string dirPath);
        
        /// <summary>
        /// Watches a directory for changes and executes the provided action when changes occur
        /// </summary>
        /// <param name="dirPath">Directory to watch</param>
        /// <param name="onChange">Action to execute when changes occur</param>
        /// <returns>Task that completes when the watch operation is set up</returns>
        Task WatchDirectoryForChangesAsync(string dirPath, Action<FileSystemEventArgs> onChange);
        
        /// <summary>
        /// Reads the content of a file from /proc directory
        /// </summary>
        /// <param name="procFileName">The name of the file in /proc directory (e.g., "mdstat")</param>
        /// <param name="useCacheIfAvailable">Whether to use cached content if available</param>
        /// <returns>The content of the file as string</returns>
        Task<string> ReadFromProcAsync(string procFileName, bool useCacheIfAvailable = true);
        
        /// <summary>
        /// Reads the content of a file from /sys directory
        /// </summary>
        /// <param name="sysPath">The relative path within /sys (e.g., "block/sda/size")</param>
        /// <param name="useCacheIfAvailable">Whether to use cached content if available</param>
        /// <returns>The content of the file as string</returns>
        Task<string> ReadFromSysAsync(string sysPath, bool useCacheIfAvailable = true);
        
        /// <summary>
        /// Reads multiple properties from a /sys device path
        /// </summary>
        /// <param name="devicePath">Relative path within /sys/block (e.g., "sda")</param>
        /// <param name="propertyNames">List of property files to read (e.g., ["size", "model", "serial"])</param>
        /// <returns>Dictionary mapping property names to values</returns>
        Task<Dictionary<string, string>> ReadSysBlockDevicePropertiesAsync(string devicePath, IEnumerable<string> propertyNames);
        
        /// <summary>
        /// Checks if a directory exists
        /// </summary>
        /// <param name="dirPath">Path to check</param>
        /// <returns>True if directory exists</returns>
        bool DirectoryExists(string dirPath);
        
        /// <summary>
        /// Checks if a file exists
        /// </summary>
        /// <param name="filePath">Path to check</param>
        /// <returns>True if file exists</returns>
        bool FileExists(string filePath);
        
        /// <summary>
        /// Gets list of directories within a specified path
        /// </summary>
        /// <param name="dirPath">Parent directory path</param>
        /// <returns>List of directory names</returns>
        IEnumerable<string> GetDirectories(string dirPath);
        
        /// <summary>
        /// Gets list of files within a specified path
        /// </summary>
        /// <param name="dirPath">Parent directory path</param>
        /// <returns>List of file names</returns>
        IEnumerable<string> GetFiles(string dirPath);
        
        /// <summary>
        /// Invalidates cache for a specific file
        /// </summary>
        /// <param name="filePath">Path of file whose cache should be invalidated</param>
        void InvalidateFileCache(string filePath);
    }

    /// <summary>
    /// Implementation of file system operations with caching and robust error handling.
    /// </summary>
    /// <remarks>
    /// This service provides:
    /// - Cached file reads for improved performance
    /// - Direct access to system information in /proc and /sys
    /// - Unified error handling for all file operations
    /// - Directory and file enumeration
    /// - Cache invalidation mechanisms
    /// 
    /// The service reduces duplicate code and implements consistent caching
    /// strategies, with configurable TTL from application settings.
    /// </remarks>
    public class FileSystemInfoService : IFileSystemInfoService
    {
        private readonly ILogger<FileSystemInfoService> _logger;
        private readonly IMemoryCache _cache;
        private readonly AgentSettings _settings;
        
        private const string PROC_BASE_PATH = "/proc";
        private const string SYS_BASE_PATH = "/sys";
        
        // Dictionary to track active directory watchers
        private readonly Dictionary<string, FileSystemWatcher> _activeWatchers = new Dictionary<string, FileSystemWatcher>();
        
        public FileSystemInfoService(
            ILogger<FileSystemInfoService> logger,
            IMemoryCache cache,
            IOptions<AgentSettings> options)
        {
            _logger = logger;
            _cache = cache;
            _settings = options.Value;
        }
        
        /// <inheritdoc />
        public async Task<string> ReadFileAsync(string filePath, bool useCacheIfAvailable = true)
        {
            // First check cache if requested
            if (useCacheIfAvailable && _cache.TryGetValue(filePath, out string? cachedContent) && cachedContent != null)
            {
                _logger.LogDebug("Retrieved file content from cache for {FilePath}", filePath);
                return cachedContent;
            }
            
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("File does not exist: {FilePath}", filePath);
                return string.Empty;
            }
            
            try
            {
                _logger.LogDebug("Reading file: {FilePath}", filePath);
                string content = await File.ReadAllTextAsync(filePath);
                
                // Cache the result if requested
                if (useCacheIfAvailable)
                {
                    _cache.Set(
                        filePath,
                        content,
                        new MemoryCacheEntryOptions().SetAbsoluteExpiration(
                            TimeSpan.FromSeconds(_settings.FileCacheTimeToLiveSeconds)
                        )
                    );
                }
                
                return content;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading file {FilePath}", filePath);
                return string.Empty;
            }
        }
        
        /// <inheritdoc />
        public async Task<bool> WriteFileAsync(string filePath, string content)
        {
            try
            {
                // Ensure directory exists
                string? directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                await File.WriteAllTextAsync(filePath, content);
                
                // Invalidate cache for this file
                InvalidateFileCache(filePath);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing to file {FilePath}", filePath);
                return false;
            }
        }
        
        /// <inheritdoc />
        public Task<bool> FileExistsAsync(string filePath)
        {
            return Task.FromResult(FileExists(filePath));
        }
        
        /// <inheritdoc />
        public Task<bool> DirectoryExistsAsync(string dirPath)
        {
            return Task.FromResult(DirectoryExists(dirPath));
        }
        
        /// <inheritdoc />
        public Task<IEnumerable<string>> GetDirectoriesAsync(string dirPath)
        {
            return Task.FromResult(GetDirectories(dirPath));
        }
        
        /// <inheritdoc />
        public Task<IEnumerable<string>> GetFilesAsync(string dirPath)
        {
            return Task.FromResult(GetFiles(dirPath));
        }
        
        /// <inheritdoc />
        public Task WatchDirectoryForChangesAsync(string dirPath, Action<FileSystemEventArgs> onChange)
        {
            if (!Directory.Exists(dirPath))
            {
                _logger.LogWarning("Cannot watch directory that doesn't exist: {DirPath}", dirPath);
                return Task.CompletedTask;
            }
            
            // If already watching this directory, remove the old watcher
            if (_activeWatchers.TryGetValue(dirPath, out var existingWatcher))
            {
                existingWatcher.EnableRaisingEvents = false;
                existingWatcher.Dispose();
                _activeWatchers.Remove(dirPath);
            }
            
            try
            {
                var watcher = new FileSystemWatcher(dirPath)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };
                
                watcher.Created += (sender, e) => onChange(e);
                watcher.Deleted += (sender, e) => onChange(e);
                watcher.Changed += (sender, e) => onChange(e);
                watcher.Renamed += (sender, e) => onChange(e);
                
                _activeWatchers[dirPath] = watcher;
                
                _logger.LogInformation("Started watching directory: {DirPath}", dirPath);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting up directory watcher for {DirPath}", dirPath);
                return Task.CompletedTask;
            }
        }
        
        /// <inheritdoc />
        public async Task<string> ReadFromProcAsync(string procFileName, bool useCacheIfAvailable = true)
        {
            string filePath = Path.Combine(PROC_BASE_PATH, procFileName);
            return await ReadFileAsync(filePath, useCacheIfAvailable);
        }
        
        /// <inheritdoc />
        public async Task<string> ReadFromSysAsync(string sysPath, bool useCacheIfAvailable = true)
        {
            string filePath = Path.Combine(SYS_BASE_PATH, sysPath);
            return await ReadFileAsync(filePath, useCacheIfAvailable);
        }
        
        /// <inheritdoc />
        public async Task<Dictionary<string, string>> ReadSysBlockDevicePropertiesAsync(
            string devicePath, IEnumerable<string> propertyNames)
        {
            var results = new Dictionary<string, string>();
            string basePath = Path.Combine(SYS_BASE_PATH, "block", devicePath);
            
            if (!DirectoryExists(basePath))
            {
                _logger.LogWarning("Device directory does not exist: {BasePath}", basePath);
                return results;
            }
            
            foreach (string propName in propertyNames)
            {
                try
                {
                    // Check direct path first
                    string directPath = Path.Combine(basePath, propName);
                    if (FileExists(directPath))
                    {
                        string value = await ReadFileAsync(directPath);
                        results[propName] = value.Trim();
                        continue;
                    }
                    
                    // Check device subdirectory
                    string deviceSubdirPath = Path.Combine(basePath, "device", propName);
                    if (FileExists(deviceSubdirPath))
                    {
                        string value = await ReadFileAsync(deviceSubdirPath);
                        results[propName] = value.Trim();
                        continue;
                    }
                    
                    // Not found
                    _logger.LogDebug("Property {Property} not found for device {Device}", propName, devicePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading property {Property} for device {Device}", 
                        propName, devicePath);
                }
            }
            
            return results;
        }
        
        /// <inheritdoc />
        public bool DirectoryExists(string dirPath)
        {
            return Directory.Exists(dirPath);
        }
        
        /// <inheritdoc />
        public bool FileExists(string filePath)
        {
            return File.Exists(filePath);
        }
        
        /// <inheritdoc />
        public IEnumerable<string> GetDirectories(string dirPath)
        {
            try
            {
                return Directory.GetDirectories(dirPath)
                    .Select(Path.GetFileName)
                    .Where(name => name != null)
                    .Cast<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting directories from {DirPath}", dirPath);
                return Enumerable.Empty<string>();
            }
        }
        
        /// <inheritdoc />
        public IEnumerable<string> GetFiles(string dirPath)
        {
            try
            {
                return Directory.GetFiles(dirPath)
                    .Select(Path.GetFileName)
                    .Where(name => name != null)
                    .Cast<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting files from {DirPath}", dirPath);
                return Enumerable.Empty<string>();
            }
        }
        
        /// <inheritdoc />
        public void InvalidateFileCache(string filePath)
        {
            _cache.Remove(filePath);
            _logger.LogDebug("Invalidated cache for {FilePath}", filePath);
        }
    }
}