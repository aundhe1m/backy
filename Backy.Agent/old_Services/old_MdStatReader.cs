using System.Text.RegularExpressions;
using Backy.Agent.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Backy.Agent.Services;

public class MdStatReader : IMdStatReader
{
    private const string MDSTAT_FILE_PATH = "/proc/mdstat";
    private readonly ILogger<MdStatReader> _logger;
    private readonly IMemoryCache _cache;
    private readonly IFileSystemInfoService _fileSystemInfoService;
    private readonly ISystemCommandService _commandService;
    private readonly AgentSettings _settings;
    private const string METADATA_FILE_PATH = "/var/lib/backy/pool-metadata.json";
    
    // Cache key for the mdstat information
    private const string MDSTAT_CACHE_KEY = "MdStatInfo";
    
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

    /// <summary>
    /// Gets the current MD array information from /proc/mdstat
    /// </summary>
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

    /// <summary>
    /// Gets information about a specific MD array
    /// </summary>
    public async Task<MdArrayInfo?> GetArrayInfoAsync(string deviceName)
    {
        var mdStatInfo = await GetMdStatInfoAsync();
        
        if (mdStatInfo.Arrays.TryGetValue(deviceName, out var arrayInfo))
        {
            return arrayInfo;
        }
        
        return null;
    }

    /// <summary>
    /// Gets information about an array by its GUID
    /// </summary>
    public async Task<MdArrayInfo?> GetArrayInfoByGuidAsync(Guid poolGroupGuid)
    {
        if (poolGroupGuid == Guid.Empty)
        {
            return null;
        }
        
        try
        {
            // Get pool metadata to find the corresponding device name
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