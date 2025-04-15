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
}

public class DriveService : IDriveService
{
    private readonly ISystemCommandService _commandService;
    private readonly ILogger<DriveService> _logger;
    private readonly AgentSettings _settings;
    private readonly IFileSystemInfoService _fileSystemInfoService;
    private readonly IDriveInfoService _driveInfoService;
    private readonly IMdStatReader _mdStatReader;

    public DriveService(
        ISystemCommandService commandService,
        ILogger<DriveService> logger,
        IConfiguration configuration,
        IFileSystemInfoService fileSystemInfoService,
        IDriveInfoService driveInfoService,
        IMdStatReader mdStatReader)
    {
        _commandService = commandService;
        _logger = logger;
        _fileSystemInfoService = fileSystemInfoService;
        _driveInfoService = driveInfoService;
        _mdStatReader = mdStatReader;
        
        // Bind configuration to AgentSettings
        _settings = new AgentSettings();
        configuration.GetSection("AgentSettings").Bind(_settings);
    }

    public async Task<LsblkOutput> GetDrivesAsync()
    {
        try
        {
            // We'll still use lsblk for JSON output as it provides the most complete drive information
            var result = await _commandService.ExecuteCommandAsync(
                "lsblk -J -b -o NAME,SIZE,TYPE,MOUNTPOINT,UUID,SERIAL,VENDOR,MODEL,FSTYPE,PATH,ID-LINK");
            
            if (!result.Success)
            {
                _logger.LogError("Failed to list block devices: {Error}", result.Output);
                return new LsblkOutput { Blockdevices = new List<BlockDevice>() };
            }
            
            var lsblkOutput = JsonSerializer.Deserialize<LsblkOutput>(result.Output);
            
            // Filter to include only disk types and exclude excluded drives
            if (lsblkOutput?.Blockdevices != null)
            {
                // Filter to include only type "disk" and exclude specified drives
                lsblkOutput.Blockdevices = lsblkOutput.Blockdevices
                    .Where(device => 
                        device.Type == "disk" && // Only include disk types
                        !_settings.ExcludedDrives.Any(excluded => 
                            device.Path != null && device.Path.Equals(excluded, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }
            
            return lsblkOutput ?? new LsblkOutput { Blockdevices = new List<BlockDevice>() };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while getting drives information");
            return new LsblkOutput { Blockdevices = new List<BlockDevice>() };
        }
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
}