using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Backy.Data;
using Backy.Models;
using Microsoft.EntityFrameworkCore;

namespace Backy.Services
{
    /// <summary>
    /// Defines the contract for drive-related operations.
    /// </summary>
    public interface IDriveService
    {
        string FetchPoolStatus(int poolGroupId);
        (long Size, long Used, long Available, string UsePercent) GetMountPointSize(string mountPoint);
        Task<(bool Success, string Message)> ProtectDriveAsync(string serial);
        Task<(bool Success, string Message)> UnprotectDriveAsync(string serial);
        Task<(bool Success, string Message, List<string> Outputs)> CreatePoolAsync(CreatePoolRequest request);
        Task<(bool Success, string Message)> UnmountPoolAsync(Guid poolGroupGuid);
        Task<(bool Success, string Message)> RemovePoolGroupAsync(Guid poolGroupGuid);
        Task<(bool Success, string Message)> MountPoolAsync(Guid poolGroupGuid);
        Task<(bool Success, string Message)> RenamePoolGroupAsync(RenamePoolRequest request);
        Task<(bool Success, string Message, string Output)> GetPoolDetailAsync(Guid poolGroupGuid);
        Task<(bool Success, string Message)> ForceAddDriveAsync(int driveId, Guid poolGroupGuid, string devPath);
        Task<(bool Success, string Message, List<string> Outputs)> KillProcessesAsync(KillProcessesRequest request);
        Task<List<Drive>> UpdateActiveDrivesAsync();
        Task<List<ProcessInfo>> GetProcessesUsingMountPointAsync(string mountPoint);
    }

    /// <summary>
    /// Implements drive-related operations, including managing pools and handling drive protection.
    /// </summary>
    public class DriveService : IDriveService
    {
        // ---------------------------
        // Private Fields
        // ---------------------------
        private readonly ApplicationDbContext _context;
        private readonly ILogger<DriveService> _logger;

        // ---------------------------
        // Constructor
        // ---------------------------

        /// <summary>
        /// Initializes a new instance of the <see cref="DriveService"/> class.
        /// </summary>
        /// <param name="context">The application database context.</param>
        /// <param name="logger">The logger instance.</param>
        public DriveService(ApplicationDbContext context, ILogger<DriveService> logger)
        {
            _context = context;
            _logger = logger;
        }

        // ---------------------------
        // Public Methods
        // ---------------------------

        /// <summary>
        /// Updates the list of active drives by executing the 'lsblk' command and parsing its output.
        /// </summary>
        /// <returns>A list of active <see cref="Drive"/> objects.</returns>
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
                                        Type = partition.Type ?? "Unknown", // Assuming Type is provided
                                        Path = partition.Path ?? string.Empty, // Populate Path
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

        /// <summary>
        /// Fetches the status of a pool group based on its ID by executing the 'mdadm' command.
        /// </summary>
        /// <param name="poolGroupId">The ID of the pool group.</param>
        /// <returns>The status of the pool as a string.</returns>
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

        /// <summary>
        /// Retrieves the size details of a specified mount point by executing the 'df' command.
        /// </summary>
        /// <param name="mountPoint">The mount point to inspect.</param>
        /// <returns>A tuple containing size, used, available space, and usage percentage.</returns>
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

        /// <summary>
        /// Protects a drive by adding it to the list of protected drives.
        /// </summary>
        /// <param name="serial">The serial number of the drive to protect.</param>
        /// <returns>A tuple indicating success status and a message.</returns>
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

        /// <summary>
        /// Unprotects a drive by removing it from the list of protected drives.
        /// </summary>
        /// <param name="serial">The serial number of the drive to unprotect.</param>
        /// <returns>A tuple indicating success status and a message.</returns>
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

        /// <summary>
        /// Creates a new pool based on the provided request data.
        /// </summary>
        /// <param name="request">The pool creation request containing pool label and drive serials.</param>
        /// <returns>A tuple indicating success status, a message, and a list of command outputs.</returns>
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
                string mountCommand = $"mkdir -p {mountPath} && mount -v /dev/md{poolGroupId} {mountPath}";
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
                return (false, "Error:", commandOutputs);
            }
        }

        /// <summary>
        /// Unmounts a pool based on its GUID by executing the necessary shell commands.
        /// </summary>
        /// <param name="poolGroupGuid">The GUID of the pool group to unmount.</param>
        /// <returns>A tuple indicating success status and a message.</returns>
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

        /// <summary>
        /// Renames a pool group and its associated drive labels based on the provided request.
        /// </summary>
        /// <param name="request">The pool rename request containing new labels.</param>
        /// <returns>A tuple indicating success status and a message.</returns>
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
                foreach (var driveLabel in request.DriveLabels)
                {
                    var drive = poolGroup.Drives.FirstOrDefault(d => d.Id == driveLabel.DriveId);
                    if (drive != null)
                    {
                        drive.Label = driveLabel.Label?.Trim() ?? drive.Label;
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

        /// <summary>
        /// Removes a pool group based on its GUID by executing the necessary shell commands and updating the database.
        /// </summary>
        /// <param name="poolGroupGuid">The GUID of the pool group to remove.</param>
        /// <returns>A tuple indicating success status and a message.</returns>
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

        /// <summary>
        /// Retrieves detailed information about a pool group by executing the 'mdadm' command.
        /// </summary>
        /// <param name="poolGroupGuid">The GUID of the pool group.</param>
        /// <returns>A tuple indicating success status, a message, and the command output.</returns>
        public async Task<(bool Success, string Message, string Output)> GetPoolDetailAsync(Guid poolGroupGuid)
        {
            var poolGroup = await _context.PoolGroups.FirstOrDefaultAsync(pg => pg.PoolGroupGuid == poolGroupGuid);
            if (poolGroup == null)
            {
                return (false, "Pool group not found.", string.Empty);
            }

            int poolGroupId = poolGroup.PoolGroupId;

            string statusCommand = $"mdadm --detail /dev/md{poolGroupId}";
            var commandOutputs = new List<string>();
            var statusResult = ExecuteShellCommand(statusCommand, commandOutputs);

            if (statusResult.success)
            {
                return (true, string.Empty, statusResult.message);
            }
            else
            {
                return (false, statusResult.message, string.Empty);
            }
        }

        /// <summary>
        /// Mounts a pool group by assembling the RAID array and mounting it to the designated path.
        /// </summary>
        /// <param name="poolGroupGuid">The GUID of the pool group to mount.</param>
        /// <returns>A tuple indicating success status and a message.</returns>
        public async Task<(bool Success, string Message)> MountPoolAsync(Guid poolGroupGuid)
        {
            var poolGroup = await _context.PoolGroups.Include(pg => pg.Drives).FirstOrDefaultAsync(pg => pg.PoolGroupGuid == poolGroupGuid);

            if (poolGroup == null)
            {
                return (false, "Pool group not found.");
            }

            int poolGroupId = poolGroup.PoolGroupId;

            // Step 1: Fetch active drives to identify existing RAID devices
            var activeDrives = await UpdateActiveDrivesAsync();

            // Step 2: Identify RAID devices associated with the pool group's drives
            var existingMdPaths = new HashSet<string>();

            foreach (var drive in poolGroup.Drives)
            {
                var activeDrive = activeDrives.FirstOrDefault(d => d.Serial == drive.Serial);
                if (activeDrive != null && activeDrive.Partitions != null)
                {
                    foreach (var partition in activeDrive.Partitions)
                    {
                        if (partition.Type?.ToLower() == "raid1" && !string.IsNullOrEmpty(partition.Path))
                        {
                            existingMdPaths.Add(partition.Path);
                        }
                    }
                }
            }

            // Step 3: Stop existing RAID devices to avoid conflicts
            foreach (var mdPath in existingMdPaths)
            {
                string stopCommand = $"mdadm --stop {mdPath}";
                var stopResult = ExecuteShellCommand(stopCommand, null);
                if (!stopResult.success)
                {
                    _logger.LogWarning($"Failed to stop existing RAID device {mdPath}: {stopResult.message}");
                    // Optionally, decide whether to return failure or continue
                    // For this implementation, we'll continue
                }
            }

            var commandOutputs = new List<string>();

            // Step 4: Assemble the RAID pool
            string assembleCommand = $"mdadm --assemble /dev/md{poolGroupId} ";
            assembleCommand += string.Join(" ", poolGroup.Drives.Select(d => d.DevPath));

            var assembleResult = ExecuteShellCommand(assembleCommand, commandOutputs);
            if (!assembleResult.success)
            {
                _logger.LogError($"Failed to assemble pool: {assembleResult.message}");
                return (false, $"Failed to assemble pool: {assembleResult.message}");
            }

            // Step 5: Mount the assembled RAID pool
            string mountPath = $"/mnt/backy/md{poolGroupId}";
            string mountCommand = $"mkdir -p {mountPath} && mount /dev/md{poolGroupId} {mountPath}";
            var mountResult = ExecuteShellCommand(mountCommand, commandOutputs);

            if (!mountResult.success)
            {
                _logger.LogError($"Failed to mount pool: {mountResult.message}");
                return (false, $"Failed to mount pool: {mountResult.message}");
            }

            // Step 6: Update the database to reflect the mounted drives and enabled pool
            foreach (var drive in poolGroup.Drives)
            {
                drive.IsMounted = true;
            }

            poolGroup.PoolEnabled = true;
            poolGroup.MountPath = mountPath; // Ensure MountPath is updated
            await _context.SaveChangesAsync();

            _logger.LogInformation($"Pool '/dev/md{poolGroupId}' mounted successfully at '{mountPath}'.");
            return (true, $"Pool '/dev/md{poolGroupId}' mounted successfully at '{mountPath}'.");
        }

        /// <summary>
        /// Retrieves a list of processes using the specified mount point by executing the 'lsof' command.
        /// </summary>
        /// <param name="mountPoint">The mount point to inspect.</param>
        /// <returns>A list of <see cref="ProcessInfo"/> objects representing the processes.</returns>
        public async Task<List<ProcessInfo>> GetProcessesUsingMountPointAsync(string mountPoint)
        {
            return await Task.Run(() =>
            {
                var command = $"lsof +f -- {mountPoint}";
                var commandOutputs = new List<string>();
                var (success, message) = ExecuteShellCommand(command, commandOutputs);
                if (!success)
                {
                    _logger.LogWarning($"Failed to execute lsof command: {message}");
                    return new List<ProcessInfo>();
                }

                // Parse the output
                return ParseLsofOutput(message);
            });
        }

        /// <summary>
        /// Forcefully adds a drive to a pool group by executing the 'mdadm' command.
        /// </summary>
        /// <param name="driveId">The ID of the drive to add.</param>
        /// <param name="poolGroupGuid">The GUID of the pool group.</param>
        /// <param name="devPath">The device path of the drive.</param>
        /// <returns>A tuple indicating success status and a message.</returns>
        public async Task<(bool Success, string Message)> ForceAddDriveAsync(int driveId, Guid poolGroupGuid, string devPath)
        {
            var poolGroup = await _context.PoolGroups.FirstOrDefaultAsync(pg => pg.PoolGroupGuid == poolGroupGuid);
            if (poolGroup == null)
            {
                return (false, "Pool group not found.");
            }

            int poolGroupId = poolGroup.PoolGroupId;

            string command = $"mdadm --manage /dev/md{poolGroupId} --add {devPath}";
            var commandOutputs = new List<string>();
            var (success, message) = ExecuteShellCommand(command, commandOutputs);

            if (success)
            {
                // Optionally update the drive's status
                var drive = await _context.PoolDrives.FirstOrDefaultAsync(d => d.Id == driveId);
                if (drive != null)
                {
                    drive.IsMounted = true;
                    await _context.SaveChangesAsync();
                }

                return (true, "Drive added to the pool successfully.");
            }
            else
            {
                return (false, message);
            }
        }

        /// <summary>
        /// Kills specified processes and performs actions such as unmounting or removing pool groups.
        /// </summary>
        /// <param name="request">The request containing process IDs and the action to perform.</param>
        /// <returns>A tuple indicating success status, a message, and a list of command outputs.</returns>
        public async Task<(bool Success, string Message, List<string> Outputs)> KillProcessesAsync(KillProcessesRequest request)
        {
            if (request == null)
            {
                await Task.CompletedTask; // Ensure an await exists
                return (false, "Invalid request data.", new List<string>());
            }

            if (request.PoolGroupGuid == Guid.Empty)
            {
                await Task.CompletedTask; // Ensure an await exists
                return (false, "Invalid Pool Group GUID.", new List<string>());
            }

            var poolGroup = await _context
                .PoolGroups.Include(pg => pg.Drives)
                .FirstOrDefaultAsync(pg => pg.PoolGroupGuid == request.PoolGroupGuid);

            if (poolGroup == null)
            {
                await Task.CompletedTask; // Ensure an await exists
                return (false, "Pool group not found.", new List<string>());
            }

            int poolGroupId = poolGroup.PoolGroupId;

            var commandOutputs = new List<string>();

            // Kill the processes
            foreach (var pid in request.Pids)
            {
                string killCommand = $"kill -9 {pid}";
                var killResult = ExecuteShellCommand(killCommand, commandOutputs);
                if (!killResult.success)
                {
                    await Task.CompletedTask; // Ensure an await exists
                    return (false, $"Failed to kill process {pid}.", commandOutputs);
                }
            }

            // After killing processes, perform actions based on Action
            if (request.Action.Equals("UnmountPool", StringComparison.OrdinalIgnoreCase))
            {
                // Retry unmount and mdadm --stop
                string mountPoint = $"/mnt/backy/md{poolGroupId}";
                string unmountCommand = $"umount {mountPoint}";
                var unmountResult = ExecuteShellCommand(unmountCommand, commandOutputs);
                if (!unmountResult.success)
                {
                    await Task.CompletedTask; // Ensure an await exists
                    return (false, unmountResult.message, commandOutputs);
                }

                string stopCommand = $"mdadm --stop /dev/md{poolGroupId}";
                var stopResult = ExecuteShellCommand(stopCommand, commandOutputs);
                if (!stopResult.success)
                {
                    await Task.CompletedTask; // Ensure an await exists
                    return (false, stopResult.message, commandOutputs);
                }

                // Update drive statuses and pool
                foreach (var drive in poolGroup.Drives)
                {
                    drive.IsMounted = false;
                }

                // Set PoolEnabled to false
                poolGroup.PoolEnabled = false;

                await _context.SaveChangesAsync();

                return (true, "Pool unmounted successfully after killing processes.", commandOutputs);
            }
            else if (request.Action.Equals("RemovePoolGroup", StringComparison.OrdinalIgnoreCase))
            {
                // Retry unmount and mdadm --stop
                string mountPoint = poolGroup.MountPath;
                string unmountCommand = $"umount {mountPoint}";
                var unmountResult = ExecuteShellCommand(unmountCommand, commandOutputs);
                if (!unmountResult.success)
                {
                    await Task.CompletedTask; // Ensure an await exists
                    return (false, unmountResult.message, commandOutputs);
                }

                string stopCommand = $"mdadm --stop /dev/md{poolGroupId}";
                var stopResult = ExecuteShellCommand(stopCommand, commandOutputs);
                if (!stopResult.success)
                {
                    await Task.CompletedTask; // Ensure an await exists
                    return (false, stopResult.message, commandOutputs);
                }

                // Wipe filesystem signatures
                foreach (var drive in poolGroup.Drives)
                {
                    string wipeCommand = $"wipefs -a {drive.DevPath}";
                    var wipeResult = ExecuteShellCommand(wipeCommand, commandOutputs);
                    if (!wipeResult.success)
                    {
                        await Task.CompletedTask; // Ensure an await exists
                        return (false, $"Failed to clean drive {drive.Serial}.", commandOutputs);
                    }
                }

                // Remove the PoolGroup from the database
                _context.PoolGroups.Remove(poolGroup);
                await _context.SaveChangesAsync();

                return (true, "Pool group removed successfully after killing processes.", commandOutputs);
            }
            else
            {
                await Task.CompletedTask; // Ensure an await exists
                return (false, "Unknown action for kill processes.", new List<string>());
            }
        }

        // ---------------------------
        // Private Helper Methods
        // ---------------------------

        /// <summary>
        /// Parses the output of the 'lsof' command to extract process information.
        /// </summary>
        /// <param name="lsofOutput">The raw output string from the 'lsof' command.</param>
        /// <returns>A list of <see cref="ProcessInfo"/> objects.</returns>
        private List<ProcessInfo> ParseLsofOutput(string lsofOutput)
        {
            var processes = new List<ProcessInfo>();
            var lines = lsofOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
                return processes; // No processes found

            // The first line is the header
            var headers = Regex.Split(lines[0].Trim(), @"\s+");
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var columns = Regex.Split(line.Trim(), @"\s+");
                if (columns.Length < headers.Length)
                {
                    _logger.LogWarning($"Line {i + 1} has fewer columns than headers. Skipping line.");
                    continue; // Skip lines that don't have enough columns
                }

                var process = new ProcessInfo();
                for (int j = 0; j < headers.Length && j < columns.Length; j++)
                {
                    try
                    {
                        switch (headers[j].ToUpper())
                        {
                            case "COMMAND":
                                process.Command = columns[j];
                                break;
                            case "PID":
                                if (int.TryParse(columns[j], out int pid))
                                {
                                    process.PID = pid;
                                }
                                else
                                {
                                    _logger.LogWarning($"Invalid PID value '{columns[j]}' at line {i + 1}.");
                                    process.PID = 0;
                                }
                                break;
                            case "USER":
                                process.User = columns[j];
                                break;
                            case "NAME":
                                process.Name = columns[j];
                                break;
                            default:
                                // Ignore unhandled headers (FD, TYPE, DEVICE, SIZE/OFF, NODE)
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error parsing column {j + 1} ('{headers[j]}') at line {i + 1}: {ex.Message}");
                    }
                }
                processes.Add(process);
            }
            return processes;
        }

        /// <summary>
        /// Executes a shell command and captures its output.
        /// </summary>
        /// <param name="command">The shell command to execute.</param>
        /// <param name="outputList">A list to capture the command's output.</param>
        /// <returns>A tuple indicating success status and the combined output message.</returns>
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
