using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Backy.Data;
using Backy.Models;
using Microsoft.EntityFrameworkCore;

namespace Backy.Services
{
    public interface IDriveService
    {
        Task<List<Drive>> UpdateActiveDrivesAsync();
        string FetchPoolStatus(int poolGroupId);
        (long Size, long Used, long Available, string UsePercent) GetMountPointSize(string mountPoint);
        Task<(bool Success, string Message)> ProtectDriveAsync(string serial);
        Task<(bool Success, string Message)> UnprotectDriveAsync(string serial);
        Task<(bool Success, string Message, List<string> Outputs)> CreatePoolAsync(CreatePoolRequest request);
        Task<(bool Success, string Message)> UnmountPoolAsync(Guid poolGroupGuid);
        Task<(bool Success, string Message)> RemovePoolGroupAsync(Guid poolGroupGuid);
        Task<(bool Success, string Message)> MountPoolAsync(Guid poolGroupGuid);
        Task<(bool Success, string Message)> RenamePoolGroupAsync(RenamePoolRequest request);
    }

    public class DriveService : IDriveService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<DriveService> _logger;

        public DriveService(ApplicationDbContext context, ILogger<DriveService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<List<Drive>> UpdateActiveDrivesAsync()
        {
            var activeDrives = new List<Drive>();
            try
            {
                // Use lsblk to find drives
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "bash",
                        Arguments = $"-c \"lsblk -J -b -o NAME,SIZE,TYPE,MOUNTPOINT,UUID,SERIAL,VENDOR,MODEL,FSTYPE,PATH,ID-LINK\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    },
                };
                process.Start();
                string jsonOutput = await process.StandardOutput.ReadToEndAsync();
                process.WaitForExit();

                var lsblkOutput = JsonSerializer.Deserialize<LsblkOutput>(jsonOutput);
                if (lsblkOutput?.Blockdevices != null)
                {
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
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating active drives.");
            }

            return activeDrives;
        }

        public string FetchPoolStatus(int poolGroupId)
        {
            string command = $"mdadm --detail /dev/md{poolGroupId}";
            var commandOutputs = new List<string>();
            var (success, output) = ExecuteShellCommand(command, commandOutputs);
            if (!success)
            {
                return "Offline";
            }
            else
            {
                // Parse the output to get the State line
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Trim().StartsWith("State :"))
                    {
                        var state = line.Substring(line.IndexOf(':') + 1).Trim();
                        switch (state.ToLower())
                        {
                            case "clean":
                                return "Active";
                            case "clean, resyncing":
                                return "Resyncing";
                            case "clean, degraded":
                                return "Degraded";
                            case "clean, degraded, recovering":
                                return "Recovering";
                            case "clean, failed":
                                return "Failed";
                            default:
                                return "Unknown";
                        }
                    }
                }
                return "Unknown";
            }
        }

        public (long Size, long Used, long Available, string UsePercent) GetMountPointSize(string mountPoint)
        {
            try
            {
                // Execute the df command
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "df",
                        Arguments = $"-PB1 {mountPoint}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    },
                };
                process.Start();

                string output = process.StandardOutput.ReadToEnd();
                string errorOutput = process.StandardError.ReadToEnd();
                process.WaitForExit();

                // Split the output into lines
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length < 2)
                {
                    _logger.LogWarning($"No mount information found for {mountPoint}.");
                    return (0, 0, 0, "0%");
                }

                // The first line is the header; the second line contains the data
                var dataLine = lines[1];
                var parts = Regex.Split(dataLine.Trim(), @"\s+");
                if (parts.Length < 6)
                {
                    _logger.LogWarning($"Unexpected df output format for {mountPoint}.");
                    return (0, 0, 0, "0%");
                }

                // Extract the necessary fields
                var sizeStr = parts[1];
                var usedStr = parts[2];
                var availStr = parts[3];
                var usePercent = parts[4];
                var mountedOn = parts[5];

                // Convert string values to long
                if (!long.TryParse(sizeStr, out long size))
                {
                    _logger.LogWarning($"Failed to parse size: {sizeStr}");
                    size = 0;
                }

                if (!long.TryParse(usedStr, out long used))
                {
                    _logger.LogWarning($"Failed to parse used: {usedStr}");
                    used = 0;
                }

                if (!long.TryParse(availStr, out long avail))
                {
                    _logger.LogWarning($"Failed to parse available: {availStr}");
                    avail = 0;
                }

                return (Size: size, Used: used, Available: avail, UsePercent: usePercent);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting mount point size for {mountPoint}: {ex.Message}");
                return (0, 0, 0, "0%");
            }
        }

        public async Task<(bool Success, string Message)> ProtectDriveAsync(string serial)
        {
            var drive = await _context.ProtectedDrives.FirstOrDefaultAsync(d => d.Serial == serial);
            if (drive == null)
            {
                // Find the drive in the active drives list
                var activeDrives = await UpdateActiveDrivesAsync();
                var activeDrive = activeDrives.FirstOrDefault(d => d.Serial == serial);
                if (activeDrive == null)
                {
                    return (false, "Drive not found.");
                }

                drive = new ProtectedDrive
                {
                    Serial = serial,
                    Vendor = activeDrive.Vendor,
                    Model = activeDrive.Model,
                    Name = activeDrive.Name,
                    Label = activeDrive.Label,
                };
                _context.ProtectedDrives.Add(drive);
                await _context.SaveChangesAsync();
                return (true, "Drive protected successfully.");
            }
            return (false, "Drive is already protected.");
        }

        public async Task<(bool Success, string Message)> UnprotectDriveAsync(string serial)
        {
            var drive = await _context.ProtectedDrives.FirstOrDefaultAsync(d => d.Serial == serial);
            if (drive != null)
            {
                _context.ProtectedDrives.Remove(drive);
                await _context.SaveChangesAsync();
                return (true, "Drive unprotected successfully.");
            }
            return (false, "Drive not found in protected list.");
        }

        public async Task<(bool Success, string Message, List<string> Outputs)> CreatePoolAsync(CreatePoolRequest request)
        {
            if (string.IsNullOrEmpty(request.PoolLabel) || request.DriveSerials == null || request.DriveSerials.Count == 0)
            {
                return (false, "Pool Label and at least one drive must be selected.", new List<string>());
            }

            var activeDrives = await UpdateActiveDrivesAsync();
            var drives = activeDrives.Where(d => request.DriveSerials.Contains(d.Serial)).ToList();

            if (!drives.Any())
            {
                return (false, "No active drives found matching the provided serials.", new List<string>());
            }

            // Safety check to prevent operating on protected drives
            var protectedSerials = _context.ProtectedDrives.Select(pd => pd.Serial).ToHashSet();
            if (drives.Any(d => protectedSerials.Contains(d.Serial)))
            {
                return (false, "One or more selected drives are protected.", new List<string>());
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            var commandOutputs = new List<string>();

            try
            {
                // Save PoolGroup to get PoolGroupId and PoolGroupGuid
                var newPoolGroup = new PoolGroup
                {
                    GroupLabel = request.PoolLabel,
                    Drives = new List<PoolDrive>(),
                };
                _context.PoolGroups.Add(newPoolGroup);
                await _context.SaveChangesAsync();
                int poolGroupId = newPoolGroup.PoolGroupId;

                // Initialize a counter for default labels
                int defaultLabelCounter = 1;

                // Update drives and associate with PoolGroup
                foreach (var drive in drives)
                {
                    var dbDrive = _context.PoolDrives.FirstOrDefault(d => d.Serial == drive.Serial);
                    string assignedLabel;

                    // Check if a label was provided for this drive
                    if (request.DriveLabels != null && request.DriveLabels.TryGetValue(drive.Serial, out string? providedLabel))
                    {
                        if (!string.IsNullOrWhiteSpace(providedLabel))
                        {
                            assignedLabel = providedLabel.Trim();
                        }
                        else
                        {
                            // Assign default label if provided label is empty
                            assignedLabel = $"{request.PoolLabel}-{defaultLabelCounter}";
                            defaultLabelCounter++;
                        }
                    }
                    else
                    {
                        // Assign default label if no label is provided
                        assignedLabel = $"{request.PoolLabel}-{defaultLabelCounter}";
                        defaultLabelCounter++;
                    }

                    if (dbDrive == null)
                    {
                        dbDrive = new PoolDrive
                        {
                            Serial = drive.Serial,
                            Vendor = drive.Vendor,
                            Model = drive.Model,
                            Label = assignedLabel,
                            IsMounted = true,
                            IsConnected = true,
                            DevPath = drive.IdLink,
                            Size = drive.Size,
                            PoolGroup = newPoolGroup,
                        };
                        _context.PoolDrives.Add(dbDrive);
                    }
                    else
                    {
                        dbDrive.Label = assignedLabel;
                        dbDrive.IsMounted = true;
                        dbDrive.IsConnected = true;
                        dbDrive.PoolGroup = newPoolGroup;
                    }
                    newPoolGroup.Drives.Add(dbDrive);
                }

                await _context.SaveChangesAsync();

                // Build mdadm command
                string mdadmCommand = $"mdadm --create /dev/md{poolGroupId} --level=1 --raid-devices={drives.Count} ";
                mdadmCommand += string.Join(" ", drives.Select(d => d.IdLink));
                mdadmCommand += " --run --force";

                var mdadmResult = ExecuteShellCommand(mdadmCommand, commandOutputs);
                if (!mdadmResult.success)
                {
                    await transaction.RollbackAsync();
                    return (false, mdadmResult.message, commandOutputs);
                }

                // Format and mount the md device
                string mkfsCommand = $"mkfs.ext4 -F /dev/md{poolGroupId}";
                var mkfsResult = ExecuteShellCommand(mkfsCommand, commandOutputs);
                if (!mkfsResult.success)
                {
                    ExecuteShellCommand($"mdadm --stop /dev/md{poolGroupId}", commandOutputs);
                    await transaction.RollbackAsync();
                    return (false, mkfsResult.message, commandOutputs);
                }

                // Mount the md device
                string mountPath = $"/mnt/backy/md{poolGroupId}";
                string mountCommand = $"mkdir -p {mountPath} && mount /dev/md{poolGroupId} {mountPath}";
                var mountResult = ExecuteShellCommand(mountCommand, commandOutputs);
                if (!mountResult.success)
                {
                    ExecuteShellCommand($"umount {mountPath}", commandOutputs);
                    ExecuteShellCommand($"mdadm --stop /dev/md{poolGroupId}", commandOutputs);
                    await transaction.RollbackAsync();
                    return (false, mountResult.message, commandOutputs);
                }

                // Update MountPath
                newPoolGroup.MountPath = mountPath;
                await _context.SaveChangesAsync();

                // Commit transaction
                await transaction.CommitAsync();

                return (true, $"Pool '{request.PoolLabel}' created successfully.", commandOutputs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating pool.");
                await transaction.RollbackAsync();
                return (false, "An error occurred while creating the pool.", commandOutputs);
            }
        }


        public async Task<(bool Success, string Message)> UnmountPoolAsync(Guid poolGroupGuid)
        {
            var poolGroup = await _context.PoolGroups.Include(pg => pg.Drives).FirstOrDefaultAsync(pg => pg.PoolGroupGuid == poolGroupGuid);

            if (poolGroup == null)
            {
                return (false, "Pool group not found.");
            }

            int poolGroupId = poolGroup.PoolGroupId;

            var commandOutputs = new List<string>();
            string mountPoint = $"/mnt/backy/md{poolGroupId}";
            string unmountCommand = $"umount {mountPoint}";
            var unmountResult = ExecuteShellCommand(unmountCommand, commandOutputs);
            if (!unmountResult.success)
            {
                return (false, unmountResult.message);
            }

            string stopCommand = $"mdadm --stop /dev/md{poolGroupId}";
            var stopResult = ExecuteShellCommand(stopCommand, commandOutputs);
            if (!stopResult.success)
            {
                return (false, stopResult.message);
            }

            foreach (var drive in poolGroup.Drives)
            {
                drive.IsMounted = false;
            }

            // Set PoolEnabled to false
            poolGroup.PoolEnabled = false;

            await _context.SaveChangesAsync();

            return (true, "Pool unmounted successfully.");
        }

        public async Task<(bool Success, string Message)> RenamePoolGroupAsync(RenamePoolRequest request)
        {
            var poolGroup = await _context
                .PoolGroups.Include(pg => pg.Drives)
                .FirstOrDefaultAsync(pg => pg.PoolGroupGuid == request.PoolGroupGuid);

            if (poolGroup == null)
            {
                return (false, "Pool group not found.");
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Update pool label
                poolGroup.GroupLabel = request.NewPoolLabel;

                // Update drive labels
                foreach (var drive in poolGroup.Drives)
                {
                    if (request.DriveLabels.TryGetValue(drive.Id, out string? newLabel))
                    {
                        drive.Label = newLabel?.Trim() ?? drive.Label;
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return (true, "Pool and drive labels updated successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error renaming pool.");
                await transaction.RollbackAsync();
                return (false, "An error occurred while renaming the pool.");
            }
        }


        public async Task<(bool Success, string Message)> RemovePoolGroupAsync(Guid poolGroupGuid)
        {
            var poolGroup = await _context.PoolGroups.Include(pg => pg.Drives).FirstOrDefaultAsync(pg => pg.PoolGroupGuid == poolGroupGuid);

            if (poolGroup == null)
            {
                return (false, "Pool group not found.");
            }

            int poolGroupId = poolGroup.PoolGroupId;

            if (!poolGroup.PoolEnabled)
            {
                // Pool is disabled; remove directly
                _context.PoolGroups.Remove(poolGroup);
                await _context.SaveChangesAsync();
                return (true, "Pool group removed successfully.");
            }
            else
            {
                // Pool is enabled; need to unmount and remove
                var commandOutputs = new List<string>();
                string mountPoint = poolGroup.MountPath;

                // Attempt to unmount
                string unmountCommand = $"umount {mountPoint}";
                var unmountResult = ExecuteShellCommand(unmountCommand, commandOutputs);

                if (!unmountResult.success)
                {
                    return (false, unmountResult.message);
                }

                // Stop the RAID array
                string stopCommand = $"mdadm --stop /dev/md{poolGroupId}";
                var stopResult = ExecuteShellCommand(stopCommand, commandOutputs);

                if (!stopResult.success)
                {
                    return (false, stopResult.message);
                }

                // Clean up drives by wiping filesystem signatures
                foreach (var drive in poolGroup.Drives)
                {
                    string wipeCommand = $"wipefs -a {drive.DevPath}";
                    var wipeResult = ExecuteShellCommand(wipeCommand, commandOutputs);

                    if (!wipeResult.success)
                    {
                        return (false, $"Failed to clean drive {drive.Serial}.");
                    }
                }

                // Remove the PoolGroup from the database
                _context.PoolGroups.Remove(poolGroup);
                await _context.SaveChangesAsync();

                return (true, "Pool group removed successfully.");
            }
        }

        public async Task<(bool Success, string Message)> MountPoolAsync(Guid poolGroupGuid)
        {
            var poolGroup = await _context.PoolGroups.Include(pg => pg.Drives).FirstOrDefaultAsync(pg => pg.PoolGroupGuid == poolGroupGuid);

            if (poolGroup == null)
            {
                return (false, "Pool group not found.");
            }

            int poolGroupId = poolGroup.PoolGroupId;

            var commandOutputs = new List<string>();
            string assembleCommand = $"mdadm --assemble /dev/md{poolGroupId} ";
            assembleCommand += string.Join(" ", poolGroup.Drives.Select(d => d.DevPath));

            // Execute the assemble command
            var assembleResult = ExecuteShellCommand(assembleCommand, commandOutputs);

            // Attempt to mount regardless of assemble result
            string mountPath = $"/mnt/backy/md{poolGroupId}";
            string mountCommand = $"mkdir -p {mountPath} && mount /dev/md{poolGroupId} {mountPath}";
            var mountResult = ExecuteShellCommand(mountCommand, commandOutputs);

            if (!mountResult.success)
            {
                return (false, mountResult.message);
            }

            // Update the database to reflect the mounted drives and enabled pool
            foreach (var drive in poolGroup.Drives)
            {
                drive.IsMounted = true;
            }

            poolGroup.PoolEnabled = true;
            await _context.SaveChangesAsync();

            return (true, "Pool mounted successfully.");
        }

        private (bool success, string message) ExecuteShellCommand(string command, List<string>? outputList = null)
        {
            outputList ??= new List<string>();
            string output;
            int exitCode;

            try
            {
                _logger.LogInformation($"Executing command: {command}");

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "bash",
                        Arguments = $"-c \"{command}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    },
                };

                process.Start();
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                output = stdout + stderr;
                exitCode = process.ExitCode;

                if (outputList != null)
                {
                    outputList.Add($"$ {command}");
                    outputList.Add(output);
                }

                if (exitCode == 0)
                {
                    _logger.LogInformation($"Command executed successfully. Output: {output}");
                    return (true, output.Trim());
                }
                else
                {
                    _logger.LogWarning($"Command failed with exit code {exitCode}. Output: {output}");
                    return (false, output.Trim());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Command execution failed: {ex.Message}");
                if (outputList != null)
                {
                    outputList.Add($"Error executing command: {ex.Message}");
                }
                return (false, "An error occurred while executing the command.");
            }
        }

    }
}
