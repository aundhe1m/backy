using Backy.Agent.Models;

namespace Backy.Agent.Services;

public interface IPoolService
{
    Task<List<PoolListItem>> GetAllPoolsAsync();
    Task<(bool Success, string Message, PoolDetailResponse PoolDetail)> GetPoolDetailByMdDeviceAsync(string mdDeviceName);
    Task<(bool Success, string Message, PoolDetailResponse PoolDetail)> GetPoolDetailByGuidAsync(Guid poolGroupGuid);
    Task<(bool Success, string Message, Guid PoolGroupGuid, string MdDeviceName, string? MountPath, List<string> Outputs)> CreatePoolAsync(PoolCreationRequest request);
    Task<(bool Success, string Message, List<string> Outputs)> MountPoolByMdDeviceAsync(string mdDeviceName, string mountPath);
    Task<(bool Success, string Message, List<string> Outputs)> MountPoolByGuidAsync(Guid poolGroupGuid, string mountPath);
    Task<(bool Success, string Message, List<string> Outputs)> UnmountPoolByMdDeviceAsync(string mdDeviceName);
    Task<(bool Success, string Message, List<string> Outputs)> UnmountPoolByGuidAsync(Guid poolGroupGuid);
    Task<(bool Success, string Message, List<string> Outputs)> RemovePoolByMdDeviceAsync(string mdDeviceName);
    Task<(bool Success, string Message, List<string> Outputs)> RemovePoolByGuidAsync(Guid poolGroupGuid);
    
    // Metadata management methods
    Task<(bool Success, string Message)> SavePoolMetadataAsync(PoolMetadata metadata);
    Task<(bool Success, string Message)> RemovePoolMetadataAsync(PoolMetadataRemovalRequest request);
    Task<PoolMetadata?> GetPoolMetadataByMdDeviceAsync(string mdDeviceName);
    Task<PoolMetadata?> GetPoolMetadataByGuidAsync(Guid poolGroupGuid);
    Task<string?> ResolveMdDeviceAsync(Guid? poolGroupGuid);
    Task<(bool Success, string Message, int FixedEntries)> ValidateAndUpdatePoolMetadataAsync();
}

public class PoolService : IPoolService
{
    private readonly ISystemCommandService _commandService;
    private readonly IDriveService _driveService;
    private readonly IMdStatReader _mdStatReader;
    private readonly ILogger<PoolService> _logger;
    private readonly IFileSystemInfoService _fileSystemInfoService;
    private readonly IDriveInfoService _driveInfoService;

    // Metadata file path
    private const string METADATA_FILE_PATH = "/var/lib/backy/pool-metadata.json";

    public PoolService(
        ISystemCommandService commandService,
        IDriveService driveService,
        IMdStatReader mdStatReader,
        ILogger<PoolService> logger,
        IFileSystemInfoService fileSystemInfoService,
        IDriveInfoService driveInfoService)
    {
        _commandService = commandService;
        _driveService = driveService;
        _mdStatReader = mdStatReader;
        _logger = logger;
        _fileSystemInfoService = fileSystemInfoService;
        _driveInfoService = driveInfoService;
        
        // Automatically validate and update pool metadata during initialization
        // Using Task.Run to avoid blocking the constructor
        Task.Run(AutoRecoverPoolsAsync);
    }
    
    /// <summary>
    /// Automatically validates and recovers pools during service initialization
    /// </summary>
    private async Task AutoRecoverPoolsAsync()
    {
        try
        {
            _logger.LogInformation("Starting automatic pool recovery and validation");
            
            // Load the metadata
            var allMetadata = await LoadMetadataAsync();
            
            if (allMetadata.Pools.Count == 0)
            {
                _logger.LogInformation("No pools found in metadata to recover");
                return;
            }
            
            // Get current active drives
            var drivesResult = await _driveService.GetDrivesAsync();
            if (drivesResult?.Blockdevices == null)
            {
                _logger.LogWarning("Failed to get active drives for pool recovery");
                return;
            }
            
            // Scan for existing MD arrays
            var mdStat = await _mdStatReader.GetMdStatInfoAsync();
            if (mdStat == null || mdStat.Arrays.Count == 0)
            {
                _logger.LogInformation("No MD arrays found on the system");
            }
            
            // Get currently mounted filesystems
            var mountedFilesystems = await _driveInfoService.GetMountedFilesystemsAsync();
            var mountedMdDevices = mountedFilesystems
                .Where(m => m.Device.StartsWith("/dev/md"))
                .Select(m => System.IO.Path.GetFileName(m.Device))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            
            _logger.LogInformation("Found {Count} mounted MD devices: {Devices}", 
                mountedMdDevices.Count, string.Join(", ", mountedMdDevices));

            // Create a mapping of drive serials to their corresponding block devices
            var serialToDeviceMap = new Dictionary<string, BlockDevice>(StringComparer.OrdinalIgnoreCase);
            foreach (var device in drivesResult.Blockdevices)
            {
                if (!string.IsNullOrEmpty(device.Serial))
                {
                    serialToDeviceMap[device.Serial] = device;
                }
            }
            
            // Create a mapping of drive serials to their current MD device
            var serialToMdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var mdToSerialsMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            
            // First, identify all drives that are part of MD arrays
            foreach (var device in drivesResult.Blockdevices)
            {
                // Check if this device has children that include an MD device
                if (device.Children != null)
                {
                    foreach (var child in device.Children)
                    {
                        if (child.Name.StartsWith("md") && !string.IsNullOrEmpty(device.Serial))
                        {
                            serialToMdMap[device.Serial] = child.Name;
                            
                            // Also track which serials are part of each MD device
                            if (!mdToSerialsMap.TryGetValue(child.Name, out var serials))
                            {
                                serials = new List<string>();
                                mdToSerialsMap[child.Name] = serials;
                            }
                            
                            serials.Add(device.Serial);
                            break;
                        }
                    }
                }
            }
            
            _logger.LogDebug("Found {Count} drives that belong to MD arrays", serialToMdMap.Count);

            // Process each pool in metadata
            var processedMds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var poolMetadata in allMetadata.Pools)
            {
                try
                {
                    int driveCount = poolMetadata.DriveSerials?.Count ?? 0;
                    _logger.LogInformation("Processing pool {PoolGuid} ({MdDeviceName}) with {DriveCount} drives",
                        poolMetadata.PoolGroupGuid, poolMetadata.MdDeviceName, driveCount);
                    
                    // Skip if no drive serials in metadata
                    if (poolMetadata.DriveSerials == null || poolMetadata.DriveSerials.Count == 0)
                    {
                        _logger.LogWarning("Pool {PoolGuid} has no drive serials in metadata, skipping",
                            poolMetadata.PoolGroupGuid);
                        continue;
                    }
                    
                    // Find the current MD device for this pool based on drive serials
                    string? currentMdDevice = null;
                    var matchingDriveSerials = poolMetadata.DriveSerials
                        .Where(serial => !string.IsNullOrEmpty(serial) && serialToMdMap.ContainsKey(serial))
                        .ToList();
                    
                    if (matchingDriveSerials.Count > 0)
                    {
                        // Get the MD device that contains these drives
                        var mdDevices = matchingDriveSerials
                            .Where(serial => serialToMdMap.ContainsKey(serial))
                            .Select(serial => serialToMdMap[serial])
                            .Distinct()
                            .ToList();
                        
                        if (mdDevices.Count == 1)
                        {
                            // If all drives belong to the same MD device, use that
                            currentMdDevice = mdDevices[0];
                            _logger.LogInformation("Pool {PoolGuid} is currently using MD device {CurrentMdDevice}",
                                poolMetadata.PoolGroupGuid, currentMdDevice);
                        }
                        else if (mdDevices.Count > 1)
                        {
                            // If drives belong to different MD devices, this is unexpected
                            _logger.LogWarning("Pool {PoolGuid} has drives across multiple MD devices: {MdDevices}",
                                poolMetadata.PoolGroupGuid, string.Join(", ", mdDevices));
                                
                            // Use the MD device with the most matching drives
                            var mdCounts = new Dictionary<string, int>();
                            foreach (var serial in matchingDriveSerials)
                            {
                                if (serialToMdMap.TryGetValue(serial, out var md))
                                {
                                    if (!mdCounts.ContainsKey(md))
                                    {
                                        mdCounts[md] = 0;
                                    }
                                    mdCounts[md]++;
                                }
                            }
                            
                            if (mdCounts.Any())
                            {
                                currentMdDevice = mdCounts.OrderByDescending(kv => kv.Value).First().Key;
                                _logger.LogInformation("Selected MD device {CurrentMdDevice} with most matching drives",
                                    currentMdDevice);
                            }
                        }
                    }
                    
                    // If we found the current MD device for this pool
                    if (!string.IsNullOrEmpty(currentMdDevice))
                    {
                        // Check if we've already processed this MD device
                        if (processedMds.Contains(currentMdDevice))
                        {
                            _logger.LogInformation("MD device {CurrentMdDevice} already processed, skipping",
                                currentMdDevice);
                            continue;
                        }
                        
                        // Update the metadata if necessary
                        if (poolMetadata.MdDeviceName != currentMdDevice)
                        {
                            _logger.LogInformation("Updating pool {PoolGuid} metadata to use MD device {CurrentMdDevice} (was {OldMdDevice})",
                                poolMetadata.PoolGroupGuid, currentMdDevice, poolMetadata.MdDeviceName);
                                
                            poolMetadata.MdDeviceName = currentMdDevice;
                            await SavePoolMetadataAsync(poolMetadata);
                        }
                        
                        // Check if the pool is already mounted
                        bool isCurrentlyMounted = mountedMdDevices.Contains(currentMdDevice);
                        if (isCurrentlyMounted)
                        {
                            _logger.LogInformation("Pool {PoolGuid} ({CurrentMdDevice}) is already mounted",
                                poolMetadata.PoolGroupGuid, currentMdDevice);
                                
                            // Mark as processed
                            processedMds.Add(currentMdDevice);
                            continue;
                        }
                        
                        // Auto-mount the pool only if IsMounted is true
                        if (poolMetadata.IsMounted == true && !string.IsNullOrEmpty(poolMetadata.LastMountPath))
                        {
                            _logger.LogInformation("Auto-mounting pool {PoolGuid} ({CurrentMdDevice}) at {MountPath} because IsMounted=true",
                                poolMetadata.PoolGroupGuid, currentMdDevice, poolMetadata.LastMountPath);
                                
                            // Ensure mount directory exists
                            if (!_fileSystemInfoService.DirectoryExists(poolMetadata.LastMountPath))
                            {
                                Directory.CreateDirectory(poolMetadata.LastMountPath);
                                _logger.LogInformation("Created mount directory at {MountPath}", poolMetadata.LastMountPath);
                            }
                            
                            // Mount the pool directly without reassembly since it's already assembled
                            var mountResult = await _commandService.ExecuteCommandAsync($"mount /dev/{currentMdDevice} {poolMetadata.LastMountPath}", true);
                            
                            if (mountResult.Success)
                            {
                                _logger.LogInformation("Successfully auto-mounted pool {PoolGuid} ({CurrentMdDevice}) at {MountPath}",
                                    poolMetadata.PoolGroupGuid, currentMdDevice, poolMetadata.LastMountPath);
                            }
                            else
                            {
                                _logger.LogWarning("Failed to auto-mount pool {PoolGuid} ({CurrentMdDevice}) at {MountPath}: {Error}",
                                    poolMetadata.PoolGroupGuid, currentMdDevice, poolMetadata.LastMountPath, 
                                    mountResult.Output != null ? mountResult.Output.Trim() : (mountResult.Error != null ? mountResult.Error.ToString() : "Unknown error"));
                            }
                        }
                        else
                        {
                            if (poolMetadata.IsMounted == false)
                            {
                                _logger.LogInformation("Skipping auto-mount for pool {PoolGuid} ({CurrentMdDevice}) because IsMounted is set to false in metadata",
                                    poolMetadata.PoolGroupGuid, currentMdDevice);
                            }
                            else
                            {
                                _logger.LogInformation("Pool {PoolGuid} ({CurrentMdDevice}) is not mounted and has no LastMountPath, skipping auto-mount",
                                    poolMetadata.PoolGroupGuid, currentMdDevice);
                            }
                        }
                        
                        // Mark as processed
                        processedMds.Add(currentMdDevice);
                    }
                    else
                    {
                        // MD device not currently active, try to assemble it
                        _logger.LogInformation("Pool {PoolGuid} ({MdDeviceName}) is not currently active, attempting to assemble",
                            poolMetadata.PoolGroupGuid, poolMetadata.MdDeviceName);
                            
                        // Skip assembly if IsMounted is explicitly set to false
                        if (poolMetadata.IsMounted == false)
                        {
                            _logger.LogInformation("Skipping assembly for pool {PoolGuid} ({MdDeviceName}) because IsMounted is set to false in metadata",
                                poolMetadata.PoolGroupGuid, poolMetadata.MdDeviceName);
                            continue;
                        }
                            
                        // Find the device paths for the drives in this pool
                        var devicePaths = new List<string>();
                        foreach (var serial in poolMetadata.DriveSerials)
                        {
                            if (serialToDeviceMap.TryGetValue(serial, out var device))
                            {
                                string? devicePath = null;
                                
                                // Prefer ID-LINK or PATH if available
                                if (!string.IsNullOrEmpty(device.IdLink))
                                {
                                    if (device.IdLink.StartsWith("/dev/"))
                                        devicePath = device.IdLink;
                                    else
                                        devicePath = $"/dev/disk/by-id/{device.IdLink}";
                                }
                                else if (!string.IsNullOrEmpty(device.Path))
                                {
                                    devicePath = device.Path;
                                }
                                else if (!string.IsNullOrEmpty(device.Name))
                                {
                                    devicePath = $"/dev/{device.Name}";
                                }
                                
                                if (!string.IsNullOrEmpty(devicePath))
                                {
                                    devicePaths.Add(devicePath);
                                }
                            }
                        }
                        
                        if (devicePaths.Count > 0)
                        {
                            // Try to scan first
                            await _commandService.ExecuteCommandAsync("mdadm --scan", true);
                            
                            // Try to assemble the array with auto-detection (no specific device name)
                            var assembleResult = await _commandService.ExecuteCommandAsync(
                                $"mdadm --assemble --scan {string.Join(" ", devicePaths)}", true);
                                
                            if (assembleResult.Success)
                            {
                                _logger.LogInformation("Successfully assembled pool {PoolGuid}, rescanning to find new MD device",
                                    poolMetadata.PoolGroupGuid);
                                    
                                // Refresh the MD state to find the new device name
                                var refreshedMdStat = await _mdStatReader.GetMdStatInfoAsync();
                                
                                // Find the MD device containing these drives
                                foreach (var (mdName, arrayInfo) in refreshedMdStat.Arrays)
                                {
                                    // Skip if we've already processed this MD
                                    if (processedMds.Contains(mdName))
                                        continue;
                                        
                                    // Get the device paths for this array
                                    var arrayDevicePaths = new List<string>();
                                    foreach (var deviceName in arrayInfo.Devices)
                                    {
                                        arrayDevicePaths.Add($"/dev/{deviceName}");
                                    }
                                    
                                    // Check if this array contains our drives
                                    var intersection = devicePaths.Intersect(arrayDevicePaths, StringComparer.OrdinalIgnoreCase).ToList();
                                    if (intersection.Count > 0)
                                    {
                                        _logger.LogInformation("Found MD device {MdName} containing {Count} of our drives",
                                            mdName, intersection.Count);
                                            
                                        // Update the metadata
                                        poolMetadata.MdDeviceName = mdName;
                                        await SavePoolMetadataAsync(poolMetadata);
                                        
                                        // Try to mount it only if IsMounted is true
                                        if (poolMetadata.IsMounted == true && !string.IsNullOrEmpty(poolMetadata.LastMountPath))
                                        {
                                            // Ensure mount directory exists
                                            if (!_fileSystemInfoService.DirectoryExists(poolMetadata.LastMountPath))
                                            {
                                                Directory.CreateDirectory(poolMetadata.LastMountPath);
                                            }
                                            
                                            // Mount the pool
                                            var mountResult = await _commandService.ExecuteCommandAsync(
                                                $"mount /dev/{mdName} {poolMetadata.LastMountPath}", true);
                                                
                                            if (mountResult.Success)
                                            {
                                                _logger.LogInformation("Successfully auto-mounted pool {PoolGuid} ({MdName}) at {MountPath}",
                                                    poolMetadata.PoolGroupGuid, mdName, poolMetadata.LastMountPath);
                                            }
                                            else
                                            {
                                                _logger.LogWarning("Failed to auto-mount pool {PoolGuid} ({MdName}) at {MountPath}: {Error}",
                                                    poolMetadata.PoolGroupGuid, mdName, poolMetadata.LastMountPath, 
                                                    mountResult.Output != null ? mountResult.Output.Trim() : (mountResult.Error != null ? mountResult.Error.ToString() : "Unknown error"));
                                            }
                                        }
                                        else
                                        {
                                            _logger.LogInformation("Skipping mount for assembled pool {PoolGuid} ({MdName}) because IsMounted is not set to true",
                                                poolMetadata.PoolGroupGuid, mdName);
                                        }
                                        
                                        // Mark as processed
                                        processedMds.Add(mdName);
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                string errorMessage = "";
                                if (assembleResult.Error != null) 
                                {
                                    errorMessage = assembleResult.Error.ToString();
                                }
                                else if (assembleResult.Output != null)
                                {
                                    errorMessage = assembleResult.Output.Trim();
                                }
                                
                                _logger.LogWarning("Failed to assemble pool {PoolGuid}: {Error}",
                                    poolMetadata.PoolGroupGuid, errorMessage);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Could not find device paths for any drives in pool {PoolGuid}",
                                poolMetadata.PoolGroupGuid);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing pool {PoolGuid} during auto-recovery",
                        poolMetadata.PoolGroupGuid);
                }
            }
            
            // Log summary
            _logger.LogInformation("Auto-recovery completed. Processed {Count} MD devices", processedMds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during automatic pool recovery");
        }
    }

    public async Task<List<PoolListItem>> GetAllPoolsAsync()
    {
        var result = new List<PoolListItem>();
        
        try
        {
            // Get all available drives on the system
            var drivesResult = await _driveService.GetDrivesAsync();
            
            // Create a map of device names to their details for easy lookup
            var deviceNameToDriveMap = new Dictionary<string, BlockDevice>(StringComparer.OrdinalIgnoreCase);
            var serialToDeviceMap = new Dictionary<string, BlockDevice>(StringComparer.OrdinalIgnoreCase);
            var connectedDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            if (drivesResult?.Blockdevices != null)
            {
                foreach (var drive in drivesResult.Blockdevices.Where(d => d.Type == "disk"))
                {
                    // Store device by name for lookups
                    if (!string.IsNullOrEmpty(drive.Name))
                    {
                        deviceNameToDriveMap[drive.Name] = drive;
                    }
                    
                    // Store device by serial for lookups
                    if (!string.IsNullOrEmpty(drive.Serial))
                    {
                        serialToDeviceMap[drive.Serial] = drive;
                        connectedDrives.Add(drive.Serial);
                    }
                }
            }
            
            // Get all active md arrays using the MdStatReader
            var mdStatInfo = await _mdStatReader.GetMdStatInfoAsync();
            if (mdStatInfo.Arrays.Count == 0)
            {
                _logger.LogInformation("No MD arrays found on the system");
                return result;
            }
            
            // Process each MD array
            foreach (var (deviceName, arrayInfo) in mdStatInfo.Arrays)
            {
                var poolItem = new PoolListItem
                {
                    MdDeviceName = deviceName,
                    Status = arrayInfo.IsActive ? "active" : "inactive"
                };
                
                // Add resync information if available
                if (arrayInfo.ResyncInProgress)
                {
                    poolItem.ResyncPercentage = arrayInfo.ResyncPercentage;
                    poolItem.ResyncTimeEstimate = arrayInfo.ResyncTimeEstimate;
                    
                    // Update status to indicate resync
                    if (poolItem.Status == "active")
                    {
                        poolItem.Status = "resync";
                    }
                }
                
                // Check if mounted
                var mountResult = await _commandService.ExecuteCommandAsync($"mount | grep '/dev/{deviceName}'");
                if (mountResult.Success)
                {
                    var mountMatch = System.Text.RegularExpressions.Regex.Match(
                        CleanCommandOutput(mountResult.Output ?? string.Empty), $@"/dev/{deviceName} on (.*?) type");
                    
                    if (mountMatch.Success)
                    {
                        poolItem.MountPath = mountMatch.Groups[1].Value;
                        poolItem.IsMounted = true;
                    }
                }
                
                // Check metadata for this pool to get labels and GUID
                var metadata = await GetPoolMetadataByMdDeviceAsync(deviceName);
                if (metadata != null)
                {
                    poolItem.PoolGroupGuid = metadata.PoolGroupGuid;
                    poolItem.Label = metadata.Label;
                }
                else
                {
                    // Create a default label if metadata doesn't exist
                    poolItem.Label = $"Pool-{deviceName}";
                    _logger.LogInformation("Metadata not found for pool {DeviceName}, using default label", deviceName);
                }
                
                // Always get drive details regardless of metadata existence
                foreach (var devicePath in arrayInfo.Devices)
                {
                    string serial;
                    string label;
                    bool isConnected;
                    
                    // Find the BlockDevice by its name (the devicePath from arrayInfo.Devices)
                    if (deviceNameToDriveMap.TryGetValue(devicePath, out var blockDevice))
                    {
                        // We found the device in our map, use its serial
                        serial = blockDevice.Serial ?? "unknown";
                        isConnected = !string.IsNullOrEmpty(serial) && connectedDrives.Contains(serial);
                        
                        // Get label from metadata if available
                        if (metadata != null && !string.IsNullOrEmpty(serial) && metadata.DriveLabels.ContainsKey(serial))
                        {
                            label = metadata.DriveLabels[serial];
                        }
                        else
                        {
                            label = $"{deviceName}-{poolItem.Drives.Count + 1}";
                        }
                    }
                    else
                    {
                        // Fallback - this shouldn't happen often as deviceNameToDriveMap should have all drives
                        serial = "unknown";
                        label = $"{deviceName}-{poolItem.Drives.Count + 1}";
                        isConnected = false;
                        _logger.LogWarning("Device {DevicePath} not found in block devices list", devicePath);
                    }
                    
                    poolItem.Drives.Add(new PoolDriveSummary
                    {
                        Serial = serial,
                        Label = label,
                        IsConnected = isConnected
                    });
                }
                
                // If we have metadata with drive serials but couldn't match them in arrayInfo.Devices,
                // add them from the metadata to ensure all drives are represented
                if (metadata != null && metadata.DriveSerials != null && metadata.DriveSerials.Count > 0)
                {
                    // Get the current serials in the pool
                    var existingSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var drive in poolItem.Drives)
                    {
                        if (!string.IsNullOrEmpty(drive.Serial))
                        {
                            existingSerials.Add(drive.Serial);
                        }
                    }
                    
                    // Find any serials in the metadata that aren't in the pool yet
                    foreach (var serial in metadata.DriveSerials)
                    {
                        if (string.IsNullOrEmpty(serial) || existingSerials.Contains(serial))
                            continue;
                            
                        // Get label from metadata if available
                        string label = $"{deviceName}-{poolItem.Drives.Count + 1}";
                        if (metadata.DriveLabels.ContainsKey(serial))
                        {
                            label = metadata.DriveLabels[serial];
                        }
                        
                        bool isConnected = connectedDrives.Contains(serial);
                        
                        poolItem.Drives.Add(new PoolDriveSummary
                        {
                            Serial = serial,
                            Label = label,
                            IsConnected = isConnected
                        });
                    }
                }
                
                result.Add(poolItem);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all pools");
            return result;
        }
    }

    public async Task<(bool Success, string Message, PoolDetailResponse PoolDetail)> GetPoolDetailByMdDeviceAsync(string mdDeviceName)
    {
        try
        {
            // Get array information from MdStatReader
            var arrayInfo = await _mdStatReader.GetArrayInfoAsync(mdDeviceName);
            if (arrayInfo == null)
            {
                // If MdStatReader doesn't find the array, fall back to mdadm command
                var mdadmDetail = await _commandService.ExecuteCommandAsync($"mdadm --detail /dev/{mdDeviceName}");
                if (!mdadmDetail.Success)
                {
                    return (false, $"Pool '{mdDeviceName}' not found", new PoolDetailResponse());
                }
            }
            
            var response = new PoolDetailResponse();
            
            // Get pool status from array info or fall back to DriveService
            if (arrayInfo != null)
            {
                response.Status = arrayInfo.IsActive ? "active" : "inactive";
                
                // Add resync information if available
                if (arrayInfo.ResyncInProgress)
                {
                    response.ResyncPercentage = arrayInfo.ResyncPercentage;
                    response.ResyncTimeEstimate = arrayInfo.ResyncTimeEstimate;
                    
                    // Update status to indicate resync
                    if (response.Status == "active")
                    {
                        response.Status = "resync";
                    }
                }
            }
            else
            {
                // Fall back to using DriveService
                response.Status = await _driveService.GetPoolStatusAsync(mdDeviceName);
            }
            
            // Find mount point for pool
            var mountResult = await _commandService.ExecuteCommandAsync($"mount | grep '/dev/{mdDeviceName}'");
            if (mountResult.Success)
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    CleanCommandOutput(mountResult.Output ?? string.Empty), $@"/dev/{mdDeviceName} on (.*?) type");
                
                if (match.Success)
                {
                    response.MountPath = match.Groups[1].Value;
                    
                    // Get size information if mounted
                    var sizeInfo = await _driveService.GetMountPointSizeAsync(response.MountPath);
                    response.Size = sizeInfo.Size;
                    response.Used = sizeInfo.Used;
                    response.Available = sizeInfo.Available;
                    response.UsePercent = sizeInfo.UsePercent;
                }
            }
            
            // Get component drive information
            if (arrayInfo != null)
            {
                // Use array info from MdStatReader for drive details
                var metadata = await GetPoolMetadataByMdDeviceAsync(mdDeviceName);
                
                // Add each drive to the response
                foreach (var devicePath in arrayInfo.Devices)
                {
                    // Find status based on array info
                    string status = "unknown";
                    int deviceIndex = arrayInfo.Devices.IndexOf(devicePath);
                    if (deviceIndex < arrayInfo.Status.Length)
                    {
                        // The status letters in [UU_] format
                        string statusLetter = arrayInfo.Status[deviceIndex];
                        status = statusLetter == "U" ? "active" : 
                                 statusLetter == "_" ? "failed" :
                                 statusLetter == "S" ? "spare" : "unknown";
                    }
                    
                    // Try to get serial number using lsblk
                    var lsblk = await _commandService.ExecuteCommandAsync($"lsblk -n -o SERIAL /dev/{devicePath}");
                    string serial = lsblk.Success ? CleanCommandOutput(lsblk.Output ?? string.Empty).Trim() : "unknown";
                    
                    // Try to get label from metadata
                    string label = $"{mdDeviceName}-{response.Drives.Count + 1}";
                    if (metadata != null && metadata.DriveLabels.ContainsKey(serial))
                    {
                        label = metadata.DriveLabels[serial];
                    }
                    
                    response.Drives.Add(new PoolDriveStatus
                    {
                        Serial = serial,
                        Label = label,
                        Status = status
                    });
                }
            }
            else
            {
                // Fall back to parsing mdadm output
                var mdadmDetail = await _commandService.ExecuteCommandAsync($"mdadm --detail /dev/{mdDeviceName}");
                var lines = CleanCommandOutput(mdadmDetail.Output ?? string.Empty).Split('\n');
                
                foreach (var line in lines)
                {
                    if (line.Trim().Contains("/dev/") && !line.Contains("md"))
                    {
                        var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4)
                        {
                            string devicePath = parts[parts.Length - 1];
                            string status = line.Contains("active") ? "active" : 
                                            line.Contains("faulty") ? "faulty" : 
                                            line.Contains("spare") ? "spare" : "unknown";
                            
                            // Try to get serial number
                            var deviceName = System.IO.Path.GetFileName(devicePath);
                            var lsblk = await _commandService.ExecuteCommandAsync($"lsblk -n -o SERIAL /dev/{deviceName}");
                            string serial = lsblk.Success ? CleanCommandOutput(lsblk.Output ?? string.Empty).Trim() : "unknown";
                            
                            // Try to get label from metadata
                            string label = $"{mdDeviceName}-{response.Drives.Count + 1}";
                            var metadata = await GetPoolMetadataByMdDeviceAsync(mdDeviceName);
                            if (metadata != null && metadata.DriveLabels.ContainsKey(serial))
                            {
                                label = metadata.DriveLabels[serial];
                            }
                            
                            response.Drives.Add(new PoolDriveStatus
                            {
                                Serial = serial,
                                Label = label,
                                Status = status
                            });
                        }
                    }
                }
            }
            
            return (true, "Pool details retrieved successfully", response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pool details for {MdDeviceName}", mdDeviceName);
            return (false, $"Error retrieving pool details: {ex.Message}", new PoolDetailResponse { Status = "Error" });
        }
    }

    public async Task<(bool Success, string Message, PoolDetailResponse PoolDetail)> GetPoolDetailByGuidAsync(Guid poolGroupGuid)
    {
        try
        {
            // Get array information from MdStatReader directly using GUID
            var arrayInfo = await _mdStatReader.GetArrayInfoByGuidAsync(poolGroupGuid);
            if (arrayInfo == null)
            {
                return (false, $"Pool with GUID '{poolGroupGuid}' not found", new PoolDetailResponse());
            }
            
            // Resolve the mdDeviceName from the array info
            string mdDeviceName = arrayInfo.DeviceName;

            // Get metadata for this pool
            var metadata = await GetPoolMetadataByGuidAsync(poolGroupGuid);
            
            var response = new PoolDetailResponse
            {
                Status = arrayInfo.IsActive ? "active" : "inactive"
            };
            
            // Add resync information if available
            if (arrayInfo.ResyncInProgress)
            {
                response.ResyncPercentage = arrayInfo.ResyncPercentage;
                response.ResyncTimeEstimate = arrayInfo.ResyncTimeEstimate;
                
                // Update status to indicate resync
                if (response.Status == "active")
                {
                    response.Status = "resync";
                }
            }

            // Get mount point information using DriveInfoService
            var mountedFilesystems = await _driveInfoService.GetMountedFilesystemsAsync();
            var mdMount = mountedFilesystems.FirstOrDefault(m => m.Device.Contains($"/dev/{mdDeviceName}"));
            
            if (mdMount != null)
            {
                response.MountPath = mdMount.MountPoint;
                
                // Get size information if mounted
                var sizeInfo = await _driveService.GetMountPointSizeAsync(response.MountPath);
                response.Size = sizeInfo.Size;
                response.Used = sizeInfo.Used;
                response.Available = sizeInfo.Available;
                response.UsePercent = sizeInfo.UsePercent;
            }
            
            // Get all drives in the system for looking up by name and by serial
            var drivesResult = await _driveService.GetDrivesAsync();
            var deviceNameToDriveMap = new Dictionary<string, BlockDevice>(StringComparer.OrdinalIgnoreCase);
            var serialToDriveMap = new Dictionary<string, BlockDevice>(StringComparer.OrdinalIgnoreCase);
            var connectedDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            if (drivesResult?.Blockdevices != null)
            {
                foreach (var drive in drivesResult.Blockdevices.Where(d => d.Type == "disk"))
                {
                    // Store device by name for lookups
                    if (!string.IsNullOrEmpty(drive.Name))
                    {
                        deviceNameToDriveMap[drive.Name] = drive;
                    }

                    // Store device by serial for lookups
                    if (!string.IsNullOrEmpty(drive.Serial))
                    {
                        serialToDriveMap[drive.Serial] = drive;
                        connectedDrives.Add(drive.Serial); // Track all connected drive serials
                    }
                }
            }
            
            // Add each component drive to the response
            var addedSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // Track which serials we've added
            
            // First add drives based on the array status characters [UU] directly from mdstat
            // This is the most reliable source of drive status
            if (arrayInfo.Status?.Length > 0)
            {
                _logger.LogDebug("Array status for {MdDeviceName}: [{Status}]", 
                    mdDeviceName, string.Join("", arrayInfo.Status));

                // First pass: process drives we can directly map from arrayInfo.Devices
                for (int i = 0; i < arrayInfo.Devices.Count; i++)
                {
                    string devicePath = arrayInfo.Devices[i];
                    string status = "unknown";
                    
                    // If we have a status character for this device position
                    if (i < arrayInfo.Status.Length)
                    {
                        // Map the status character to a human-readable status
                        string statusLetter = arrayInfo.Status[i];
                        status = statusLetter == "U" ? "active" : 
                                 statusLetter == "_" ? "failed" :
                                 statusLetter == "S" ? "spare" : "unknown";
                    }
                    
                    // Try to lookup the device in our mapping
                    string serial = "unknown";
                    string label = $"{mdDeviceName}-{i+1}";
                    bool foundDevice = false;
                    
                    // Method 1: Direct device name mapping
                    if (deviceNameToDriveMap.TryGetValue(devicePath, out var blockDevice) && 
                        !string.IsNullOrEmpty(blockDevice.Serial))
                    {
                        serial = blockDevice.Serial;
                        foundDevice = true;
                    }
                    // Method 2: If we couldn't find by devicePath directly, try extracting the short name
                    else
                    {
                        // Extract just the device name part (e.g., "sdc" from "/dev/sdc1")
                        string shortName = devicePath;
                        if (shortName.Contains("/"))
                        {
                            shortName = System.IO.Path.GetFileName(devicePath);
                        }
                        
                        // Try with the short name
                        if (deviceNameToDriveMap.TryGetValue(shortName, out blockDevice) &&
                            !string.IsNullOrEmpty(blockDevice.Serial))
                        {
                            serial = blockDevice.Serial;
                            foundDevice = true;
                        }
                    }
                    
                    // If we found the device, get its label
                    if (foundDevice && metadata != null && metadata.DriveLabels.ContainsKey(serial))
                    {
                        label = metadata.DriveLabels[serial];
                    }
                    
                    // Add the drive to our response
                    response.Drives.Add(new PoolDriveStatus
                    {
                        Serial = serial,
                        Label = label,
                        Status = status
                    });
                    
                    // Track that we've added this serial
                    if (serial != "unknown")
                    {
                        addedSerials.Add(serial);
                    }
                }
            }

            // Add any drives from metadata that we haven't already added
            if (metadata != null && metadata.DriveSerials != null)
            {
                foreach (var serial in metadata.DriveSerials)
                {
                    if (string.IsNullOrEmpty(serial) || addedSerials.Contains(serial))
                    {
                        continue;
                    }
                    
                    // Determine status based on whether the drive is connected
                    string status = connectedDrives.Contains(serial) ? "active" : "disconnected";
                    
                    // Get label from metadata
                    string label = $"{mdDeviceName}-{response.Drives.Count + 1}";
                    if (metadata.DriveLabels.ContainsKey(serial))
                    {
                        label = metadata.DriveLabels[serial];
                    }
                    
                    // Add drive to response
                    response.Drives.Add(new PoolDriveStatus
                    {
                        Serial = serial,
                        Label = label,
                        Status = status
                    });
                    
                    addedSerials.Add(serial);
                }
            }
            
            return (true, "Pool details retrieved successfully", response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pool details by GUID {PoolGroupGuid}", poolGroupGuid);
            return (false, $"Error retrieving pool details: {ex.Message}", new PoolDetailResponse { Status = "Error" });
        }
    }

    public async Task<(bool Success, string Message, Guid PoolGroupGuid, string MdDeviceName, string? MountPath, List<string> Outputs)> CreatePoolAsync(
        PoolCreationRequest request)
    {
        var outputs = new List<string>();
        
        try
        {
            // Handle extended request with PoolGroupGuid
            Guid? poolGroupGuid = null;
            
            if (request is PoolCreationRequestExtended extendedRequest)
            {
                poolGroupGuid = extendedRequest.PoolGroupGuid;
            }

            // Validate request
            if (string.IsNullOrWhiteSpace(request.Label))
            {
                return (false, "Pool label is required", Guid.Empty, string.Empty, null, outputs);
            }

            if (request.DriveSerials == null || !request.DriveSerials.Any())
            {
                return (false, "At least one drive is required to create a pool", Guid.Empty, string.Empty, null, outputs);
            }

            // Validate mount path
            if (string.IsNullOrWhiteSpace(request.MountPath))
            {
                return (false, "Mount path is required", Guid.Empty, string.Empty, null, outputs);
            }

            if (!request.MountPath.StartsWith("/"))
            {
                return (false, "Mount path must be absolute", Guid.Empty, string.Empty, null, outputs);
            }

            // Get available drives
            var drivesResult = await _driveService.GetDrivesAsync();
            var availableDrives = drivesResult.Blockdevices?.Where(d => 
                d.Type == "disk" && 
                !string.IsNullOrEmpty(d.Serial) &&
                request.DriveSerials.Contains(d.Serial)).ToList();

            if (availableDrives == null || availableDrives.Count < request.DriveSerials.Count)
            {
                return (false, "One or more requested drives not found", Guid.Empty, string.Empty, null, outputs);
            }

            // Find an available md device
            var mdstatResult = await _commandService.ExecuteCommandAsync("cat /proc/mdstat");
            int poolId = GetNextAvailableMdDeviceId(mdstatResult.Output != null ? mdstatResult.Output : string.Empty);
            string poolDevice = $"md{poolId}";

            // Build mdadm command
            string mdadmCommand = $"mdadm --create /dev/{poolDevice} --level=1 --raid-devices={availableDrives.Count} ";
            
            // Construct proper device paths for each drive
            var devicePaths = availableDrives.Select(d => {
                if (!string.IsNullOrEmpty(d.IdLink) && d.IdLink.StartsWith("/dev/"))
                    return d.IdLink; // Already a full path
                else if (!string.IsNullOrEmpty(d.IdLink))
                    return $"/dev/disk/by-id/{d.IdLink}"; // Construct proper path for IdLink
                else if (!string.IsNullOrEmpty(d.Path))
                    return d.Path; // Use path as fallback
                else
                    return $"/dev/{d.Name}"; // Use name as final fallback
            });
            
            mdadmCommand += string.Join(" ", devicePaths);
            mdadmCommand += " --run --force";

            // Execute mdadm create command
            var mdadmResult = await _commandService.ExecuteCommandAsync(mdadmCommand, true);
            outputs.Add($"$ {mdadmResult.Command}");
            outputs.Add(CleanCommandOutput(mdadmResult.Output));

            if (!mdadmResult.Success)
            {
                return (false, $"Failed to create RAID array: {CleanCommandOutput(mdadmResult.Output)}", Guid.Empty, poolDevice, null, outputs);
            }

            // Format the new MD device
            string mkfsCommand = $"mkfs.ext4 -F /dev/{poolDevice}";
            var mkfsResult = await _commandService.ExecuteCommandAsync(mkfsCommand, true);
            outputs.Add($"$ {mkfsResult.Command}");
            outputs.Add(CleanCommandOutput(mkfsResult.Output));

            if (!mkfsResult.Success)
            {
                // Try to stop the array if formatting fails
                await _commandService.ExecuteCommandAsync($"mdadm --stop /dev/{poolDevice}", true);
                return (false, $"Failed to format RAID array: {CleanCommandOutput(mkfsResult.Output)}", Guid.Empty, poolDevice, null, outputs);
            }

            // Create mount directory
            var mountPath = request.MountPath;
            var mkdirResult = await _commandService.ExecuteCommandAsync($"mkdir -p {mountPath}");
            outputs.Add($"$ {mkdirResult.Command}");
            outputs.Add(CleanCommandOutput(mkdirResult.Output));

            if (!mkdirResult.Success)
            {
                // Try to stop the array if directory creation fails
                await _commandService.ExecuteCommandAsync($"mdadm --stop /dev/{poolDevice}", true);
                return (false, $"Failed to create mount directory: {CleanCommandOutput(mkdirResult.Output)}", Guid.Empty, poolDevice, null, outputs);
            }

            // Mount the new filesystem
            var mountResult = await _commandService.ExecuteCommandAsync($"mount /dev/{poolDevice} {mountPath}", true);
            outputs.Add($"$ {mountResult.Command}");
            outputs.Add(CleanCommandOutput(mountResult.Output));

            if (!mountResult.Success)
            {
                // Try to stop the array if mounting fails
                await _commandService.ExecuteCommandAsync($"mdadm --stop /dev/{poolDevice}", true);
                return (false, $"Failed to mount filesystem: {CleanCommandOutput(mountResult.Output)}", Guid.Empty, poolDevice, null, outputs);
            }

            // Save metadata
            var metadata = new PoolMetadata
            {
                MdDeviceName = poolDevice,
                Label = request.Label,
                DriveSerials = request.DriveSerials,
                DriveLabels = request.DriveLabels ?? new Dictionary<string, string>(),
                LastMountPath = mountPath,
                PoolGroupGuid = poolGroupGuid ?? Guid.NewGuid(),
                IsMounted = true // Set to true since we just mounted it
            };
            
            var saveResult = await SavePoolMetadataAsync(metadata);
            if (!saveResult.Success)
            {
                _logger.LogWarning("Failed to save pool metadata: {Message}", saveResult.Message);
                outputs.Add($"Warning: Failed to save pool metadata: {saveResult.Message}");
                // Continue despite metadata save failure
            }
            
            // Trigger a refresh of the drive cache to reflect the changes
            await _driveService.RefreshDrivesAsync();

            return (true, "Pool created successfully", metadata.PoolGroupGuid, poolDevice, mountPath, outputs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating pool");
            outputs.Add($"Error: {ex.Message}");
            return (false, $"Error creating pool: {ex.Message}", Guid.Empty, string.Empty, null, outputs);
        }
    }

    public async Task<(bool Success, string Message, List<string> Outputs)> MountPoolByMdDeviceAsync(
        string mdDeviceName, string mountPath)
    {
        var outputs = new List<string>();
        
        try
        {
            // Validate mount path
            if (string.IsNullOrWhiteSpace(mountPath))
            {
                return (false, "Mount path is required", outputs);
            }

            if (!mountPath.StartsWith("/"))
            {
                return (false, "Mount path must be absolute", outputs);
            }
            
            // Check if the mount path is already in use by another pool
            var (isMountPathUsed, usedByMdDeviceName) = await IsMountPathInUseAsync(mountPath);
            if (isMountPathUsed && usedByMdDeviceName != mdDeviceName)
            {
                return (false, $"Mount path '{mountPath}' is already in use by pool '{usedByMdDeviceName}'", outputs);
            }
            
            // First try to scan for MD devices
            var scanResult = await _commandService.ExecuteCommandAsync("mdadm --scan", true);
            outputs.Add($"$ {scanResult.Command}");
            outputs.Add(CleanCommandOutput(scanResult.Output));
            
            // Assemble the array if needed
            var assembleResult = await _commandService.ExecuteCommandAsync($"mdadm --assemble /dev/{mdDeviceName}", true);
            outputs.Add($"$ {assembleResult.Command}");
            outputs.Add(CleanCommandOutput(assembleResult.Output));
            
            // Even if assembly reports failure (e.g., already running), continue with mount attempt
            
            // Create mount directory
            var mkdirResult = await _commandService.ExecuteCommandAsync($"mkdir -p {mountPath}");
            outputs.Add($"$ {mkdirResult.Command}");
            outputs.Add(CleanCommandOutput(mkdirResult.Output));

            if (!mkdirResult.Success)
            {
                return (false, $"Failed to create mount directory: {CleanCommandOutput(mkdirResult.Output)}", outputs);
            }
            
            // Mount the filesystem
            var mountResult = await _commandService.ExecuteCommandAsync($"mount /dev/{mdDeviceName} {mountPath}", true);
            outputs.Add($"$ {mountResult.Command}");
            outputs.Add(CleanCommandOutput(mountResult.Output));

            if (!mountResult.Success)
            {
                string errorMessage = "";
                if (mountResult.Error != null) 
                {
                    errorMessage = mountResult.Error.ToString();
                }
                else if (mountResult.Output != null)
                {
                    errorMessage = mountResult.Output.Trim();
                }
                
                _logger.LogWarning("Failed to mount filesystem: {Error}", errorMessage);
                return (false, $"Failed to mount filesystem: {errorMessage}", outputs);
            }
            
            // Update mount path in metadata if successful
            var metadata = await GetPoolMetadataByMdDeviceAsync(mdDeviceName);
            if (metadata != null)
            {
                metadata.LastMountPath = mountPath;
                metadata.IsMounted = true; // Set the mounted flag
                await SavePoolMetadataAsync(metadata);
            }
            
            // Trigger a refresh of the drive cache to reflect the changes
            await _driveService.RefreshDrivesAsync();
            
            return (true, $"Pool '{mdDeviceName}' mounted successfully at '{mountPath}'", outputs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error mounting pool {MdDeviceName}", mdDeviceName);
            outputs.Add($"Error: {ex.Message}");
            return (false, $"Error mounting pool: {ex.Message}", outputs);
        }
    }

    public async Task<(bool Success, string Message, List<string> Outputs)> MountPoolByGuidAsync(
        Guid poolGroupGuid, string mountPath)
    {
        var result = await MountPoolByGuidAsyncImpl(poolGroupGuid, mountPath);
        await _driveService.RefreshDrivesAsync();
        return result;
    }

    // Helper method to implement MountPoolByGuidAsync without the drive refresh
    private async Task<(bool Success, string Message, List<string> Outputs)> MountPoolByGuidAsyncImpl(
        Guid poolGroupGuid, string mountPath)
    {
        var outputs = new List<string>();
        
        try
        {
            // Get metadata for this pool group
            var metadata = await GetPoolMetadataByGuidAsync(poolGroupGuid);
            if (metadata == null)
            {
                return (false, $"No pool found with GUID '{poolGroupGuid}'", outputs);
            }
            
            // Check if the original mdadm device still exists
            var checkResult = await _commandService.ExecuteCommandAsync($"mdadm --detail /dev/{metadata.MdDeviceName}");
            
            if (!checkResult.Success)
            {
                _logger.LogInformation("Original mdadm device {DeviceName} is not available, finding a new device ID", metadata.MdDeviceName);
                
                // Find an available md device ID
                var mdstatResult = await _commandService.ExecuteCommandAsync("cat /proc/mdstat");
                int poolId = GetNextAvailableMdDeviceId(mdstatResult.Output != null ? mdstatResult.Output : string.Empty);
                string newPoolDevice = $"md{poolId}";
                
                _logger.LogInformation("Using new mdadm device: {NewDeviceName}", newPoolDevice);
                outputs.Add($"Info: Original mdadm device {metadata.MdDeviceName} not available. Using new device {newPoolDevice}.");
                
                // Update metadata with the new device name
                metadata.MdDeviceName = newPoolDevice;
                var saveResult = await SavePoolMetadataAsync(metadata);
                if (!saveResult.Success)
                {
                    _logger.LogWarning("Failed to update pool metadata: {Message}", saveResult.Message);
                    outputs.Add($"Warning: Failed to update pool metadata: {saveResult.Message}");
                    // Continue despite metadata save failure
                }
                
                // First try to scan for MD devices
                var scanResult = await _commandService.ExecuteCommandAsync("mdadm --scan", true);
                outputs.Add($"$ {scanResult.Command}");
                outputs.Add(CleanCommandOutput(scanResult.Output));
                
                // Construct the mdadm assemble command with drive serials
                string assembleCommand = $"mdadm --assemble /dev/{newPoolDevice}";
                
                // Try to get device paths for each drive in the metadata
                var drivesResult = await _driveService.GetDrivesAsync();
                var availableDrives = drivesResult?.Blockdevices?.Where(d => 
                    d.Type == "disk" && 
                    !string.IsNullOrEmpty(d.Serial) &&
                    metadata.DriveSerials.Contains(d.Serial)).ToList();
                
                if (availableDrives != null && availableDrives.Any())
                {
                    // Construct proper device paths for each drive
                    var devicePaths = availableDrives.Select(d => {
                        if (!string.IsNullOrEmpty(d.IdLink) && d.IdLink.StartsWith("/dev/"))
                            return d.IdLink; // Already a full path
                        else if (!string.IsNullOrEmpty(d.IdLink))
                            return $"/dev/disk/by-id/{d.IdLink}"; // Construct proper path for IdLink
                        else if (!string.IsNullOrEmpty(d.Path))
                            return d.Path; // Use path as fallback
                        else
                            return $"/dev/{d.Name}"; // Use name as final fallback
                    });
                    
                    assembleCommand += " " + String.Join(" ", devicePaths);
                }
                
                // Execute the assemble command
                var assembleResult = await _commandService.ExecuteCommandAsync(assembleCommand, true);
                outputs.Add($"$ {assembleResult.Command}");
                outputs.Add(CleanCommandOutput(assembleResult.Output));
                
                if (!assembleResult.Success)
                {
                    string errorMessage = "";
                    if (assembleResult.Error != null) 
                    {
                        errorMessage = assembleResult.Error.ToString();
                    }
                    else if (assembleResult.Output != null)
                    {
                        errorMessage = assembleResult.Output.Trim();
                    }
                    
                    _logger.LogWarning("Failed to assemble RAID array: {Error}", errorMessage);
                    return (false, $"Failed to assemble RAID array: {errorMessage}", outputs);
                }
                
                // Create mount directory
                var mkdirResult = await _commandService.ExecuteCommandAsync($"mkdir -p {mountPath}");
                outputs.Add($"$ {mkdirResult.Command}");
                outputs.Add(CleanCommandOutput(mkdirResult.Output));
                
                if (!mkdirResult.Success)
                {
                    return (false, $"Failed to create mount directory: {CleanCommandOutput(mkdirResult.Output)}", outputs);
                }
                
                // Mount the filesystem
                var mountResult = await _commandService.ExecuteCommandAsync($"mount /dev/{newPoolDevice} {mountPath}", true);
                outputs.Add($"$ {mountResult.Command}");
                outputs.Add(CleanCommandOutput(mountResult.Output));
                
                if (!mountResult.Success)
                {
                    string errorMessage = "";
                    if (mountResult.Error != null) 
                    {
                        errorMessage = mountResult.Error.ToString();
                    }
                    else if (mountResult.Output != null)
                    {
                        errorMessage = mountResult.Output.Trim();
                    }
                    
                    _logger.LogWarning("Failed to mount filesystem: {Error}", errorMessage);
                    return (false, $"Failed to mount filesystem: {errorMessage}", outputs);
                }
                
                // Update the mount path in metadata
                metadata.LastMountPath = mountPath;
                await SavePoolMetadataAsync(metadata);
                
                return (true, $"Pool mounted successfully at '{mountPath}' using new device /dev/{newPoolDevice}", outputs);
            }
            else
            {
                // The original mdadm device exists, use the existing mount method
                return await MountPoolByMdDeviceAsync(metadata.MdDeviceName, mountPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error mounting pool by GUID {PoolGroupGuid}", poolGroupGuid);
            outputs.Add($"Error: {ex.Message}");
            return (false, $"Error mounting pool: {ex.Message}", outputs);
        }
    }

    public async Task<(bool Success, string Message, List<string> Outputs)> UnmountPoolByMdDeviceAsync(string mdDeviceName)
    {
        var outputs = new List<string>();
        
        try
        {
            // Find mount point
            var mountResult = await _commandService.ExecuteCommandAsync($"mount | grep '/dev/{mdDeviceName}'");
            string mountPoint = string.Empty;
            
            if (mountResult.Success)
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    CleanCommandOutput(mountResult.Output ?? string.Empty), $@"/dev/{mdDeviceName} on (.*?) type");
                
                if (match.Success)
                {
                    mountPoint = match.Groups[1].Value;
                }
                else
                {
                    return (false, $"Pool '{mdDeviceName}' does not appear to be mounted", outputs);
                }
            }
            else
            {
                return (false, $"Pool '{mdDeviceName}' is not mounted", outputs);
            }
            
            // Check for processes using the mount point
            var processes = await _driveService.GetProcessesUsingMountPointAsync(mountPoint);
            if (processes.Any())
            {
                string processList = string.Join(", ", processes.Select(p => $"{p.Command}({p.PID})"));
                return (false, $"Cannot unmount: processes using mount point: {processList}", outputs);
            }
            
            // Unmount the filesystem
            var unmountResult = await _commandService.ExecuteCommandAsync($"umount {mountPoint}", true);
            outputs.Add($"$ {unmountResult.Command}");
            outputs.Add(CleanCommandOutput(unmountResult.Output));

            if (!unmountResult.Success)
            {
                return (false, $"Failed to unmount filesystem: {CleanCommandOutput(unmountResult.Output)}", outputs);
            }
            
            // Update the metadata to set isMounted=false
            var metadata = await GetPoolMetadataByMdDeviceAsync(mdDeviceName);
            if (metadata != null)
            {
                metadata.IsMounted = false;
                await SavePoolMetadataAsync(metadata);
            }
            
            // Stop the array
            var stopResult = await _commandService.ExecuteCommandAsync($"mdadm --stop /dev/{mdDeviceName}", true);
            outputs.Add($"$ {stopResult.Command}");
            outputs.Add(CleanCommandOutput(stopResult.Output));

            if (!stopResult.Success)
            {
                return (false, $"Failed to stop RAID array: {CleanCommandOutput(stopResult.Output)}", outputs);
            }
            
            // Trigger a refresh of the drive cache to reflect the changes
            await _driveService.RefreshDrivesAsync();
            
            return (true, $"Pool '{mdDeviceName}' unmounted successfully", outputs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unmounting pool {MdDeviceName}", mdDeviceName);
            outputs.Add($"Error: {ex.Message}");
            return (false, $"Error unmounting pool: {ex.Message}", outputs);
        }
    }

    public async Task<(bool Success, string Message, List<string> Outputs)> UnmountPoolByGuidAsync(Guid poolGroupGuid)
    {
        try
        {
            // Resolve the mdDeviceName from the GUID
            var mdDeviceName = await ResolveMdDeviceAsync(poolGroupGuid);
            if (string.IsNullOrEmpty(mdDeviceName))
            {
                return (false, $"No pool found with GUID '{poolGroupGuid}'", new List<string>());
            }
            
            // Use the existing unmount method
            var result = await UnmountPoolByMdDeviceAsync(mdDeviceName);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unmounting pool by GUID {PoolGroupGuid}", poolGroupGuid);
            return (false, $"Error unmounting pool: {ex.Message}", new List<string>());
        }
    }

    public async Task<(bool Success, string Message, List<string> Outputs)> RemovePoolByMdDeviceAsync(string mdDeviceName)
    {
        var outputs = new List<string>();
        
        try
        {
            // First try to unmount if needed
            var unmountResult = await UnmountPoolByMdDeviceAsync(mdDeviceName);
            outputs.AddRange(unmountResult.Outputs);
            
            if (!unmountResult.Success)
            {
                // If the failure is because it's not mounted, that's okay, continue
                if (!unmountResult.Message.Contains("not mounted"))
                {
                    return (false, unmountResult.Message, outputs);
                }
            }
            
            // Get information about drives in the pool
            var mdadmDetail = await _commandService.ExecuteCommandAsync($"mdadm --detail /dev/{mdDeviceName}", true);
            
            // Clean up drives by wiping filesystem signatures
            if (mdadmDetail.Success)
            {
                var lines = CleanCommandOutput(mdadmDetail.Output ?? string.Empty).Split('\n');
                foreach (var line in lines)
                {
                    if (line.Trim().Contains("/dev/") && !line.Contains("md"))
                    {
                        var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4)
                        {
                            string devicePath = parts[parts.Length - 1];
                            
                            // Wipe filesystem signatures
                            var wipeResult = await _commandService.ExecuteCommandAsync($"wipefs -a {devicePath}", true);
                            outputs.Add($"$ {wipeResult.Command}");
                            outputs.Add(CleanCommandOutput(wipeResult.Output));
                            
                            if (!wipeResult.Success)
                            {
                                _logger.LogWarning("Failed to wipe signatures on {DevicePath}: {Error}", 
                                    devicePath, CleanCommandOutput(wipeResult.Output));
                                // Continue despite failure, as removing an individual drive is not critical
                            }
                        }
                    }
                }
            }
            
            // Remove the RAID device
            var removeResult = await _commandService.ExecuteCommandAsync($"mdadm --remove /dev/{mdDeviceName}", true);
            outputs.Add($"$ {removeResult.Command}");
            outputs.Add(CleanCommandOutput(removeResult.Output));
            
            // Trigger a refresh of the drive cache to reflect the changes
            // Note: The unmount operation above should have already triggered a refresh, but we do it again to be safe
            await _driveService.RefreshDrivesAsync();
            
            // Even if the remove command fails (the device might already be gone), consider the operation successful
            
            return (true, $"Pool '{mdDeviceName}' removed successfully", outputs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing pool {MdDeviceName}", mdDeviceName);
            outputs.Add($"Error: {ex.Message}");
            return (false, $"Error removing pool: {ex.Message}", outputs);
        }
    }

    public async Task<(bool Success, string Message, List<string> Outputs)> RemovePoolByGuidAsync(Guid poolGroupGuid)
    {
        try
        {
            // Resolve the mdDeviceName from the GUID
            var mdDeviceName = await ResolveMdDeviceAsync(poolGroupGuid);
            if (string.IsNullOrEmpty(mdDeviceName))
            {
                return (false, $"No pool found with GUID '{poolGroupGuid}'", new List<string>());
            }
            
            // Use the existing remove method
            var result = await RemovePoolByMdDeviceAsync(mdDeviceName);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing pool by GUID {PoolGroupGuid}", poolGroupGuid);
            return (false, $"Error removing pool: {ex.Message}", new List<string>());
        }
    }

    public async Task<(bool Success, string Message)> SavePoolMetadataAsync(PoolMetadata metadata)
    {
        try
        {
            // Ensure metadata directory exists
            var metadataDir = Path.GetDirectoryName(METADATA_FILE_PATH);
            if (!string.IsNullOrEmpty(metadataDir) && !Directory.Exists(metadataDir))
            {
                Directory.CreateDirectory(metadataDir);
            }
            
            // Load existing metadata
            var allMetadata = await LoadMetadataAsync();
            
            // Remove existing entry for this pool, if any
            allMetadata.Pools.RemoveAll(p => 
                p.MdDeviceName == metadata.MdDeviceName ||
                (metadata.PoolGroupGuid != Guid.Empty && p.PoolGroupGuid == metadata.PoolGroupGuid));
            
            // Add new metadata
            allMetadata.Pools.Add(metadata);
            allMetadata.LastUpdated = DateTime.UtcNow;
            
            // Save to file
            string json = System.Text.Json.JsonSerializer.Serialize(allMetadata, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            await File.WriteAllTextAsync(METADATA_FILE_PATH, json);
            
            return (true, "Metadata saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving pool metadata");
            return (false, $"Error saving metadata: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> RemovePoolMetadataAsync(PoolMetadataRemovalRequest request)
    {
        try
        {
            if (!File.Exists(METADATA_FILE_PATH))
            {
                return (true, "No metadata file exists");
            }
            
            // Load existing metadata
            var allMetadata = await LoadMetadataAsync();
            
            if (request.RemoveAll)
            {
                // Remove all metadata
                allMetadata.Pools.Clear();
                allMetadata.LastUpdated = DateTime.UtcNow;
            }
            else
            {
                // Remove specific metadata based on poolGroupGuid
                bool removed = false;
                
                if (request.PoolGroupGuid.HasValue && request.PoolGroupGuid.Value != Guid.Empty)
                {
                    removed |= allMetadata.Pools.RemoveAll(p => p.PoolGroupGuid == request.PoolGroupGuid.Value) > 0;
                }
                
                if (!removed)
                {
                    return (false, "No matching metadata found to remove");
                }
                
                allMetadata.LastUpdated = DateTime.UtcNow;
            }
            
            // Save updated metadata
            string json = System.Text.Json.JsonSerializer.Serialize(allMetadata, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            await File.WriteAllTextAsync(METADATA_FILE_PATH, json);
            
            return (true, "Metadata removed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing pool metadata");
            return (false, $"Error removing metadata: {ex.Message}");
        }
    }

    public async Task<PoolMetadata?> GetPoolMetadataByMdDeviceAsync(string mdDeviceName)
    {
        try
        {
            if (!File.Exists(METADATA_FILE_PATH))
            {
                return null;
            }
            
            var allMetadata = await LoadMetadataAsync();
            return allMetadata.Pools.FirstOrDefault(p => p.MdDeviceName == mdDeviceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pool metadata by device name");
            return null;
        }
    }

    public async Task<PoolMetadata?> GetPoolMetadataByGuidAsync(Guid poolGroupGuid)
    {
        try
        {
            if (!File.Exists(METADATA_FILE_PATH))
            {
                return null;
            }
            
            var allMetadata = await LoadMetadataAsync();
            return allMetadata.Pools.FirstOrDefault(p => p.PoolGroupGuid == poolGroupGuid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pool metadata by GUID");
            return null;
        }
    }

    public async Task<string?> ResolveMdDeviceAsync(Guid? poolGroupGuid)
    {
        try
        {
            // Find by GUID
            if (poolGroupGuid.HasValue && poolGroupGuid.Value != Guid.Empty)
            {
                var metadata = await GetPoolMetadataByGuidAsync(poolGroupGuid.Value);
                if (metadata != null)
                {
                    // Verify that the pool still exists
                    var checkResult = await _commandService.ExecuteCommandAsync($"mdadm --detail /dev/{metadata.MdDeviceName}");
                    if (checkResult.Success)
                    {
                        return metadata.MdDeviceName;
                    }
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving pool device name");
            return null;
        }
    }

    private async Task<(bool IsMountPathInUse, string? UsedByMdDeviceName)> IsMountPathInUseAsync(string mountPath)
    {
        try
        {
            // Get all pools
            var pools = await GetAllPoolsAsync();
            
            // Check if any pool is using this mount path
            var poolUsingPath = pools.FirstOrDefault(p => p.MountPath.Equals(mountPath, StringComparison.OrdinalIgnoreCase) && p.IsMounted);
            
            if (poolUsingPath != null)
            {
                return (true, poolUsingPath.MdDeviceName);
            }
            
            return (false, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if mount path is in use");
            return (false, null); // On error, we'll proceed with mount attempt
        }
    }

    public async Task<PoolMetadataCollection> LoadMetadataAsync()
    {
        try
        {
            if (!_fileSystemInfoService.FileExists(METADATA_FILE_PATH))
            {
                _logger.LogInformation("Pool metadata file not found at {FilePath}, creating a new one", METADATA_FILE_PATH);
                
                // Ensure metadata directory exists
                var metadataDir = Path.GetDirectoryName(METADATA_FILE_PATH);
                if (!string.IsNullOrEmpty(metadataDir) && !_fileSystemInfoService.DirectoryExists(metadataDir))
                {
                    Directory.CreateDirectory(metadataDir);
                    _logger.LogInformation("Created metadata directory at {DirectoryPath}", metadataDir);
                }
                
                // Create a new empty metadata collection
                var newMetadata = new PoolMetadataCollection
                {
                    LastUpdated = DateTime.UtcNow
                };
                
                // Save it to file
                string json = System.Text.Json.JsonSerializer.Serialize(newMetadata, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                await File.WriteAllTextAsync(METADATA_FILE_PATH, json);
                _logger.LogInformation("Created a new empty pool metadata file at {FilePath}", METADATA_FILE_PATH);
                
                return newMetadata;
            }
            
            try 
            {
                // Use FileSystemInfoService to read the file
                string existingJson = await _fileSystemInfoService.ReadFileAsync(METADATA_FILE_PATH);
                
                // Try to deserialize
                var metadata = System.Text.Json.JsonSerializer.Deserialize<PoolMetadataCollection>(existingJson);
                
                if (metadata != null)
                {
                    return metadata;
                }
                
                _logger.LogWarning("Deserialized pool metadata was null at {FilePath}, creating a new one", METADATA_FILE_PATH);
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogError(ex, "Error deserializing pool metadata file at {FilePath}, the file is corrupt. Creating a new file.", METADATA_FILE_PATH);
                
                // Make a backup of the corrupt file if possible
                try
                {
                    string backupPath = $"{METADATA_FILE_PATH}.corrupt.{DateTime.UtcNow:yyyyMMdd_HHmmss}";
                    File.Copy(METADATA_FILE_PATH, backupPath);
                    _logger.LogInformation("Created backup of corrupt metadata file at {BackupPath}", backupPath);
                }
                catch (Exception backupEx)
                {
                    _logger.LogWarning(backupEx, "Failed to create backup of corrupt metadata file");
                }
            }
            
            // If we're here, either the file couldn't be deserialized or was null
            // Create a new metadata collection
            var newCollection = new PoolMetadataCollection
            {
                LastUpdated = DateTime.UtcNow
            };
            
            // Save it to file
            string newJson = System.Text.Json.JsonSerializer.Serialize(newCollection, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            await File.WriteAllTextAsync(METADATA_FILE_PATH, newJson);
            _logger.LogInformation("Created a new empty pool metadata file at {FilePath} to replace corrupt/null data", METADATA_FILE_PATH);
            
            return newCollection;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading metadata");
            return new PoolMetadataCollection();
        }
    }

    private int GetNextAvailableMdDeviceId(string mdstatOutput)
    {
        if (string.IsNullOrEmpty(mdstatOutput))
        {
            _logger.LogWarning("Empty mdstat output when getting next available device ID");
            return 0; // Default to md0 if we can't parse mdstat
        }
        
        // Parse mdstat to find used md device IDs
        var usedIds = new HashSet<int>();
        var lines = CleanCommandOutput(mdstatOutput).Split('\n');
        
        foreach (var line in lines)
        {
            if (line.StartsWith("md"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(line, @"md(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int id))
                {
                    usedIds.Add(id);
                }
            }
        }
        
        // Find the lowest unused ID
        int nextId = 0;
        while (usedIds.Contains(nextId)) // Fixed "contains" to "Contains" (C# is case-sensitive)
        {
            nextId++;
        }
        
        return nextId;
    }

    // Utility method to clean command outputs by removing control characters and symbols
    private string CleanCommandOutput(string? output)
    {
        if (output == null)
            return string.Empty;

        // Remove progress indicators and backspace characters
        var result = System.Text.RegularExpressions.Regex.Replace(output, @"\d+/\d+\b\b\b\b\b     \b\b\b\b\b", "");
        
        // Remove other control sequences
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\b+", "");
        
        // Normalize line endings
        result = result.Replace("\r\n", "\n").Replace("\r", "\n");
        
        // Remove trailing whitespace from each line
        var lines = result.Split('\n')
            .Select(line => line.TrimEnd())
            .ToArray();
        
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Validates and updates pool metadata by checking if the MdDeviceName matches the actual mdadm device
    /// containing the drives with the specified serial numbers.
    /// </summary>
    /// <returns>A tuple with success status, message, and count of corrected entries</returns>
    public async Task<(bool Success, string Message, int FixedEntries)> ValidateAndUpdatePoolMetadataAsync()
    {
        try
        {
            // Load the metadata - this will create a new file if it doesn't exist
            var allMetadata = await LoadMetadataAsync();
            
            // If this is a new empty file, just return success
            if (allMetadata.Pools.Count == 0)
            {
                _logger.LogInformation("No pools found in metadata to validate");
                return (true, "No pools found in metadata to validate", 0);
            }
            
            // Get current active drives
            var drivesResult = await _driveService.GetDrivesAsync();
            if (drivesResult?.Blockdevices == null)
            {
                _logger.LogWarning("Failed to get active drives for metadata validation");
                return (false, "Failed to get active drives for metadata validation", 0);
            }
            
            // Get the current mdadm arrays
            var mdstatResult = await _commandService.ExecuteCommandAsync("cat /proc/mdstat");
            if (!mdstatResult.Success)
            {
                _logger.LogWarning("Failed to read mdstat for metadata validation");
                return (false, "Failed to read mdstat", 0);
            }
            
            // Build a map of actual md devices and their component drives
            var mdDeviceMap = await BuildMdDeviceMapAsync(CleanCommandOutput(mdstatResult.Output ?? string.Empty), drivesResult.Blockdevices);
            
            // Count of entries that needed fixing
            int fixedEntries = 0;
            
            // Check each pool in metadata and update if needed
            foreach (var poolMetadata in allMetadata.Pools)
            {
                // Skip if the pool has no drives
                if (poolMetadata.DriveSerials == null || !poolMetadata.DriveSerials.Any())
                {
                    continue;
                }
                
                // Find the md device containing these drives
                string? actualMdDevice = null;
                foreach (var entry in mdDeviceMap)
                {
                    // Check if the set of drives in this md device matches the set in metadata
                    // We consider it a match if all drives from metadata are in this md device
                    bool allDrivesMatched = true;
                    foreach (var serial in poolMetadata.DriveSerials)
                    {
                        if (!entry.Value.Contains(serial))
                        {
                            allDrivesMatched = false;
                            break;
                        }
                    }
                    
                    if (allDrivesMatched)
                    {
                        actualMdDevice = entry.Key;
                        break;
                    }
                }
                
                // If we found a real md device that contains these drives
                if (!string.IsNullOrEmpty(actualMdDevice))
                {
                    // And if it doesn't match what's in metadata
                    if (poolMetadata.MdDeviceName != actualMdDevice)
                    {
                        _logger.LogInformation(
                            "Pool metadata mismatch detected: Metadata shows {OldDevice} but system has {ActualDevice} for pool {PoolLabel} (GUID: {PoolGuid})",
                            poolMetadata.MdDeviceName, actualMdDevice, poolMetadata.Label, poolMetadata.PoolGroupGuid);
                            
                        // Update the metadata
                        poolMetadata.MdDeviceName = actualMdDevice;
                        fixedEntries++;
                    }
                }
                else
                {
                    // The pool exists in metadata but not on the system
                    _logger.LogInformation(
                        "Pool in metadata not found on system: {MdDevice} for pool {PoolLabel} (GUID: {PoolGuid})",
                        poolMetadata.MdDeviceName, poolMetadata.Label, poolMetadata.PoolGroupGuid);
                    
                    // We don't remove it - it might be temporarily unavailable
                }
            }
            
            // If any entries were fixed, save the updated metadata
            if (fixedEntries > 0)
            {
                allMetadata.LastUpdated = DateTime.UtcNow;
                string json = System.Text.Json.JsonSerializer.Serialize(allMetadata, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                await File.WriteAllTextAsync(METADATA_FILE_PATH, json);
                _logger.LogInformation("Updated pool metadata with {FixedEntries} corrected entries", fixedEntries);
            }
            
            return (true, $"Pool metadata validation complete. Fixed {fixedEntries} entries.", fixedEntries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating and updating pool metadata");
            return (false, $"Error validating pool metadata: {ex.Message}", 0);
        }
    }
    
    /// <summary>
    /// Builds a mapping of md device names to sets of drive serials that they contain
    /// </summary>
    private async Task<Dictionary<string, HashSet<string>>> BuildMdDeviceMapAsync(string mdstatOutput, List<BlockDevice> blockDevices)
    {
        var result = new Dictionary<string, HashSet<string>>();
        
        try
        {
            // Get all MD arrays from MdStatReader
            var mdStatInfo = await _mdStatReader.GetMdStatInfoAsync();
            
            foreach (var (deviceName, arrayInfo) in mdStatInfo.Arrays)
            {
                result[deviceName] = new HashSet<string>();
                
                foreach (var devicePath in arrayInfo.Devices)
                {
                    // Find the corresponding block device
                    var device = blockDevices.FirstOrDefault(d => d.Name == devicePath);
                    if (device != null && !string.IsNullOrEmpty(device.Serial))
                    {
                        result[deviceName].Add(device.Serial);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building MD device map");
        }
        
        return result;
    }
}