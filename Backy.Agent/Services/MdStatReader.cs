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
    private readonly ISystemCommandService _commandService;
    private readonly AgentSettings _settings;
    private const string METADATA_FILE_PATH = "/var/lib/backy/pool-metadata.json";
    
    // Cache key for the mdstat information
    private const string MDSTAT_CACHE_KEY = "MdStatInfo";
    
    public MdStatReader(
        ILogger<MdStatReader> logger,
        IMemoryCache cache,
        ISystemCommandService commandService,
        IOptions<AgentSettings> options)
    {
        _logger = logger;
        _cache = cache;
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
            string mdstatContent;
            
            if (File.Exists(MDSTAT_FILE_PATH))
            {
                mdstatContent = await File.ReadAllTextAsync(MDSTAT_FILE_PATH);
            }
            else
            {
                // Fall back to command-based approach if file is not accessible
                _logger.LogWarning("Could not access {FilePath}, falling back to command execution", MDSTAT_FILE_PATH);
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
            var personalitiesMatch = Regex.Match(personalitiesLine, @"Personalities : (.+)");
            if (personalitiesMatch.Success)
            {
                var personalitiesText = personalitiesMatch.Groups[1].Value;
                mdStatInfo.Personalities = Regex.Matches(personalitiesText, @"\[(.*?)\]")
                    .Cast<Match>()
                    .Select(m => m.Groups[1].Value)
                    .ToList();
            }
        }
        
        // Parse unused devices
        var unusedLine = lines.FirstOrDefault(l => l.StartsWith("unused devices:"));
        if (unusedLine != null)
        {
            var unusedMatch = Regex.Match(unusedLine, @"unused devices:(.*?)$");
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
            var mdMatch = Regex.Match(line, @"^(md\d+)\s*:.+");
            
            if (mdMatch.Success)
            {
                var deviceName = mdMatch.Groups[1].Value;
                var arrayInfo = new MdArrayInfo { DeviceName = deviceName };
                
                // Parse active/inactive state and RAID level
                var statusMatch = Regex.Match(line, @": (\w+) (raid\d+|linear|multipath) (.+)");
                if (statusMatch.Success)
                {
                    arrayInfo.State = statusMatch.Groups[1].Value; // active, inactive
                    arrayInfo.Level = statusMatch.Groups[2].Value; // raid0, raid1, etc.
                    arrayInfo.IsActive = arrayInfo.State.Equals("active", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    var simpleStatusMatch = Regex.Match(line, @": (\w+) (.+)");
                    if (simpleStatusMatch.Success)
                    {
                        arrayInfo.State = simpleStatusMatch.Groups[1].Value;
                        arrayInfo.IsActive = arrayInfo.State.Equals("active", StringComparison.OrdinalIgnoreCase);
                    }
                }
                
                // Extract component devices
                var deviceMatch = Regex.Match(line, @"(.+)\[(.+)\]");
                if (deviceMatch.Success)
                {
                    var devicesText = deviceMatch.Groups[1].Value;
                    var devices = Regex.Matches(devicesText, @"(\w+)\[\d+\]")
                        .Cast<Match>()
                        .Select(m => m.Groups[1].Value)
                        .ToList();
                    
                    arrayInfo.Devices = devices;
                }
                
                // Look for the next line with array size and status
                if (i + 1 < lines.Length)
                {
                    var sizeLine = lines[i + 1].Trim();
                    
                    // Parse array size
                    var sizeMatch = Regex.Match(sizeLine, @"(\d+) blocks");
                    if (sizeMatch.Success)
                    {
                        var blockCount = long.Parse(sizeMatch.Groups[1].Value);
                        arrayInfo.ArraySize = blockCount * 1024; // Convert blocks to bytes
                    }
                    
                    // Parse RAID status like [2/2] [UU]
                    var raidStatusMatch = Regex.Match(sizeLine, @"\[(\d+)/(\d+)\] \[([^\]]+)\]");
                    if (raidStatusMatch.Success)
                    {
                        arrayInfo.ActiveDevices = int.Parse(raidStatusMatch.Groups[1].Value);
                        arrayInfo.TotalDevices = int.Parse(raidStatusMatch.Groups[2].Value);
                        arrayInfo.Status = raidStatusMatch.Groups[3].Value.Select(c => c.ToString()).ToArray();
                    }
                }
                
                // Check for resync/recovery line
                if (i + 2 < lines.Length && (
                    lines[i + 2].Contains("resync") || 
                    lines[i + 2].Contains("recovery") || 
                    lines[i + 2].Contains("check")))
                {
                    var resyncLine = lines[i + 2].Trim();
                    arrayInfo.ResyncInProgress = true;
                    
                    // Parse resync percentage
                    var resyncMatch = Regex.Match(resyncLine, @"= *([0-9.]+)% \((\d+)/(\d+)\)");
                    if (resyncMatch.Success)
                    {
                        arrayInfo.ResyncPercentage = double.Parse(resyncMatch.Groups[1].Value);
                    }
                    
                    // Parse finish time estimate
                    var finishMatch = Regex.Match(resyncLine, @"finish=([0-9.]+)min");
                    if (finishMatch.Success)
                    {
                        arrayInfo.ResyncTimeEstimate = double.Parse(finishMatch.Groups[1].Value);
                    }
                    
                    // Parse speed
                    var speedMatch = Regex.Match(resyncLine, @"speed=([0-9.]+[KMG])/sec");
                    if (speedMatch.Success)
                    {
                        arrayInfo.ResyncSpeed = speedMatch.Groups[1].Value;
                    }
                }
                
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
            if (!File.Exists(METADATA_FILE_PATH))
            {
                return new List<PoolMetadata>();
            }
            
            var json = await File.ReadAllTextAsync(METADATA_FILE_PATH);
            var metadata = System.Text.Json.JsonSerializer.Deserialize<PoolMetadataCollection>(json);
            return metadata?.Pools ?? new List<PoolMetadata>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading pool metadata file");
            return new List<PoolMetadata>();
        }
    }
}