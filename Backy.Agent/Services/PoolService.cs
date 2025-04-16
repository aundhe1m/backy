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
                        CleanCommandOutput(mountResult.Output), $@"/dev/{deviceName} on (.*?) type");
                    
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
                    CleanCommandOutput(mountResult.Output), $@"/dev/{mdDeviceName} on (.*?) type");
                
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
                    string serial = lsblk.Success ? CleanCommandOutput(lsblk.Output).Trim() : "unknown";
                    
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
                var lines = CleanCommandOutput(mdadmDetail.Output).Split('\n');
                
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
                            string serial = lsblk.Success ? CleanCommandOutput(lsblk.Output).Trim() : "unknown";
                            
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
            int poolId = GetNextAvailableMdDeviceId(CleanCommandOutput(mdstatResult.Output));
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
                PoolGroupGuid = poolGroupGuid ?? Guid.NewGuid()
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
                return (false, $"Failed to mount filesystem: {CleanCommandOutput(mountResult.Output)}", outputs);
            }
            
            // Update mount path in metadata if successful
            var metadata = await GetPoolMetadataByMdDeviceAsync(mdDeviceName);
            if (metadata != null && metadata.LastMountPath != mountPath)
            {
                metadata.LastMountPath = mountPath;
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
                int poolId = GetNextAvailableMdDeviceId(CleanCommandOutput(mdstatResult.Output));
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
                var availableDrives = drivesResult.Blockdevices?.Where(d => 
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
                    
                    assembleCommand += " " + string.Join(" ", devicePaths);
                }
                
                // Execute the assemble command
                var assembleResult = await _commandService.ExecuteCommandAsync(assembleCommand, true);
                outputs.Add($"$ {assembleResult.Command}");
                outputs.Add(CleanCommandOutput(assembleResult.Output));
                
                if (!assembleResult.Success)
                {
                    return (false, $"Failed to assemble RAID array: {CleanCommandOutput(assembleResult.Output)}", outputs);
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
                    return (false, $"Failed to mount filesystem: {CleanCommandOutput(mountResult.Output)}", outputs);
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
                    CleanCommandOutput(mountResult.Output), $@"/dev/{mdDeviceName} on (.*?) type");
                
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
                var lines = CleanCommandOutput(mdadmDetail.Output).Split('\n');
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

    private async Task<(bool IsMountPathUsed, string? UsedByMdDeviceName)> IsMountPathInUseAsync(string mountPath)
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
            
            // Use FileSystemInfoService to read the file
            string existingJson = await _fileSystemInfoService.ReadFileAsync(METADATA_FILE_PATH);
            var metadata = System.Text.Json.JsonSerializer.Deserialize<PoolMetadataCollection>(existingJson);
            
            return metadata ?? new PoolMetadataCollection();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading metadata");
            return new PoolMetadataCollection();
        }
    }

    private int GetNextAvailableMdDeviceId(string mdstatOutput)
    {
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
        while (usedIds.Contains(nextId))
        {
            nextId++;
        }
        
        return nextId;
    }

    // Utility method to clean command outputs by removing control characters and symbols
    private string CleanCommandOutput(string output)
    {
        if (string.IsNullOrEmpty(output))
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
        
        return String.Join("\n", lines);
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
            var mdDeviceMap = await BuildMdDeviceMapAsync(CleanCommandOutput(mdstatResult.Output), drivesResult.Blockdevices);
            
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