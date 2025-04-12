using Backy.Agent.Models;

namespace Backy.Agent.Services;

public interface IPoolService
{
    Task<List<PoolListItem>> GetAllPoolsAsync();
    Task<(bool Success, string Message, PoolDetailResponse PoolDetail)> GetPoolDetailAsync(string poolId);
    Task<(bool Success, string Message, PoolDetailResponse PoolDetail)> GetPoolDetailByGuidAsync(Guid poolGroupGuid);
    Task<(bool Success, string Message, string PoolId, string? MountPath, List<string> Outputs)> CreatePoolAsync(PoolCreationRequest request);
    Task<(bool Success, string Message, List<string> Outputs)> MountPoolAsync(string poolId, string mountPath);
    Task<(bool Success, string Message, List<string> Outputs)> MountPoolByGuidAsync(Guid poolGroupGuid, string mountPath);
    Task<(bool Success, string Message, List<string> Outputs)> UnmountPoolAsync(string poolId);
    Task<(bool Success, string Message, List<string> Outputs)> UnmountPoolByGuidAsync(Guid poolGroupGuid);
    Task<(bool Success, string Message, List<string> Outputs)> RemovePoolAsync(string poolId);
    Task<(bool Success, string Message, List<string> Outputs)> RemovePoolByGuidAsync(Guid poolGroupGuid);
    
    // Metadata management methods
    Task<(bool Success, string Message)> SavePoolMetadataAsync(PoolMetadata metadata);
    Task<(bool Success, string Message)> RemovePoolMetadataAsync(PoolMetadataRemovalRequest request);
    Task<PoolMetadata?> GetPoolMetadataByIdAsync(string poolId);
    Task<PoolMetadata?> GetPoolMetadataByGuidAsync(Guid poolGroupGuid);
    Task<string?> ResolvePoolIdAsync(Guid? poolGroupGuid);
    Task<(bool Success, string Message, int FixedEntries)> ValidateAndUpdatePoolMetadataAsync();
}

public class PoolService : IPoolService
{
    private readonly ISystemCommandService _commandService;
    private readonly IDriveService _driveService;
    private readonly ILogger<PoolService> _logger;

    // Metadata file path
    private const string METADATA_FILE_PATH = "/var/lib/backy/pool-metadata.json";

    public PoolService(
        ISystemCommandService commandService,
        IDriveService driveService,
        ILogger<PoolService> logger)
    {
        _commandService = commandService;
        _driveService = driveService;
        _logger = logger;
    }

    public async Task<List<PoolListItem>> GetAllPoolsAsync()
    {
        var result = new List<PoolListItem>();
        
        try
        {
            // Get all available drives on the system for checking connection status
            var drivesResult = await _driveService.GetDrivesAsync();
            var connectedDrives = new HashSet<string>();
            if (drivesResult?.Blockdevices != null)
            {
                foreach (var drive in drivesResult.Blockdevices.Where(d => d.Type == "disk" && !string.IsNullOrEmpty(d.Serial)))
                {
                    connectedDrives.Add(drive.Serial!);
                }
            }
            
            // Get all active md devices from mdstat
            var mdstatResult = await _commandService.ExecuteCommandAsync("cat /proc/mdstat");
            if (!mdstatResult.Success)
            {
                return result;
            }
            
            var lines = CleanCommandOutput(mdstatResult.Output).Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith("md") && !line.StartsWith("mdadm"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"^(md\d+)");
                    if (match.Success)
                    {
                        string mdDevice = match.Groups[1].Value;
                        var poolItem = new PoolListItem
                        {
                            PoolId = mdDevice,
                            Status = await _driveService.GetPoolStatusAsync(mdDevice)
                        };
                        
                        // Check if mounted
                        var mountResult = await _commandService.ExecuteCommandAsync($"mount | grep '/dev/{mdDevice}'");
                        if (mountResult.Success)
                        {
                            var mountMatch = System.Text.RegularExpressions.Regex.Match(
                                CleanCommandOutput(mountResult.Output), $@"/dev/{mdDevice} on (.*?) type");
                            
                            if (mountMatch.Success)
                            {
                                poolItem.MountPath = mountMatch.Groups[1].Value;
                                poolItem.IsMounted = true;
                            }
                        }
                        
                        // Get drive count and drive details
                        var detailResult = await _commandService.ExecuteCommandAsync($"mdadm --detail /dev/{mdDevice}");
                        if (detailResult.Success)
                        {
                            var detailLines = CleanCommandOutput(detailResult.Output).Split('\n');
                            poolItem.DriveCount = detailLines.Count(l => l.Contains("/dev/") && !l.Contains("md"));
                            
                            // Check metadata for this pool
                            var metadata = await GetPoolMetadataByIdAsync(mdDevice);
                            if (metadata != null)
                            {
                                poolItem.PoolGroupGuid = metadata.PoolGroupGuid;
                                poolItem.Label = metadata.Label;
                                
                                // Get drive details from mdadm output
                                foreach (var detailLine in detailLines)
                                {
                                    if (detailLine.Trim().Contains("/dev/") && !detailLine.Contains("md"))
                                    {
                                        var parts = detailLine.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                        if (parts.Length >= 4)
                                        {
                                            string devicePath = parts[parts.Length - 1];
                                            
                                            // Try to get serial number
                                            var deviceName = System.IO.Path.GetFileName(devicePath);
                                            var lsblk = await _commandService.ExecuteCommandAsync($"lsblk -n -o SERIAL /dev/{deviceName}");
                                            string serial = lsblk.Success ? CleanCommandOutput(lsblk.Output).Trim() : "unknown";
                                            
                                            // Get label from metadata if available
                                            string label = $"{mdDevice}-{poolItem.Drives.Count + 1}";
                                            if (metadata.DriveLabels.ContainsKey(serial))
                                            {
                                                label = metadata.DriveLabels[serial];
                                            }
                                            
                                            // Check if drive is connected
                                            bool isConnected = connectedDrives.Contains(serial);
                                            
                                            poolItem.Drives.Add(new PoolDriveSummary
                                            {
                                                Serial = serial,
                                                Label = label,
                                                IsConnected = isConnected
                                            });
                                        }
                                    }
                                }
                            }
                        }
                        
                        result.Add(poolItem);
                    }
                }
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all pools");
            return result;
        }
    }

    public async Task<(bool Success, string Message, PoolDetailResponse PoolDetail)> GetPoolDetailAsync(string poolId)
    {
        try
        {
            // Check if the pool exists
            var mdadmDetail = await _commandService.ExecuteCommandAsync($"mdadm --detail /dev/{poolId}");
            if (!mdadmDetail.Success)
            {
                return (false, $"Pool '{poolId}' not found", new PoolDetailResponse());
            }
            
            var response = new PoolDetailResponse();
            
            // Get pool status (Active, Degraded, etc.)
            response.Status = await _driveService.GetPoolStatusAsync(poolId);
            
            // Find mount point for pool
            var mountResult = await _commandService.ExecuteCommandAsync($"mount | grep '/dev/{poolId}'");
            if (mountResult.Success)
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    CleanCommandOutput(mountResult.Output), $@"/dev/{poolId} on (.*?) type");
                
                if (match.Success)
                {
                    response.MountPath = match.Groups[1].Value;
                    
                    // Get size information if mounted
                    var sizeInfo = await _driveService.GetMountPointSizeAsync(response.MountPath);
                    response.Size = sizeInfo.Size;
                    response.Used = sizeInfo.Used; // This is correct now because the property in PoolDetailResponse is lowercase 'used'
                    response.Available = sizeInfo.Available;
                    response.UsePercent = sizeInfo.UsePercent;
                }
            }
            
            // Extract drives from mdadm output and add to response
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
                        string label = $"{poolId}-{response.Drives.Count + 1}";
                        var metadata = await GetPoolMetadataByIdAsync(poolId);
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
            
            return (true, "Pool details retrieved successfully", response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pool details for {PoolId}", poolId);
            return (false, $"Error retrieving pool details: {ex.Message}", new PoolDetailResponse { Status = "Error" });
        }
    }

    public async Task<(bool Success, string Message, PoolDetailResponse PoolDetail)> GetPoolDetailByGuidAsync(Guid poolGroupGuid)
    {
        try
        {
            // Resolve the poolId from the GUID
            var poolId = await ResolvePoolIdAsync(poolGroupGuid);
            if (string.IsNullOrEmpty(poolId))
            {
                return (false, $"No pool found with GUID '{poolGroupGuid}'", new PoolDetailResponse());
            }
            
            return await GetPoolDetailAsync(poolId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pool details by GUID {PoolGroupGuid}", poolGroupGuid);
            return (false, $"Error retrieving pool details: {ex.Message}", new PoolDetailResponse { Status = "Error" });
        }
    }

    public async Task<(bool Success, string Message, string PoolId, string? MountPath, List<string> Outputs)> CreatePoolAsync(
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
                return (false, "Pool label is required", string.Empty, null, outputs);
            }

            if (request.DriveSerials == null || !request.DriveSerials.Any())
            {
                return (false, "At least one drive is required to create a pool", string.Empty, null, outputs);
            }

            // Validate mount path
            if (string.IsNullOrWhiteSpace(request.MountPath))
            {
                return (false, "Mount path is required", string.Empty, null, outputs);
            }

            if (!request.MountPath.StartsWith("/"))
            {
                return (false, "Mount path must be absolute", string.Empty, null, outputs);
            }

            // Get available drives
            var drivesResult = await _driveService.GetDrivesAsync();
            var availableDrives = drivesResult.Blockdevices?.Where(d => 
                d.Type == "disk" && 
                !string.IsNullOrEmpty(d.Serial) &&
                request.DriveSerials.Contains(d.Serial)).ToList();

            if (availableDrives == null || availableDrives.Count < request.DriveSerials.Count)
            {
                return (false, "One or more requested drives not found", string.Empty, null, outputs);
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
                return (false, $"Failed to create RAID array: {CleanCommandOutput(mdadmResult.Output)}", string.Empty, null, outputs);
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
                return (false, $"Failed to format RAID array: {CleanCommandOutput(mkfsResult.Output)}", poolDevice, null, outputs);
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
                return (false, $"Failed to create mount directory: {CleanCommandOutput(mkdirResult.Output)}", poolDevice, null, outputs);
            }

            // Mount the new filesystem
            var mountResult = await _commandService.ExecuteCommandAsync($"mount /dev/{poolDevice} {mountPath}", true);
            outputs.Add($"$ {mountResult.Command}");
            outputs.Add(CleanCommandOutput(mountResult.Output));

            if (!mountResult.Success)
            {
                // Try to stop the array if mounting fails
                await _commandService.ExecuteCommandAsync($"mdadm --stop /dev/{poolDevice}", true);
                return (false, $"Failed to mount filesystem: {CleanCommandOutput(mountResult.Output)}", poolDevice, null, outputs);
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

            return (true, "Pool created successfully", poolDevice, mountPath, outputs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating pool");
            outputs.Add($"Error: {ex.Message}");
            return (false, $"Error creating pool: {ex.Message}", string.Empty, null, outputs);
        }
    }

    public async Task<(bool Success, string Message, List<string> Outputs)> MountPoolAsync(
        string poolId, string mountPath)
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
            var (isMountPathUsed, usedByPoolId) = await IsMountPathInUseAsync(mountPath);
            if (isMountPathUsed && usedByPoolId != poolId)
            {
                return (false, $"Mount path '{mountPath}' is already in use by pool '{usedByPoolId}'", outputs);
            }
            
            // First try to scan for MD devices
            var scanResult = await _commandService.ExecuteCommandAsync("mdadm --scan", true);
            outputs.Add($"$ {scanResult.Command}");
            outputs.Add(CleanCommandOutput(scanResult.Output));
            
            // Assemble the array if needed
            var assembleResult = await _commandService.ExecuteCommandAsync($"mdadm --assemble /dev/{poolId}", true);
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
            var mountResult = await _commandService.ExecuteCommandAsync($"mount /dev/{poolId} {mountPath}", true);
            outputs.Add($"$ {mountResult.Command}");
            outputs.Add(CleanCommandOutput(mountResult.Output));

            if (!mountResult.Success)
            {
                return (false, $"Failed to mount filesystem: {CleanCommandOutput(mountResult.Output)}", outputs);
            }
            
            // Update mount path in metadata if successful
            var metadata = await GetPoolMetadataByIdAsync(poolId);
            if (metadata != null && metadata.LastMountPath != mountPath)
            {
                metadata.LastMountPath = mountPath;
                await SavePoolMetadataAsync(metadata);
            }
            
            return (true, $"Pool '{poolId}' mounted successfully at '{mountPath}'", outputs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error mounting pool {PoolId}", poolId);
            outputs.Add($"Error: {ex.Message}");
            return (false, $"Error mounting pool: {ex.Message}", outputs);
        }
    }

    public async Task<(bool Success, string Message, List<string> Outputs)> MountPoolByGuidAsync(
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
                return await MountPoolAsync(metadata.MdDeviceName, mountPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error mounting pool by GUID {PoolGroupGuid}", poolGroupGuid);
            outputs.Add($"Error: {ex.Message}");
            return (false, $"Error mounting pool: {ex.Message}", outputs);
        }
    }

    public async Task<(bool Success, string Message, List<string> Outputs)> UnmountPoolAsync(string poolId)
    {
        var outputs = new List<string>();
        
        try
        {
            // Find mount point
            var mountResult = await _commandService.ExecuteCommandAsync($"mount | grep '/dev/{poolId}'");
            string mountPoint = string.Empty;
            
            if (mountResult.Success)
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    CleanCommandOutput(mountResult.Output), $@"/dev/{poolId} on (.*?) type");
                
                if (match.Success)
                {
                    mountPoint = match.Groups[1].Value;
                }
                else
                {
                    return (false, $"Pool '{poolId}' does not appear to be mounted", outputs);
                }
            }
            else
            {
                return (false, $"Pool '{poolId}' is not mounted", outputs);
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
            var stopResult = await _commandService.ExecuteCommandAsync($"mdadm --stop /dev/{poolId}", true);
            outputs.Add($"$ {stopResult.Command}");
            outputs.Add(CleanCommandOutput(stopResult.Output));

            if (!stopResult.Success)
            {
                return (false, $"Failed to stop RAID array: {CleanCommandOutput(stopResult.Output)}", outputs);
            }
            
            return (true, $"Pool '{poolId}' unmounted successfully", outputs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unmounting pool {PoolId}", poolId);
            outputs.Add($"Error: {ex.Message}");
            return (false, $"Error unmounting pool: {ex.Message}", outputs);
        }
    }

    public async Task<(bool Success, string Message, List<string> Outputs)> UnmountPoolByGuidAsync(Guid poolGroupGuid)
    {
        try
        {
            // Resolve the poolId from the GUID
            var poolId = await ResolvePoolIdAsync(poolGroupGuid);
            if (string.IsNullOrEmpty(poolId))
            {
                return (false, $"No pool found with GUID '{poolGroupGuid}'", new List<string>());
            }
            
            // Use the existing unmount method
            return await UnmountPoolAsync(poolId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unmounting pool by GUID {PoolGroupGuid}", poolGroupGuid);
            return (false, $"Error unmounting pool: {ex.Message}", new List<string>());
        }
    }

    public async Task<(bool Success, string Message, List<string> Outputs)> RemovePoolAsync(string poolId)
    {
        var outputs = new List<string>();
        
        try
        {
            // First try to unmount if needed
            var unmountResult = await UnmountPoolAsync(poolId);
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
            var mdadmDetail = await _commandService.ExecuteCommandAsync($"mdadm --detail /dev/{poolId}", true);
            
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
            var removeResult = await _commandService.ExecuteCommandAsync($"mdadm --remove /dev/{poolId}", true);
            outputs.Add($"$ {removeResult.Command}");
            outputs.Add(CleanCommandOutput(removeResult.Output));
            
            // Even if the remove command fails (the device might already be gone), consider the operation successful
            
            return (true, $"Pool '{poolId}' removed successfully", outputs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing pool {PoolId}", poolId);
            outputs.Add($"Error: {ex.Message}");
            return (false, $"Error removing pool: {ex.Message}", outputs);
        }
    }

    public async Task<(bool Success, string Message, List<string> Outputs)> RemovePoolByGuidAsync(Guid poolGroupGuid)
    {
        try
        {
            // Resolve the poolId from the GUID
            var poolId = await ResolvePoolIdAsync(poolGroupGuid);
            if (string.IsNullOrEmpty(poolId))
            {
                return (false, $"No pool found with GUID '{poolGroupGuid}'", new List<string>());
            }
            
            // Use the existing remove method
            return await RemovePoolAsync(poolId);
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
                (metadata.PoolGroupId > 0 && p.PoolGroupId == metadata.PoolGroupId) ||
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
                // Remove specific metadata
                bool removed = false;
                
                if (!string.IsNullOrEmpty(request.PoolId))
                {
                    removed |= allMetadata.Pools.RemoveAll(p => p.MdDeviceName == request.PoolId) > 0;
                }
                
                if (request.PoolGroupId.HasValue && request.PoolGroupId.Value > 0)
                {
                    removed |= allMetadata.Pools.RemoveAll(p => p.PoolGroupId == request.PoolGroupId.Value) > 0;
                }
                
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

    public async Task<PoolMetadata?> GetPoolMetadataByIdAsync(string poolId)
    {
        try
        {
            if (!File.Exists(METADATA_FILE_PATH))
            {
                return null;
            }
            
            var allMetadata = await LoadMetadataAsync();
            return allMetadata.Pools.FirstOrDefault(p => p.MdDeviceName == poolId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pool metadata by ID");
            return null;
        }
    }

    public async Task<PoolMetadata?> GetPoolMetadataByGroupIdAsync(int poolGroupId)
    {
        try
        {
            if (!File.Exists(METADATA_FILE_PATH))
            {
                return null;
            }
            
            var allMetadata = await LoadMetadataAsync();
            return allMetadata.Pools.FirstOrDefault(p => p.PoolGroupId == poolGroupId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pool metadata by group ID");
            return null;
        }
    }

    public async Task<PoolMetadata?> GetPoolMetadataByGroupGuidAsync(Guid poolGroupGuid)
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
            _logger.LogError(ex, "Error getting pool metadata by group GUID");
            return null;
        }
    }

    public async Task<PoolMetadata?> GetPoolMetadataByGuidAsync(Guid poolGroupGuid)
    {
        // Delegate to the existing implementation
        return await GetPoolMetadataByGroupGuidAsync(poolGroupGuid);
    }

    public async Task<string?> ResolvePoolIdAsync(int? poolGroupId, Guid? poolGroupGuid)
    {
        try
        {
            // First try to find by ID
            if (poolGroupId.HasValue && poolGroupId.Value > 0)
            {
                var metadata = await GetPoolMetadataByGroupIdAsync(poolGroupId.Value);
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
            
            // If not found, try by GUID
            if (poolGroupGuid.HasValue && poolGroupGuid.Value != Guid.Empty)
            {
                var metadata = await GetPoolMetadataByGroupGuidAsync(poolGroupGuid.Value);
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
            _logger.LogError(ex, "Error resolving pool ID");
            return null;
        }
    }

    public async Task<string?> ResolvePoolIdAsync(Guid? poolGroupGuid)
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
            _logger.LogError(ex, "Error resolving pool ID");
            return null;
        }
    }

    private async Task<(bool IsMountPathUsed, string? UsedByPoolId)> IsMountPathInUseAsync(string mountPath)
    {
        try
        {
            // Get all pools
            var pools = await GetAllPoolsAsync();
            
            // Check if any pool is using this mount path
            var poolUsingPath = pools.FirstOrDefault(p => p.MountPath.Equals(mountPath, StringComparison.OrdinalIgnoreCase) && p.IsMounted);
            
            if (poolUsingPath != null)
            {
                return (true, poolUsingPath.PoolId);
            }
            
            return (false, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if mount path is in use");
            return (false, null); // On error, we'll proceed with mount attempt
        }
    }

    private async Task<PoolMetadataCollection> LoadMetadataAsync()
    {
        try
        {
            if (!File.Exists(METADATA_FILE_PATH))
            {
                return new PoolMetadataCollection();
            }
            
            string json = await File.ReadAllTextAsync(METADATA_FILE_PATH);
            var metadata = System.Text.Json.JsonSerializer.Deserialize<PoolMetadataCollection>(json);
            
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
            if (!File.Exists(METADATA_FILE_PATH))
            {
                _logger.LogWarning("Pool metadata file not found at {FilePath}", METADATA_FILE_PATH);
                return (false, "Pool metadata file not found", 0);
            }
            
            // Load the existing metadata
            var allMetadata = await LoadMetadataAsync();
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
            // Parse mdstat to find the md devices
            var mdNameRegex = new System.Text.RegularExpressions.Regex(@"^(md\d+)\s*:");
            var deviceRegex = new System.Text.RegularExpressions.Regex(@"\s(\w+)\[\d+\]");
            
            string? currentMdDevice = null;
            var lines = mdstatOutput.Split('\n');
            
            foreach (var line in lines)
            {
                // Check if this line defines an md device
                var mdNameMatch = mdNameRegex.Match(line);
                if (mdNameMatch.Success)
                {
                    currentMdDevice = mdNameMatch.Groups[1].Value;
                    result[currentMdDevice] = new HashSet<string>();
                }
                else if (currentMdDevice != null)
                {
                    // Look for device references like 'sda[0]'
                    var deviceMatches = deviceRegex.Matches(line);
                    foreach (System.Text.RegularExpressions.Match deviceMatch in deviceMatches)
                    {
                        string deviceName = deviceMatch.Groups[1].Value;
                        
                        // Find the corresponding block device
                        var device = blockDevices.FirstOrDefault(d => d.Name == deviceName);
                        if (device != null && !string.IsNullOrEmpty(device.Serial))
                        {
                            // Add the serial to our map
                            result[currentMdDevice].Add(device.Serial);
                        }
                    }
                }
            }
            
            // For each md device, if we don't have any drives yet, try to get them from mdadm --detail
            foreach (var mdDevice in result.Keys.ToList())
            {
                if (result[mdDevice].Count == 0)
                {
                    var detailResult = await _commandService.ExecuteCommandAsync($"mdadm --detail /dev/{mdDevice}");
                    if (detailResult.Success)
                    {
                        var detailLines = CleanCommandOutput(detailResult.Output).Split('\n');
                        foreach (var line in detailLines)
                        {
                            // Look for lines that mention /dev/ but not /dev/md
                            if (line.Contains("/dev/") && !line.Contains("/dev/md"))
                            {
                                var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length >= 4)
                                {
                                    // Get the device path
                                    string devicePath = parts[parts.Length - 1];
                                    string deviceName = System.IO.Path.GetFileName(devicePath);
                                    
                                    // Find the corresponding block device
                                    var device = blockDevices.FirstOrDefault(d => d.Name == deviceName);
                                    if (device != null && !string.IsNullOrEmpty(device.Serial))
                                    {
                                        result[mdDevice].Add(device.Serial);
                                    }
                                    else
                                    {
                                        // Try to use lsblk to get the serial for this specific device
                                        var lsblk = await _commandService.ExecuteCommandAsync($"lsblk -n -o SERIAL /dev/{deviceName}");
                                        if (lsblk.Success)
                                        {
                                            string serial = CleanCommandOutput(lsblk.Output).Trim();
                                            if (!string.IsNullOrEmpty(serial))
                                            {
                                                result[mdDevice].Add(serial);
                                            }
                                        }
                                    }
                                }
                            }
                        }
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