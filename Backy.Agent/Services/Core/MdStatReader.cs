using System;
using System.Collections.Generic;
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
    /// Interface for reading and interpreting MD RAID status from the system.
    /// </summary>
    /// <remarks>
    /// This service is responsible for:
    /// - Reading /proc/mdstat and other MD-related status files
    /// - Parsing the status information into structured data
    /// - Providing information about MD arrays, their states, and component devices
    /// - Monitoring MD array health and status changes
    /// 
    /// This allows the application to get consistent, well-structured RAID information
    /// without duplicating complex parsing logic across multiple services.
    /// </remarks>
    public interface IMdStatReader
    {
        /// <summary>
        /// Gets the current MD array information from /proc/mdstat
        /// </summary>
        /// <returns>Information about all MD arrays on the system</returns>
        Task<MdStatInfo> GetMdStatInfoAsync();
        
        /// <summary>
        /// Gets information about a specific MD array
        /// </summary>
        /// <param name="deviceName">The device name (e.g., 'md0')</param>
        /// <returns>Information about the specified MD array, or null if not found</returns>
        Task<MdArrayInfo?> GetArrayInfoAsync(string deviceName);
        
        /// <summary>
        /// Gets information about an array by its GUID
        /// </summary>
        /// <param name="poolGroupGuid">The pool group GUID</param>
        /// <returns>Information about the MD array associated with the GUID, or null if not found</returns>
        Task<MdArrayInfo?> GetArrayInfoByGuidAsync(Guid poolGroupGuid);
        
        /// <summary>
        /// Gets detailed information about a MD array using mdadm --detail command
        /// </summary>
        /// <param name="deviceName">The device name (e.g., 'md0')</param>
        /// <returns>Detailed information about the array, including UUID and component device details</returns>
        Task<MdArrayDetailInfo?> GetArrayDetailInfoAsync(string deviceName);
        
        /// <summary>
        /// Invalidates the mdstat cache to force a fresh read on next request
        /// </summary>
        void InvalidateMdStatCache();
    }

    /// <summary>
    /// Reads and parses MD RAID status information from the system.
    /// </summary>
    /// <remarks>
    /// This service provides:
    /// - Parsing of /proc/mdstat to extract RAID array information
    /// - Mapping between MD device names and array details
    /// - Status interpretation for arrays (active, degraded, etc.)
    /// - Component device tracking
    /// - Cached reads with controlled invalidation
    /// 
    /// Uses the FileSystemInfoService for reading system files to leverage
    /// its caching and error handling capabilities.
    /// </remarks>
    public class MdStatReader : IMdStatReader
    {
        private const string MDSTAT_FILE_PATH = "/proc/mdstat";
        private readonly ILogger<MdStatReader> _logger;
        private readonly IMemoryCache _cache;
        private readonly IFileSystemInfoService _fileSystemInfoService;
        private readonly ISystemCommandService _commandService;
        private readonly AgentSettings _settings;
        
        // Cache key for the mdstat information
        private const string MDSTAT_CACHE_KEY = "MdStatInfo";
        private const string METADATA_FILE_PATH = "/var/lib/backy/pool-metadata.json";
        
        // Pre-compiled regex patterns for better performance
        private static readonly Regex PersonalitiesRegex = new(@"Personalities : (.+)", RegexOptions.Compiled);
        private static readonly Regex PersonalityItemRegex = new(@"\[(.*?)\]", RegexOptions.Compiled);
        private static readonly Regex UnusedDevicesRegex = new(@"unused devices:(.*?)$", RegexOptions.Compiled);
        private static readonly Regex MdDeviceRegex = new(@"^(md\d+)\s*:.+", RegexOptions.Compiled);
        private static readonly Regex StatusRegex = new(@": (\w+) (raid\d+|linear|multipath) (.+)", RegexOptions.Compiled);
        private static readonly Regex SimpleStatusRegex = new(@": (\w+) (.+)", RegexOptions.Compiled);
        private static readonly Regex DeviceRegex = new(@"(.+)\[(.+)\]", RegexOptions.Compiled);
        private static readonly Regex DevicesListRegex = new(@"(\w+)\[\d+\]", RegexOptions.Compiled);
        private static readonly Regex SizeRegex = new(@"(\d+) blocks", RegexOptions.Compiled);
        private static readonly Regex RaidStatusRegex = new(@"\[(\d+)/(\d+)\] \[([^\]]+)\]", RegexOptions.Compiled);
        private static readonly Regex ResyncProgressRegex = new(@"= *([0-9.]+)% \((\d+)/(\d+)\)", RegexOptions.Compiled);
        private static readonly Regex FinishTimeRegex = new(@"finish=([0-9.]+)min", RegexOptions.Compiled);
        private static readonly Regex SpeedRegex = new(@"speed=([0-9.]+[KMG])/sec", RegexOptions.Compiled);
        
        public MdStatReader(
            ILogger<MdStatReader> logger,
            IMemoryCache cache,
            IFileSystemInfoService fileSystemInfoService,
            ISystemCommandService commandService,
            IOptions<AgentSettings> options)
        {
            _logger = logger;
            _cache = cache;
            _fileSystemInfoService = fileSystemInfoService;
            _commandService = commandService;
            _settings = options.Value;
        }

        /// <inheritdoc />
        public async Task<MdStatInfo> GetMdStatInfoAsync()
        {
            // Check cache first
            if (_cache.TryGetValue(MDSTAT_CACHE_KEY, out MdStatInfo? cachedInfo) && cachedInfo != null)
            {
                _logger.LogDebug("Retrieved MD stat info from cache");
                return cachedInfo;
            }
            
            _logger.LogDebug("Reading MD stat info from {FilePath}", MDSTAT_FILE_PATH);
            
            try
            {
                // Use file-based approach as primary method
                string mdstatContent = await _fileSystemInfoService.ReadFileAsync(MDSTAT_FILE_PATH, false);
                
                if (string.IsNullOrEmpty(mdstatContent))
                {
                    // Fall back to command-based approach if file read fails
                    _logger.LogWarning("Could not read {FilePath} directly, falling back to command execution", MDSTAT_FILE_PATH);
                    var result = await _commandService.ExecuteCommandAsync("cat /proc/mdstat");
                    
                    if (!result.Success)
                    {
                        _logger.LogError("Failed to read mdstat using command fallback: {Error}", result.Error);
                        return new MdStatInfo();
                    }
                    
                    mdstatContent = result.Output;
                }
                
                // Parse the mdstat content
                var mdStatInfo = ParseMdstatContent(mdstatContent);
                
                // Cache the result with expiration based on settings
                _cache.Set(
                    MDSTAT_CACHE_KEY,
                    mdStatInfo,
                    new MemoryCacheEntryOptions().SetAbsoluteExpiration(
                        TimeSpan.FromSeconds(_settings.FileCacheTimeToLiveSeconds)
                    )
                );
                
                return mdStatInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading or parsing mdstat");
                return new MdStatInfo();
            }
        }

        /// <inheritdoc />
        public async Task<MdArrayInfo?> GetArrayInfoAsync(string deviceName)
        {
            var mdStatInfo = await GetMdStatInfoAsync();
            
            if (mdStatInfo.Arrays.TryGetValue(deviceName, out var arrayInfo))
            {
                return arrayInfo;
            }
            
            return null;
        }

        /// <inheritdoc />
        public async Task<MdArrayInfo?> GetArrayInfoByGuidAsync(Guid poolGroupGuid)
        {
            if (poolGroupGuid == Guid.Empty)
            {
                return null;
            }
            
            try
            {
                // First check by UUID-based path in /dev/md/
                string mdUuidPath = $"/dev/md/{poolGroupGuid.ToString("N")}";
                if (_fileSystemInfoService.FileExists(mdUuidPath))
                {
                    // Get the actual md device by resolving the symlink
                    var detailResult = await _commandService.ExecuteCommandAsync($"readlink -f {mdUuidPath}");
                    if (detailResult.Success && !string.IsNullOrEmpty(detailResult.Output))
                    {
                        string actualPath = detailResult.Output.Trim();
                        string deviceName = Path.GetFileName(actualPath);
                        return await GetArrayInfoAsync(deviceName);
                    }
                }
                
                // Fall back to pool metadata lookup
                var metadata = await ReadPoolMetadataAsync();
                var poolInfo = metadata.FirstOrDefault(p => p.PoolGroupGuid == poolGroupGuid);
                
                if (poolInfo == null || string.IsNullOrEmpty(poolInfo.MdDeviceName))
                {
                    return null;
                }
                
                return await GetArrayInfoAsync(poolInfo.MdDeviceName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting array info by GUID {Guid}", poolGroupGuid);
                return null;
            }
        }
        
        /// <inheritdoc />
        public async Task<MdArrayDetailInfo?> GetArrayDetailInfoAsync(string deviceName)
        {
            try
            {
                var result = await _commandService.ExecuteCommandAsync($"mdadm --detail /dev/{deviceName}");
                if (!result.Success)
                {
                    _logger.LogWarning("Failed to get detailed information for array {DeviceName}: {Error}", 
                        deviceName, result.Output);
                    return null;
                }
                
                return ParseDetailOutput(result.Output, deviceName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting detailed info for array {DeviceName}", deviceName);
                return null;
            }
        }
        
        /// <inheritdoc />
        public void InvalidateMdStatCache()
        {
            _cache.Remove(MDSTAT_CACHE_KEY);
            _logger.LogDebug("Invalidated MD stat cache");
        }
        
        /// <summary>
        /// Parses the content of /proc/mdstat into a structured object
        /// </summary>
        private MdStatInfo ParseMdstatContent(string content)
        {
            var mdStatInfo = new MdStatInfo();
            
            if (string.IsNullOrWhiteSpace(content))
            {
                return mdStatInfo;
            }
            
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            // Parse personalities line (RAID levels)
            var personalitiesLine = lines.FirstOrDefault(l => l.StartsWith("Personalities :"));
            if (personalitiesLine != null)
            {
                var personalitiesMatch = PersonalitiesRegex.Match(personalitiesLine);
                if (personalitiesMatch.Success)
                {
                    var personalitiesText = personalitiesMatch.Groups[1].Value;
                    mdStatInfo.Personalities = PersonalityItemRegex.Matches(personalitiesText)
                        .Cast<Match>()
                        .Select(m => m.Groups[1].Value)
                        .ToList();
                }
            }
            
            // Parse unused devices
            var unusedLine = lines.FirstOrDefault(l => l.StartsWith("unused devices:"));
            if (unusedLine != null)
            {
                var unusedMatch = UnusedDevicesRegex.Match(unusedLine);
                if (unusedMatch.Success)
                {
                    var unusedText = unusedMatch.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(unusedText) && unusedText != "<none>")
                    {
                        mdStatInfo.UnusedDevices = unusedText.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    }
                }
            }
            
            // Parse MD device information
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var mdMatch = MdDeviceRegex.Match(line);
                
                if (mdMatch.Success)
                {
                    var deviceName = mdMatch.Groups[1].Value;
                    var arrayInfo = new MdArrayInfo { DeviceName = deviceName };
                    
                    // Parse active/inactive state and RAID level
                    var statusMatch = StatusRegex.Match(line);
                    if (statusMatch.Success)
                    {
                        arrayInfo.State = statusMatch.Groups[1].Value; // active, inactive
                        arrayInfo.Level = statusMatch.Groups[2].Value; // raid0, raid1, etc.
                        arrayInfo.IsActive = arrayInfo.State == "active";
                    }
                    else
                    {
                        // Try simpler status regex for non-standard formats
                        var simpleStatusMatch = SimpleStatusRegex.Match(line);
                        if (simpleStatusMatch.Success)
                        {
                            arrayInfo.State = simpleStatusMatch.Groups[1].Value;
                            arrayInfo.IsActive = arrayInfo.State == "active";
                        }
                    }
                    
                    // Parse device components
                    var deviceMatch = DeviceRegex.Match(line);
                    if (deviceMatch.Success)
                    {
                        var devices = DevicesListRegex.Matches(deviceMatch.Groups[1].Value)
                            .Cast<Match>()
                            .Select(m => m.Groups[1].Value)
                            .ToList();
                        
                        arrayInfo.Devices = devices;
                    }
                    
                    // Parse next line for size and status information
                    if (i + 1 < lines.Length)
                    {
                        var nextLine = lines[i + 1];
                        
                        // Look for array size
                        var sizeMatch = SizeRegex.Match(nextLine);
                        if (sizeMatch.Success)
                        {
                            if (long.TryParse(sizeMatch.Groups[1].Value, out long blocks))
                            {
                                // Convert blocks (usually 1K) to bytes
                                arrayInfo.ArraySize = blocks * 1024;
                            }
                        }
                        
                        // Look for RAID status [UU]
                        var raidStatusMatch = RaidStatusRegex.Match(nextLine);
                        if (raidStatusMatch.Success)
                        {
                            var activeDevices = int.Parse(raidStatusMatch.Groups[1].Value);
                            var totalDevices = int.Parse(raidStatusMatch.Groups[2].Value);
                            var statusString = raidStatusMatch.Groups[3].Value;
                            
                            arrayInfo.ActiveDevices = activeDevices;
                            arrayInfo.TotalDevices = totalDevices;
                            arrayInfo.Status = statusString.ToCharArray().Select(c => c.ToString()).ToArray();
                            
                            // Count working, failed, and spare devices based on status characters
                            var workingDevices = statusString.Count(c => c == 'U');
                            var failedDevices = statusString.Count(c => c == '_');
                            var spareDevices = statusString.Count(c => c == 'S');
                            
                            arrayInfo.WorkingDevices = workingDevices;
                            arrayInfo.FailedDevices = failedDevices;
                            arrayInfo.SpareDevices = spareDevices;
                        }
                    }
                    
                    // Parse resync/recovery information from third line
                    if (i + 2 < lines.Length && (
                        lines[i + 2].Contains("resync") || 
                        lines[i + 2].Contains("recovery") || 
                        lines[i + 2].Contains("check")))
                    {
                        var resyncLine = lines[i + 2];
                        arrayInfo.ResyncInProgress = true;
                        
                        // Parse percentage complete
                        var resyncMatch = ResyncProgressRegex.Match(resyncLine);
                        if (resyncMatch.Success)
                        {
                            if (double.TryParse(resyncMatch.Groups[1].Value, out double percentage))
                            {
                                arrayInfo.ResyncPercentage = percentage;
                            }
                        }
                        
                        // Parse estimated finish time
                        var finishMatch = FinishTimeRegex.Match(resyncLine);
                        if (finishMatch.Success)
                        {
                            if (double.TryParse(finishMatch.Groups[1].Value, out double minutes))
                            {
                                arrayInfo.ResyncTimeEstimate = minutes;
                            }
                        }
                        
                        // Parse resync speed
                        var speedMatch = SpeedRegex.Match(resyncLine);
                        if (speedMatch.Success)
                        {
                            arrayInfo.ResyncSpeed = speedMatch.Groups[1].Value;
                        }
                    }
                    
                    // Add the array info to the result
                    mdStatInfo.Arrays[deviceName] = arrayInfo;
                }
            }
            
            return mdStatInfo;
        }

        /// <summary>
        /// Parses the detailed output of mdadm --detail into a structured object
        /// </summary>
        private MdArrayDetailInfo? ParseDetailOutput(string output, string deviceName)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return null;
            }
            
            var detailInfo = new MdArrayDetailInfo
            {
                DeviceName = deviceName
            };
            
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .ToArray();
            
            foreach (var line in lines)
            {
                // Extract UUID
                if (line.StartsWith("UUID :"))
                {
                    var uuidParts = line.Substring(6).Trim().Split(':');
                    if (uuidParts.Length == 4)
                    {
                        detailInfo.UUID = string.Join("", uuidParts);
                    }
                }
                
                // Extract RAID level
                if (line.StartsWith("RAID Level :"))
                {
                    detailInfo.Level = line.Substring(12).Trim();
                }
                
                // Extract state
                if (line.StartsWith("State :"))
                {
                    detailInfo.State = line.Substring(7).Trim();
                    detailInfo.IsActive = !detailInfo.State.Contains("inactive");
                    detailInfo.IsDegraded = detailInfo.State.Contains("degraded");
                }
                
                // Extract array size
                if (line.StartsWith("Array Size :"))
                {
                    var sizeText = line.Substring(12).Trim();
                    var sizeMatch = Regex.Match(sizeText, @"(\d+)");
                    if (sizeMatch.Success && long.TryParse(sizeMatch.Groups[1].Value, out long sizeK))
                    {
                        detailInfo.ArraySize = sizeK * 1024; // Convert from K to bytes
                    }
                }
                
                // Extract device count
                if (line.StartsWith("Raid Devices :"))
                {
                    if (int.TryParse(line.Substring(14).Trim(), out int devices))
                    {
                        detailInfo.TotalDevices = devices;
                    }
                }
                
                // Extract active device count
                if (line.StartsWith("Active Devices :"))
                {
                    if (int.TryParse(line.Substring(16).Trim(), out int active))
                    {
                        detailInfo.ActiveDevices = active;
                    }
                }
                
                // Extract working device count
                if (line.StartsWith("Working Devices :"))
                {
                    if (int.TryParse(line.Substring(17).Trim(), out int working))
                    {
                        detailInfo.WorkingDevices = working;
                    }
                }
                
                // Extract failed device count
                if (line.StartsWith("Failed Devices :"))
                {
                    if (int.TryParse(line.Substring(16).Trim(), out int failed))
                    {
                        detailInfo.FailedDevices = failed;
                    }
                }
                
                // Extract spare device count
                if (line.StartsWith("Spare Devices :"))
                {
                    if (int.TryParse(line.Substring(15).Trim(), out int spare))
                    {
                        detailInfo.SpareDevices = spare;
                    }
                }
                
                // Extract component devices
                if (Regex.IsMatch(line, @"^\s*\d+\s+\d+\s+\d+\s+\d+\s+"))
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5)
                    {
                        var deviceComponent = new MdArrayDeviceComponent
                        {
                            DevicePath = parts[parts.Length - 1],
                            DeviceName = Path.GetFileName(parts[parts.Length - 1]),
                            Status = parts[4]
                        };
                        
                        detailInfo.ComponentDevices.Add(deviceComponent);
                    }
                }
            }
            
            return detailInfo;
        }

        /// <summary>
        /// Reads pool metadata from the configured file
        /// </summary>
        private async Task<List<PoolMetadata>> ReadPoolMetadataAsync()
        {
            try
            {
                if (!_fileSystemInfoService.FileExists(METADATA_FILE_PATH))
                {
                    _logger.LogDebug("Pool metadata file not found at {FilePath}", METADATA_FILE_PATH);
                    return new List<PoolMetadata>();
                }
                
                string metadata = await _fileSystemInfoService.ReadFileAsync(METADATA_FILE_PATH);
                
                var metadataObject = System.Text.Json.JsonSerializer.Deserialize<PoolMetadataCollection>(metadata);
                return metadataObject?.Pools ?? new List<PoolMetadata>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading pool metadata");
                return new List<PoolMetadata>();
            }
        }
    }
}