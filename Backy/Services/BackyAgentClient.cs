using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Backy.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;

namespace Backy.Services
{
    /// <summary>
    /// Client for communicating with the Backy Agent API.
    /// </summary>
    public class BackyAgentClient : IBackyAgentClient, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<BackyAgentClient> _logger;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly IConfiguration _configuration;
        private bool _isDisposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="BackyAgentClient"/> class.
        /// </summary>
        public BackyAgentClient(
            HttpClient httpClient, 
            IOptions<BackyAgentConfig> options, 
            ILogger<BackyAgentClient> logger,
            IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Log available configuration values for debugging
            var configuredUrl = _configuration["BACKY_AGENT_URL"] ?? _configuration["BackyAgent:Url"];
            var optionsUrl = options.Value.BaseUrl;
            
            _logger.LogDebug("Configuration sources for Backy Agent URL:");
            _logger.LogDebug("  - Environment/appsettings: {ConfigUrl}", configuredUrl);
            _logger.LogDebug("  - Options value: {OptionsUrl}", optionsUrl);
            
            // Use the most specific configuration source available
            var finalBaseUrl = configuredUrl ?? optionsUrl;
            
            // Configure HttpClient using the determined URL
            _httpClient.BaseAddress = new Uri(finalBaseUrl);
            _httpClient.Timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds);
            _httpClient.DefaultRequestHeaders.Add("X-Api-Key", options.Value.ApiKey);
            
            // Configure JSON serialization options
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };
            
            _logger.LogInformation("BackyAgentClient initialized with base URL: {BaseUrl}", finalBaseUrl);
        }
        
        /// <summary>
        /// Gets the connection status of the agent.
        /// </summary>
        public async Task<(bool IsConnected, string StatusMessage)> GetConnectionStatusAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/v1/status");
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return (true, "Connected to Backy Agent");
                }
                
                return (false, $"Agent responded with status code: {response.StatusCode}");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Unable to connect to Backy Agent");
                return (false, $"Connection failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking agent connection status");
                return (false, $"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets a list of all drives in the system.
        /// </summary>
        public async Task<List<Drive>> GetDrivesAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/v1/drives");
                response.EnsureSuccessStatusCode();
                
                var lsblkOutput = await response.Content.ReadFromJsonAsync<LsblkOutput>(_jsonOptions);
                return ConvertBlockDevicesToDrives(lsblkOutput ?? new LsblkOutput());
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error getting drives from agent");
                throw new BackyAgentException("Failed to retrieve drives from Backy Agent", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error getting drives");
                throw new BackyAgentException("An unexpected error occurred while retrieving drives", ex);
            }
        }

        /// <summary>
        /// Gets status information for a specific drive.
        /// </summary>
        public async Task<DriveStatus> GetDriveStatusAsync(string serial)
        {
            if (string.IsNullOrEmpty(serial))
            {
                throw new ArgumentException("Serial number cannot be empty", nameof(serial));
            }
            
            try
            {
                var response = await _httpClient.GetAsync($"/api/v1/drives/{serial}/status");
                
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new DriveStatus { Status = "not_found" };
                }
                
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<DriveStatus>(_jsonOptions);
                return result ?? new DriveStatus { Status = "error" }; // Return a default object if null
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error getting drive status for serial {Serial}", serial);
                throw new BackyAgentException($"Failed to retrieve status for drive {serial}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error getting drive status");
                throw new BackyAgentException("An unexpected error occurred while retrieving drive status", ex);
            }
        }

        /// <summary>
        /// Creates a new pool from the selected drives.
        /// </summary>
        public async Task<(bool Success, string Message, List<string> Outputs)> CreatePoolAsync(CreatePoolRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            
            if (string.IsNullOrWhiteSpace(request.PoolLabel))
            {
                return (false, "Pool label is required", new List<string>());
            }
            
            if (request.DriveSerials == null || request.DriveSerials.Count == 0)
            {
                return (false, "At least one drive must be selected", new List<string>());
            }
            
            try
            {
                _logger.LogInformation("Creating pool with label {PoolLabel} and {DriveCount} drives", 
                    request.PoolLabel, request.DriveSerials.Count);
                
                // Convert the CreatePoolRequest to the format expected by the agent API
                var agentRequest = new CreatePoolAgentRequest
                {
                    Label = request.PoolLabel,
                    DriveSerials = request.DriveSerials,
                    DriveLabels = request.DriveLabels ?? new Dictionary<string, string>(),
                    PoolGroupGuid = Guid.NewGuid() // Generate a new GUID for this pool
                };
                
                // Set the mount path using the poolGroupGuid as requested
                agentRequest.MountPath = $"/mnt/backy/{agentRequest.PoolGroupGuid}";
                
                _logger.LogInformation("Sending pool creation request to agent with MountPath: {MountPath}", 
                    agentRequest.MountPath);
                
                var response = await _httpClient.PostAsJsonAsync("/api/v1/pools", agentRequest, _jsonOptions);
                
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<PoolCreationResponse>(_jsonOptions);
                    if (result == null)
                    {
                        return (false, "Invalid response from server", new List<string>());
                    }
                    return (result.Success, "Pool created successfully", result.CommandOutputs);
                }
                else
                {
                    var errorResponse = await DeserializeErrorResponse(response);
                    return (false, $"Failed to create pool: {errorResponse}", new List<string>());
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error creating pool");
                return (false, $"Failed to create pool: {ex.Message}", new List<string>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error creating pool");
                return (false, $"An unexpected error occurred while creating pool: {ex.Message}", new List<string>());
            }
        }

        /// <summary>
        /// Gets a list of all pools in the system.
        /// </summary>
        public async Task<List<PoolInfo>> GetPoolsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/v1/pools");
                response.EnsureSuccessStatusCode();
                
                var poolListItems = await response.Content.ReadFromJsonAsync<List<PoolListItem>>(_jsonOptions);
                return ConvertPoolListItemsToPoolInfos(poolListItems ?? new List<PoolListItem>());
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error getting pools from agent");
                throw new BackyAgentException("Failed to retrieve pools from Backy Agent", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error getting pools");
                throw new BackyAgentException("An unexpected error occurred while retrieving pools", ex);
            }
        }

        /// <summary>
        /// Gets detailed information about a specific pool.
        /// </summary>
        public async Task<(bool Success, string Message, string Output)> GetPoolDetailAsync(Guid poolGroupGuid)
        {
            try
            {
                var response = await _httpClient.GetAsync($"/api/v1/pools/guid/{poolGroupGuid}");
                
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return (false, "Pool not found", string.Empty);
                }
                
                response.EnsureSuccessStatusCode();
                
                var poolDetail = await response.Content.ReadFromJsonAsync<PoolDetailResponse>(_jsonOptions);
                if (poolDetail == null)
                {
                    return (false, "Invalid response from server", string.Empty);
                }
                
                var jsonOutput = JsonSerializer.Serialize(poolDetail, _jsonOptions);
                return (true, string.Empty, jsonOutput);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error getting pool detail for GUID {PoolGroupGuid}", poolGroupGuid);
                return (false, $"Failed to retrieve pool details: {ex.Message}", string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error getting pool detail");
                return (false, $"An unexpected error occurred while retrieving pool details: {ex.Message}", string.Empty);
            }
        }

        /// <summary>
        /// Mounts a pool.
        /// </summary>
        public async Task<(bool Success, string Message)> MountPoolAsync(Guid poolGroupGuid, string? mountPath = null)
        {
            try
            {
                var request = new MountRequest { MountPath = mountPath };
                var response = await _httpClient.PostAsJsonAsync($"/api/v1/pools/guid/{poolGroupGuid}/mount", request, _jsonOptions);
                
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<CommandResponse>(_jsonOptions);
                    if (result == null)
                    {
                        return (false, "Invalid response from server");
                    }
                    return (result.Success, "Pool mounted successfully");
                }
                else
                {
                    var errorResponse = await DeserializeErrorResponse(response);
                    return (false, $"Failed to mount pool: {errorResponse}");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error mounting pool with GUID {PoolGroupGuid}", poolGroupGuid);
                return (false, $"Failed to mount pool: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error mounting pool");
                return (false, $"An unexpected error occurred while mounting pool: {ex.Message}");
            }
        }

        /// <summary>
        /// Unmounts a pool.
        /// </summary>
        public async Task<(bool Success, string Message)> UnmountPoolAsync(Guid poolGroupGuid)
        {
            try
            {
                var response = await _httpClient.PostAsync($"/api/v1/pools/guid/{poolGroupGuid}/unmount", null);
                
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<CommandResponse>(_jsonOptions);
                    if (result == null)
                    {
                        return (false, "Invalid response from server");
                    }
                    return (result.Success, "Pool unmounted successfully");
                }
                else
                {
                    var errorResponse = await DeserializeErrorResponse(response);
                    return (false, $"Failed to unmount pool: {errorResponse}");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error unmounting pool with GUID {PoolGroupGuid}", poolGroupGuid);
                return (false, $"Failed to unmount pool: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error unmounting pool");
                return (false, $"An unexpected error occurred while unmounting pool: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes a pool group.
        /// </summary>
        public async Task<(bool Success, string Message)> RemovePoolGroupAsync(Guid poolGroupGuid)
        {
            try
            {
                var response = await _httpClient.PostAsync($"/api/v1/pools/guid/{poolGroupGuid}/remove", null);
                
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<CommandResponse>(_jsonOptions);
                    if (result == null)
                    {
                        return (false, "Invalid response from server");
                    }
                    return (result.Success, "Pool removed successfully");
                }
                else
                {
                    var errorResponse = await DeserializeErrorResponse(response);
                    return (false, $"Failed to remove pool: {errorResponse}");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error removing pool with GUID {PoolGroupGuid}", poolGroupGuid);
                return (false, $"Failed to remove pool: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error removing pool");
                return (false, $"An unexpected error occurred while removing pool: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets size information about a mount point.
        /// </summary>
        public async Task<(long Size, long Used, long Available, string UsePercent)> GetMountPointSizeAsync(string mountPoint)
        {
            if (string.IsNullOrEmpty(mountPoint))
            {
                throw new ArgumentException("Mount point cannot be empty", nameof(mountPoint));
            }
            
            try
            {
                // URL encode the mount path to handle paths with special characters
                var encodedPath = Uri.EscapeDataString(mountPoint);
                var response = await _httpClient.GetAsync($"/api/v1/mounts/{encodedPath}/size");
                response.EnsureSuccessStatusCode();
                
                var result = await response.Content.ReadFromJsonAsync<MountSizeResponse>(_jsonOptions);
                if (result == null)
                {
                    return (0, 0, 0, "0%");
                }
                return (result.Size, result.Used, result.Available, result.UsePercent);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error getting mount point size for {MountPoint}", mountPoint);
                return (0, 0, 0, "0%");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error getting mount point size");
                return (0, 0, 0, "0%");
            }
        }

        /// <summary>
        /// Gets a list of processes using a mount point.
        /// </summary>
        public async Task<List<ProcessInfo>> GetProcessesUsingMountPointAsync(string mountPoint)
        {
            if (string.IsNullOrEmpty(mountPoint))
            {
                throw new ArgumentException("Mount point cannot be empty", nameof(mountPoint));
            }
            
            try
            {
                // URL encode the mount path to handle paths with special characters
                var encodedPath = Uri.EscapeDataString(mountPoint);
                var response = await _httpClient.GetAsync($"/api/v1/mounts/{encodedPath}");
                response.EnsureSuccessStatusCode();
                
                var result = await response.Content.ReadFromJsonAsync<ProcessesResponse>(_jsonOptions);
                if (result == null)
                {
                    return new List<ProcessInfo>();
                }
                return result.Processes ?? new List<ProcessInfo>();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error getting processes using mount point {MountPoint}", mountPoint);
                return new List<ProcessInfo>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error getting processes using mount point");
                return new List<ProcessInfo>();
            }
        }

        /// <summary>
        /// Force adds a drive to a pool.
        /// </summary>
        public async Task<(bool Success, string Message)> ForceAddDriveAsync(int driveId, Guid poolGroupGuid, string devPath)
        {
            try
            {
                var request = new ForceAddDriveRequest
                {
                    DriveId = driveId,
                    DevPath = devPath
                };
                
                var response = await _httpClient.PostAsJsonAsync($"/api/v1/pools/guid/{poolGroupGuid}/drives/add", request, _jsonOptions);
                
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<CommandResponse>(_jsonOptions);
                    if (result == null)
                    {
                        return (false, "Invalid response from server");
                    }
                    return (result.Success, "Drive added to pool successfully");
                }
                else
                {
                    var errorResponse = await DeserializeErrorResponse(response);
                    return (false, $"Failed to add drive to pool: {errorResponse}");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error adding drive to pool with GUID {PoolGroupGuid}", poolGroupGuid);
                return (false, $"Failed to add drive to pool: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error adding drive to pool");
                return (false, $"An unexpected error occurred while adding drive to pool: {ex.Message}");
            }
        }

        /// <summary>
        /// Kills processes using a pool and performs an action.
        /// </summary>
        public async Task<(bool Success, string Message, List<string> Outputs)> KillProcessesAsync(KillProcessesRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            
            try
            {
                var response = await _httpClient.PostAsJsonAsync("/api/v1/processes/kill", request, _jsonOptions);
                
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<CommandResponse>(_jsonOptions);
                    if (result == null)
                    {
                        return (false, "Invalid response from server", new List<string>());
                    }
                    return (result.Success, "Processes killed successfully", result.CommandOutputs);
                }
                else
                {
                    var errorResponse = await DeserializeErrorResponse(response);
                    return (false, $"Failed to kill processes: {errorResponse}", new List<string>());
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error killing processes");
                return (false, $"Failed to kill processes: {ex.Message}", new List<string>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error killing processes");
                return (false, $"An unexpected error occurred while killing processes: {ex.Message}", new List<string>());
            }
        }

        /// <summary>
        /// Helper method for deserializing error responses.
        /// </summary>
        private async Task<string> DeserializeErrorResponse(HttpResponseMessage response)
        {
            try
            {
                var content = await response.Content.ReadAsStringAsync();
                
                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(content, _jsonOptions);
                    if (errorResponse?.Error != null)
                    {
                        return errorResponse.Error.Details != null 
                            ? $"{errorResponse.Error.Message}: {errorResponse.Error.Details}"
                            : errorResponse.Error.Message;
                    }
                }
                catch {}
                
                return $"Error {(int)response.StatusCode}: {response.ReasonPhrase}";
            }
            catch
            {
                return $"Error {(int)response.StatusCode}: {response.ReasonPhrase}";
            }
        }

        /// <summary>
        /// Helper method to convert BlockDevices to Drive objects.
        /// </summary>
        private List<Drive> ConvertBlockDevicesToDrives(LsblkOutput lsblkOutput)
        {
            var activeDrives = new List<Drive>();
            
            if (lsblkOutput.Blockdevices == null)
            {
                return activeDrives;
            }
            
            foreach (var device in lsblkOutput.Blockdevices)
            {
                // Skip sda and its children
                if (device.Name == "sda")
                    continue;

                if (device.Type == "disk")
                {
                    var driveData = new Drive
                    {
                        Name = device.Name ?? "Unknown",
                        Serial = device.Serial ?? "No Serial",
                        Vendor = device.Vendor ?? "Unknown Vendor",
                        Model = device.Model ?? "Unknown Model",
                        Size = device.Size ?? 0,
                        IsMounted = !string.IsNullOrEmpty(device.Mountpoint),
                        IsConnected = true,
                        Partitions = new List<PartitionInfo>(),
                        IdLink = !string.IsNullOrEmpty(device.IdLink) ? $"/dev/disk/by-id/{device.IdLink}" : device.Path ?? string.Empty,
                    };

                    // If the disk has partitions
                    if (device.Children != null)
                    {
                        foreach (var partition in device.Children)
                        {
                            var partitionData = new PartitionInfo
                            {
                                Name = partition.Name ?? "Unknown",
                                UUID = partition.Uuid ?? "No UUID",
                                Fstype = partition.Fstype ?? "Unknown",
                                MountPoint = partition.Mountpoint ?? "Not Mounted",
                                Size = partition.Size ?? 0,
                                Type = partition.Type ?? "Unknown",
                                Path = partition.Path ?? string.Empty,
                            };

                            if (!string.IsNullOrEmpty(partition.Mountpoint))
                            {
                                driveData.IsMounted = true;
                            }

                            driveData.Partitions.Add(partitionData);
                        }
                    }

                    activeDrives.Add(driveData);
                }
            }
            
            return activeDrives;
        }
        
        /// <summary>
        /// Helper method to convert PoolListItem objects to PoolInfo objects.
        /// </summary>
        private List<PoolInfo> ConvertPoolListItemsToPoolInfos(List<PoolListItem> poolListItems)
        {
            var poolInfos = new List<PoolInfo>();
            
            if (poolListItems == null)
            {
                return poolInfos;
            }
            
            foreach (var item in poolListItems)
            {
                var poolInfo = new PoolInfo
                {
                    PoolId = item.PoolId,
                    PoolGroupGuid = item.PoolGroupGuid,
                    Label = item.Label,
                    Status = item.Status,
                    MountPath = item.MountPath ?? string.Empty,
                    IsMounted = item.IsMounted,
                    DriveCount = item.DriveCount,
                    PoolGroupId = 0, // Initialize with a default value 
                    Drives = item.Drives.Select(d => new PoolDriveInfo
                    {
                        Serial = d.Serial,
                        Label = d.Label,
                        IsConnected = d.IsConnected
                    }).ToList()
                };
                
                poolInfos.Add(poolInfo);
            }
            
            return poolInfos;
        }

        /// <summary>
        /// Releases the resources used by the client.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the resources used by the client.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed) return;

            if (disposing)
            {
                // No need to dispose HttpClient as it's managed by the factory
            }

            _isDisposed = true;
        }
    }
    
    /// <summary>
    /// Exception thrown when there is an error communicating with the Backy Agent.
    /// </summary>
    public class BackyAgentException : Exception
    {
        public BackyAgentException(string message) : base(message) { }
        public BackyAgentException(string message, Exception innerException) : base(message, innerException) { }
    }
    
    // API Response Models
    
    internal class PoolCreationResponse
    {
        public bool Success { get; set; }
        public string? PoolId { get; set; }
        public string? MountPath { get; set; }
        public List<string> CommandOutputs { get; set; } = new List<string>();
    }
    
    internal class CommandResponse
    {
        public bool Success { get; set; }
        public List<string> CommandOutputs { get; set; } = new List<string>();
    }
    
    internal class MountSizeResponse
    {
        public long Size { get; set; }
        public long Used { get; set; }
        public long Available { get; set; }
        public string UsePercent { get; set; } = "0%";
    }
    
    internal class ProcessesResponse
    {
        public List<ProcessInfo> Processes { get; set; } = new List<ProcessInfo>();
    }
    
    internal class PoolDetailResponse
    {
        public string Status { get; set; } = "unknown";
        public long Size { get; set; }
        public long Used { get; set; }
        public long Available { get; set; }
        public string UsePercent { get; set; } = "0%";
        public string? MountPath { get; set; }
        public List<PoolDriveStatus> Drives { get; set; } = new List<PoolDriveStatus>();
    }
    
    internal class PoolDriveStatus
    {
        public string Serial { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Status { get; set; } = "unknown";
    }
    
    internal class PoolListItem
    {
        public string PoolId { get; set; } = string.Empty;
        public Guid PoolGroupGuid { get; set; }
        public string Label { get; set; } = string.Empty;
        public string Status { get; set; } = "unknown";
        public string? MountPath { get; set; }
        public bool IsMounted { get; set; }
        public int DriveCount { get; set; }
        public List<PoolDriveSummary> Drives { get; set; } = new List<PoolDriveSummary>();
    }
    
    internal class PoolDriveSummary
    {
        public string Serial { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public bool IsConnected { get; set; }
    }
    
    internal class ErrorResponse
    {
        public ErrorDetail? Error { get; set; }
    }
    
    internal class ErrorDetail
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Details { get; set; }
    }
    
    internal class ForceAddDriveRequest
    {
        public int DriveId { get; set; }
        public string DevPath { get; set; } = string.Empty;
    }
    
    internal class MountRequest
    {
        public string? MountPath { get; set; }
    }

    internal class CreatePoolAgentRequest
    {
        public string Label { get; set; } = string.Empty;
        public List<string> DriveSerials { get; set; } = new List<string>();
        public Dictionary<string, string> DriveLabels { get; set; } = new Dictionary<string, string>();
        public string MountPath { get; set; } = string.Empty;
        public Guid PoolGroupGuid { get; set; }
    }
}