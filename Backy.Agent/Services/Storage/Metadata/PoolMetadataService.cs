using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Backy.Agent.Models;
using Backy.Agent.Services.Core;

namespace Backy.Agent.Services.Storage.Metadata
{
    /// <summary>
    /// Manages basic pool metadata operations.
    /// </summary>
    /// <remarks>
    /// This service:
    /// - Reads and writes pool metadata files
    /// 
    /// Provides simple persistence for pool metadata.
    /// </remarks>
    public class PoolMetadataService : IPoolMetadataService
    {
        private readonly ILogger<PoolMetadataService> _logger;
        private readonly IFileSystemInfoService _fileSystemInfoService;
        private readonly AgentSettings _settings;
        
        // Metadata directory location
        private readonly string _metadataBasePath;
        
        // File path for metadata
        private readonly string _metadataFilePath;
        
        // Serialization options
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        
        // Semaphore to prevent concurrent metadata operations
        private readonly SemaphoreSlim _metadataLock = new(1, 1);
        
        public PoolMetadataService(
            ILogger<PoolMetadataService> logger,
            IFileSystemInfoService fileSystemInfoService,
            IOptions<AgentSettings> options)
        {
            _logger = logger;
            _fileSystemInfoService = fileSystemInfoService;
            _settings = options.Value;
            
            // Construct the base path for metadata storage
            _metadataBasePath = Path.Combine(_settings.DataPath, "metadata");
            
            // Ensure the metadata directory exists
            if (!_fileSystemInfoService.DirectoryExists(_metadataBasePath))
            {
                Directory.CreateDirectory(_metadataBasePath);
                _logger.LogInformation("Created metadata directory at {Path}", _metadataBasePath);
            }
            
            // Set up file path
            _metadataFilePath = Path.Combine(_metadataBasePath, "pools.json");
        }
        
        /// <inheritdoc />
        public async Task<Result<IEnumerable<PoolMetadata>>> GetAllPoolMetadataAsync()
        {
            try
            {
                await _metadataLock.WaitAsync();
                
                try
                {
                    var metadataExists = await _fileSystemInfoService.FileExistsAsync(_metadataFilePath);
                    if (!metadataExists)
                    {
                        _logger.LogDebug("No pool metadata file found, returning empty collection");
                        return Result<IEnumerable<PoolMetadata>>.Success(Enumerable.Empty<PoolMetadata>());
                    }
                    
                    var fileContent = await _fileSystemInfoService.ReadFileAsync(_metadataFilePath);
                    
                    var rootObject = JsonSerializer.Deserialize<MetadataRoot>(fileContent, _jsonOptions);
                    if (rootObject == null)
                    {
                        _logger.LogWarning("Failed to deserialize pool metadata file");
                        return Result<IEnumerable<PoolMetadata>>.Error("Invalid metadata format");
                    }
                    
                    return Result<IEnumerable<PoolMetadata>>.Success(rootObject.Pools);
                }
                finally
                {
                    _metadataLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading pool metadata");
                return Result<IEnumerable<PoolMetadata>>.Error($"Error reading metadata: {ex.Message}");
            }
        }
        
        /// <inheritdoc />
        public async Task<Result<PoolMetadata>> GetPoolMetadataAsync(Guid poolGroupGuid)
        {
            try
            {
                var metadataResult = await GetAllPoolMetadataAsync();
                if (!metadataResult.Success)
                {
                    return Result<PoolMetadata>.Error(metadataResult.ErrorMessage);
                }
                
                var metadata = metadataResult.Data?.FirstOrDefault(p => p.PoolGroupGuid == poolGroupGuid);
                if (metadata == null)
                {
                    return Result<PoolMetadata>.Error($"No metadata found for pool {poolGroupGuid}");
                }
                
                return Result<PoolMetadata>.Success(metadata);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting metadata for pool {PoolGroupGuid}", poolGroupGuid);
                return Result<PoolMetadata>.Error($"Error: {ex.Message}");
            }
        }
        
        /// <inheritdoc />
        public async Task<Result<bool>> SavePoolMetadataAsync(PoolMetadata metadata)
        {
            try
            {
                await _metadataLock.WaitAsync();
                
                try
                {
                    // Ensure metadata has required fields
                    if (metadata.PoolGroupGuid == Guid.Empty)
                    {
                        return Result<bool>.Error("Pool GUID cannot be empty");
                    }
                    
                    // Load existing metadata
                    var existingMetadataResult = await GetAllPoolMetadataAsync();
                    if (!existingMetadataResult.Success)
                    {
                        return Result<bool>.Error(existingMetadataResult.ErrorMessage);
                    }
                    
                    // Check if this pool already exists
                    var existingMetadata = existingMetadataResult.Data?.ToList() ?? new List<PoolMetadata>();
                    var existingPool = existingMetadata.FirstOrDefault(p => p.PoolGroupGuid == metadata.PoolGroupGuid);
                    
                    if (existingPool != null)
                    {
                        return Result<bool>.Error($"Pool with GUID {metadata.PoolGroupGuid} already exists");
                    }
                    
                    // Set creation timestamp if not already set
                    if (metadata.CreatedAt == default)
                    {
                        metadata.CreatedAt = DateTime.UtcNow;
                    }
                    
                    // Add new pool to collection
                    existingMetadata.Add(metadata);
                    
                    // Write to file
                    var rootObject = new MetadataRoot
                    {
                        Pools = existingMetadata,
                        LastUpdated = DateTime.UtcNow
                    };
                    
                    string jsonContent = JsonSerializer.Serialize(rootObject, _jsonOptions);
                    await _fileSystemInfoService.WriteFileAsync(_metadataFilePath, jsonContent);
                    
                    _logger.LogInformation("Added metadata for pool {PoolGroupGuid}", metadata.PoolGroupGuid);
                    
                    return Result<bool>.Success(true);
                }
                finally
                {
                    _metadataLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving metadata for pool {PoolGroupGuid}", metadata.PoolGroupGuid);
                return Result<bool>.Error($"Error saving metadata: {ex.Message}");
            }
        }
        
        /// <inheritdoc />
        public async Task<Result<bool>> UpdatePoolMetadataAsync(PoolMetadata metadata)
        {
            try
            {
                await _metadataLock.WaitAsync();
                
                try
                {
                    // Ensure metadata has required fields
                    if (metadata.PoolGroupGuid == Guid.Empty)
                    {
                        return Result<bool>.Error("Pool GUID cannot be empty");
                    }
                    
                    // Load existing metadata
                    var existingMetadataResult = await GetAllPoolMetadataAsync();
                    if (!existingMetadataResult.Success)
                    {
                        return Result<bool>.Error(existingMetadataResult.ErrorMessage);
                    }
                    
                    // Find the pool to update
                    var existingMetadata = existingMetadataResult.Data?.ToList() ?? new List<PoolMetadata>();
                    int poolIndex = existingMetadata.FindIndex(p => p.PoolGroupGuid == metadata.PoolGroupGuid);
                    
                    if (poolIndex == -1)
                    {
                        return Result<bool>.Error($"No metadata found for pool {metadata.PoolGroupGuid}");
                    }
                    
                    // Preserve creation timestamp from original
                    metadata.CreatedAt = existingMetadata[poolIndex].CreatedAt;
                    
                    // Update the pool
                    existingMetadata[poolIndex] = metadata;
                    
                    // Write to file
                    var rootObject = new MetadataRoot
                    {
                        Pools = existingMetadata,
                        LastUpdated = DateTime.UtcNow
                    };
                    
                    string jsonContent = JsonSerializer.Serialize(rootObject, _jsonOptions);
                    await _fileSystemInfoService.WriteFileAsync(_metadataFilePath, jsonContent);
                    
                    _logger.LogInformation("Updated metadata for pool {PoolGroupGuid}", metadata.PoolGroupGuid);
                    
                    return Result<bool>.Success(true);
                }
                finally
                {
                    _metadataLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating metadata for pool {PoolGroupGuid}", metadata.PoolGroupGuid);
                return Result<bool>.Error($"Error updating metadata: {ex.Message}");
            }
        }
        
        /// <inheritdoc />
        public async Task<Result<bool>> RemovePoolMetadataAsync(Guid poolGroupGuid)
        {
            try
            {
                await _metadataLock.WaitAsync();
                
                try
                {
                    // Load existing metadata
                    var existingMetadataResult = await GetAllPoolMetadataAsync();
                    if (!existingMetadataResult.Success)
                    {
                        return Result<bool>.Error(existingMetadataResult.ErrorMessage);
                    }
                    
                    // Find the pool to remove
                    var existingMetadata = existingMetadataResult.Data?.ToList() ?? new List<PoolMetadata>();
                    var pool = existingMetadata.FirstOrDefault(p => p.PoolGroupGuid == poolGroupGuid);
                    
                    if (pool == null)
                    {
                        return Result<bool>.Error($"No metadata found for pool {poolGroupGuid}");
                    }
                    
                    // Remove the pool
                    existingMetadata.Remove(pool);
                    
                    // Write to file
                    var rootObject = new MetadataRoot
                    {
                        Pools = existingMetadata,
                        LastUpdated = DateTime.UtcNow
                    };
                    
                    string jsonContent = JsonSerializer.Serialize(rootObject, _jsonOptions);
                    await _fileSystemInfoService.WriteFileAsync(_metadataFilePath, jsonContent);
                    
                    _logger.LogInformation("Removed metadata for pool {PoolGroupGuid}", poolGroupGuid);
                    
                    return Result<bool>.Success(true);
                }
                finally
                {
                    _metadataLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing metadata for pool {PoolGroupGuid}", poolGroupGuid);
                return Result<bool>.Error($"Error removing metadata: {ex.Message}");
            }
        }
        
        // Helper class for JSON serialization/deserialization
        private class MetadataRoot
        {
            public List<PoolMetadata> Pools { get; set; } = new();
            public DateTime LastUpdated { get; set; }
        }
    }
}