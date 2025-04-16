using System.Text.Json;
using System.Text.RegularExpressions;
using Backy.Agent.Models;

namespace Backy.Agent.Services;

public interface IDriveService
{
    Task<LsblkOutput> GetDrivesAsync();
    Task<DriveStatus> GetDriveStatusAsync(string serial);
    Task<List<ProcessInfo>> GetProcessesUsingMountPointAsync(string mountPoint);
    Task<(bool Success, string Message, List<string> Outputs)> KillProcessesAsync(ProcessesRequest request);
    Task<(long Size, long Used, long Available, string UsePercent)> GetMountPointSizeAsync(string mountPoint);
    Task<string> GetPoolStatusAsync(string mdDeviceName);
    
    /// <summary>
    /// Refreshes the cached drive information
    /// </summary>
    /// <returns>True if refresh was successful</returns>
    Task<bool> RefreshDrivesAsync();
}

public class DriveService : IDriveService
{
    private readonly ISystemCommandService _commandService;
    private readonly ILogger<DriveService> _logger;
    private readonly AgentSettings _settings;
    private readonly IFileSystemInfoService _fileSystemInfoService;
    private readonly IDriveInfoService _driveInfoService;
    private readonly IMdStatReader _mdStatReader;
    private readonly BackgroundDriveMonitoringService _driveMonitoringService;

    public DriveService(
        ISystemCommandService commandService,
        ILogger<DriveService> logger,
        IConfiguration configuration,
        IFileSystemInfoService fileSystemInfoService,
        IDriveInfoService driveInfoService,
        IMdStatReader mdStatReader,
        BackgroundDriveMonitoringService driveMonitoringService)
    {
        _commandService = commandService;
        _logger = logger;
        _fileSystemInfoService = fileSystemInfoService;
        _driveInfoService = driveInfoService;
        _mdStatReader = mdStatReader;
        _driveMonitoringService = driveMonitoringService;
        
        // Bind configuration to AgentSettings
        _settings = new AgentSettings();
        configuration.GetSection("AgentSettings").Bind(_settings);
    }

    public async Task<LsblkOutput> GetDrivesAsync()
    {
        try
        {
            // Get drives from the background service's cache instead of executing the command directly
            var cachedDrives = _driveMonitoringService.GetCachedDrives();
            
            if (cachedDrives.Blockdevices == null)
            {
                _logger.LogDebug("Drive cache is empty");
                await _driveMonitoringService.RefreshDrivesAsync();
                cachedDrives = _driveMonitoringService.GetCachedDrives();
            }
            
            // Apply exclusion filter based on settings
            if (cachedDrives.Blockdevices != null && _settings.ExcludedDrives.Any())
            {
                _logger.LogDebug("Filtering out excluded drives: {ExcludedDrives}", string.Join(", ", _settings.ExcludedDrives));
                
                // Filter out drives that match any of the excluded patterns
                cachedDrives.Blockdevices = cachedDrives.Blockdevices
                    .Where(d => !ExcludeDrive(d))
                    .ToList();
            }
            
            return cachedDrives;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while getting drives information");
            return new LsblkOutput { Blockdevices = new List<BlockDevice>() };
        }
    }

    /// <summary>
    /// Determines whether a drive should be excluded based on the configured exclusion patterns
    /// </summary>
    /// <param name="drive">The drive to check</param>
    /// <returns>True if the drive should be excluded, false otherwise</returns>
    private bool ExcludeDrive(BlockDevice drive)
    {
        if (drive == null || string.IsNullOrEmpty(drive.Name))
            return false;
        
        // Check if drive name (with or without /dev/ prefix) matches any excluded drive
        foreach (var excludedDrive in _settings.ExcludedDrives)
        {
            var normalizedDrivePattern = excludedDrive.TrimEnd('*');
            var normalizedExcludedDrive = excludedDrive.StartsWith("/dev/") ? excludedDrive : $"/dev/{excludedDrive}";
            
            // Check full path match
            if (!string.IsNullOrEmpty(drive.Path) && 
                (drive.Path.Equals(normalizedExcludedDrive, StringComparison.OrdinalIgnoreCase) ||
                 (excludedDrive.EndsWith("*") && drive.Path.StartsWith(normalizedDrivePattern, StringComparison.OrdinalIgnoreCase))))
            {
                return true;
            }
            
            // Check name match (with or without /dev/ prefix)
            var driveName = drive.Name;
            var plainExcludedName = excludedDrive.Replace("/dev/", "");
            
            if (driveName.Equals(plainExcludedName, StringComparison.OrdinalIgnoreCase) ||
                (excludedDrive.EndsWith("*") && driveName.StartsWith(plainExcludedName.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        
        return false;
    }

    public async Task<DriveStatus> GetDriveStatusAsync(string serial)
    {
        var driveStatus = new DriveStatus();
        
        try
        {
            // Get all drives first
            var allDrives = await GetDrivesAsync();
            var drive = allDrives.Blockdevices?.FirstOrDefault(d => 
                !string.IsNullOrEmpty(d.Serial) && 
                d.Serial.Equals(serial, StringComparison.OrdinalIgnoreCase));
                
            if (drive == null)
            {
                driveStatus.Status = "not_found";
                return driveStatus;
            }
            
            // Use MdStatReader to check if drive is part of a RAID array
            var mdStatInfo = await _mdStatReader.GetMdStatInfoAsync();
            
            foreach (var (mdDeviceName, arrayInfo) in mdStatInfo.Arrays)
            {
                // Check if the array contains this drive
                if (arrayInfo.Devices.Contains(drive.Name ?? ""))
                {
                    driveStatus.InPool = true;
                    driveStatus.MdDeviceName = mdDeviceName;
                    driveStatus.Status = "in_raid";
                    
                    // Check mount point using mounted filesystems
                    var mountedFilesystems = await _driveInfoService.GetMountedFilesystemsAsync();
                    var mdMount = mountedFilesystems.FirstOrDefault(m => m.Device.Contains($"/dev/{mdDeviceName}"));
                    
                    if (mdMount != null)
                    {
                        driveStatus.MountPoint = mdMount.MountPoint;
                    }
                    
                    break;
                }
            }
            
            // If not in a pool, check if mounted directly
            if (!driveStatus.InPool && !string.IsNullOrEmpty(drive.Path))
            {
                var mountedFilesystems = await _driveInfoService.GetMountedFilesystemsAsync();
                var driveMount = mountedFilesystems.FirstOrDefault(m => m.Device.Equals(drive.Path, StringComparison.OrdinalIgnoreCase));
                
                if (driveMount != null)
                {
                    driveStatus.MountPoint = driveMount.MountPoint;
                    driveStatus.Status = "mounted";
                }
            }
            
            // Get processes using the drive if it's mounted
            if (!string.IsNullOrEmpty(driveStatus.MountPoint))
            {
                driveStatus.Processes = await GetProcessesUsingMountPointAsync(driveStatus.MountPoint);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting drive status for serial {Serial}", serial);
            driveStatus.Status = "error";
        }
        
        return driveStatus;
    }

    public async Task<List<ProcessInfo>> GetProcessesUsingMountPointAsync(string mountPoint)
    {
        var processes = new List<ProcessInfo>();
        
        try
        {
            // We'll still use the lsof command here as there's no direct file-based alternative
            var result = await _commandService.ExecuteCommandAsync($"lsof +f -- {mountPoint}");
            if (!result.Success)
            {
                return processes; // Return empty list if no processes or command failed
            }
            
            // Parse lsof output
            var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2) // Header + at least one line
            {
                return processes;
            }
            
            // Skip header line
            for (int i = 1; i < lines.Length; i++)
            {
                var columns = Regex.Split(lines[i].Trim(), @"\s+");
                if (columns.Length >= 4) // Command, PID, User, FD at minimum
                {
                    try
                    {
                        var processInfo = new ProcessInfo
                        {
                            Command = columns[0],
                            User = columns[2]
                        };
                        
                        if (int.TryParse(columns[1], out int pid))
                        {
                            processInfo.PID = pid;
                        }
                        
                        // Get filename/path from the last column
                        if (columns.Length >= 9)
                        {
                            processInfo.Path = columns[8];
                        }
                        
                        processes.Add(processInfo);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error parsing lsof output line: {Line}", lines[i]);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting processes using mount point {MountPoint}", mountPoint);
        }
        
        return processes;
    }

    public async Task<(bool Success, string Message, List<string> Outputs)> KillProcessesAsync(ProcessesRequest request)
    {
        var outputs = new List<string>();
        
        try
        {
            if (request.Pids == null || !request.Pids.Any())
            {
                return (false, "No process IDs specified to kill", outputs);
            }
            
            // Kill each process
            foreach (var pid in request.Pids)
            {
                var result = await _commandService.ExecuteCommandAsync($"kill -9 {pid}");
                outputs.Add($"$ kill -9 {pid}");
                outputs.Add($"Process {pid}: {(result.Success ? "Killed" : "Failed")}");
                
                if (!result.Success)
                {
                    return (false, $"Failed to kill process {pid}", outputs);
                }
            }
            
            // Wait a moment for processes to terminate
            await Task.Delay(500);
            
            // Return success
            return (true, $"Successfully killed {request.Pids.Count} processes", outputs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error killing processes");
            outputs.Add($"Error: {ex.Message}");
            return (false, ex.Message, outputs);
        }
    }

    public async Task<(long Size, long Used, long Available, string UsePercent)> GetMountPointSizeAsync(string mountPoint)
    {
        try
        {
            // Use DriveInfoService to get disk space information
            return await _driveInfoService.GetDiskSpaceInfoAsync(mountPoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting mount point size for {MountPoint}", mountPoint);
            return (0, 0, 0, "0%");
        }
    }

    public async Task<string> GetPoolStatusAsync(string mdDeviceName)
    {
        try
        {
            // Use MdStatReader to get array information
            var arrayInfo = await _mdStatReader.GetArrayInfoAsync(mdDeviceName);
            
            if (arrayInfo == null)
            {
                return "Offline";
            }
            
            // Map the array state to a status
            return arrayInfo.State.ToLower() switch
            {
                "clean" => "Active",
                var s when s.Contains("clean, resyncing") => "Resyncing",
                var s when s.Contains("clean, degraded") => "Degraded",
                var s when s.Contains("clean, degraded, recovering") => "Recovering",
                var s when s.Contains("clean, failed") => "Failed",
                _ => arrayInfo.IsActive ? "Active" : "Inactive"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pool status for {MdDeviceName}", mdDeviceName);
            return "Error";
        }
    }
    
    /// <summary>
    /// Refreshes the cached drive information
    /// </summary>
    /// <returns>True if refresh was successful</returns>
    public async Task<bool> RefreshDrivesAsync()
    {
        _logger.LogInformation("Manual refresh of drives information requested");
        return await _driveMonitoringService.RefreshDrivesAsync();
    }
}