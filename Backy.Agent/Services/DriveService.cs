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
    Task<string> GetPoolStatusAsync(string poolId);
}

public class DriveService : IDriveService
{
    private readonly ISystemCommandService _commandService;
    private readonly ILogger<DriveService> _logger;
    private readonly AgentSettings _settings;

    public DriveService(
        ISystemCommandService commandService,
        ILogger<DriveService> logger,
        IConfiguration configuration)
    {
        _commandService = commandService;
        _logger = logger;
        
        // Bind configuration to AgentSettings
        _settings = new AgentSettings();
        configuration.GetSection("AgentSettings").Bind(_settings);
    }

    public async Task<LsblkOutput> GetDrivesAsync()
    {
        try
        {
            // Use lsblk to find drives with JSON output for consistent parsing
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
            
            // Check if drive is part of a RAID array
            var mdstatResult = await _commandService.ExecuteCommandAsync("cat /proc/mdstat");
            if (mdstatResult.Success)
            {
                // Parse mdstat to find RAID arrays and their components
                var mdstatLines = mdstatResult.Output.Split('\n');
                foreach (var line in mdstatLines)
                {
                    if (line.StartsWith("md") && line.Contains(drive.Name ?? ""))
                    {
                        // Extract md device name
                        var match = Regex.Match(line, @"^(md\d+)");
                        if (match.Success)
                        {
                            string mdDevice = match.Groups[1].Value;
                            driveStatus.InPool = true;
                            driveStatus.PoolId = mdDevice;
                            driveStatus.Status = "in_raid";
                            
                            // Check mount point
                            var mountsResult = await _commandService.ExecuteCommandAsync("mount | grep " + mdDevice);
                            if (mountsResult.Success)
                            {
                                var mountMatch = Regex.Match(mountsResult.Output, $@"/dev/{mdDevice} on (.*?) type");
                                if (mountMatch.Success)
                                {
                                    driveStatus.MountPoint = mountMatch.Groups[1].Value;
                                }
                            }
                            break;
                        }
                    }
                }
            }
            
            // If not in a pool, check if mounted directly
            if (!driveStatus.InPool && !string.IsNullOrEmpty(drive.Path))
            {
                var mountResult = await _commandService.ExecuteCommandAsync($"mount | grep '{drive.Path}'");
                if (mountResult.Success)
                {
                    var mountMatch = Regex.Match(mountResult.Output, $@"{drive.Path} on (.*?) type");
                    if (mountMatch.Success)
                    {
                        driveStatus.MountPoint = mountMatch.Groups[1].Value;
                        driveStatus.Status = "mounted";
                    }
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
            // Execute df command to get filesystem usage information
            var result = await _commandService.ExecuteCommandAsync($"df -PB1 {mountPoint}");
            if (!result.Success)
            {
                _logger.LogWarning("Failed to get mount point size for {MountPoint}: {Error}", 
                    mountPoint, result.Output);
                return (0, 0, 0, "0%");
            }
            
            // Parse df output
            var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2) // Header + at least one line
            {
                _logger.LogWarning("Unexpected df output format for {MountPoint}", mountPoint);
                return (0, 0, 0, "0%");
            }
            
            // The second line contains the data
            var dataLine = lines[1];
            var parts = Regex.Split(dataLine.Trim(), @"\s+");
            if (parts.Length < 6)
            {
                _logger.LogWarning("Unexpected df output format for {MountPoint}", mountPoint);
                return (0, 0, 0, "0%");
            }
            
            // Extract size information
            if (!long.TryParse(parts[1], out long size))
            {
                _logger.LogWarning("Failed to parse size: {Size}", parts[1]);
                size = 0;
            }
            
            if (!long.TryParse(parts[2], out long used))
            {
                _logger.LogWarning("Failed to parse used: {Used}", parts[2]);
                used = 0;
            }
            
            if (!long.TryParse(parts[3], out long available))
            {
                _logger.LogWarning("Failed to parse available: {Available}", parts[3]);
                available = 0;
            }
            
            string usePercent = parts[4];
            
            return (size, used, available, usePercent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting mount point size for {MountPoint}", mountPoint);
            return (0, 0, 0, "0%");
        }
    }

    public async Task<string> GetPoolStatusAsync(string poolId)
    {
        try
        {
            var result = await _commandService.ExecuteCommandAsync($"mdadm --detail /dev/{poolId}");
            if (!result.Success)
            {
                return "Offline";
            }
            
            // Parse the output to get the State line
            var lines = result.Output.Split('\n');
            foreach (var line in lines)
            {
                if (line.Trim().StartsWith("State :"))
                {
                    var state = line.Substring(line.IndexOf(':') + 1).Trim();
                    return MapRaidStateToStatus(state);
                }
            }
            
            return "Unknown";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pool status for {PoolId}", poolId);
            return "Error";
        }
    }
    
    private string MapRaidStateToStatus(string state)
    {
        return state.ToLower() switch
        {
            "clean" => "Active",
            var s when s.Contains("clean, resyncing") => "Resyncing",
            var s when s.Contains("clean, degraded") => "Degraded",
            var s when s.Contains("clean, degraded, recovering") => "Recovering",
            var s when s.Contains("clean, failed") => "Failed",
            _ => "Unknown"
        };
    }
}