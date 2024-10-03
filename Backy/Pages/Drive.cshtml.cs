using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Backy.Data;
using Backy.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Backy.Pages
{
    [IgnoreAntiforgeryToken]
    public class DriveModel : PageModel
    {
        private readonly ILogger<DriveModel> _logger;
        private readonly ApplicationDbContext _context;

        public List<PoolGroup> PoolGroups { get; set; } = new List<PoolGroup>();
        public List<Drive> NewDrives { get; set; } = new List<Drive>();
        public List<ProtectedDrive> ProtectedDrives { get; set; } = new List<ProtectedDrive>();

        public DriveModel(ILogger<DriveModel> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        /// <summary>
        /// Handles the GET request for the page.
        /// Organizes the drives into pools, new drives, and protected drives.
        /// </summary>
        public void OnGet()
        {
            _logger.LogDebug("Starting OnGet...");

            OrganizeDrives();

            _logger.LogDebug("OnGet completed.");
        }

        /// <summary>
        /// Organizes the drives into PoolGroups, NewDrives, and ProtectedDrives.
        /// Updates the status of each drive and pool group.
        /// </summary>
        private void OrganizeDrives()
        {
            _logger.LogDebug("Organizing drives...");

            var activeDrives = UpdateActiveDrives();
            PoolGroups = _context.PoolGroups.Include(pg => pg.Drives).ToList();
            ProtectedDrives = _context.ProtectedDrives.ToList();

            // Update connected status and properties for drives in pools
            foreach (var pool in PoolGroups)
            {
                bool allConnected = true;
                foreach (var drive in pool.Drives)
                {
                    // Find matching active drive
                    var activeDrive = activeDrives.FirstOrDefault(d => d.Serial == drive.Serial);
                    if (activeDrive != null)
                    {
                        // Update properties
                        drive.IsConnected = true;
                        drive.Vendor = activeDrive.Vendor;
                        drive.Model = activeDrive.Model;
                        drive.IsMounted = activeDrive.IsMounted;
                        drive.DevPath = activeDrive.IdLink;
                        drive.Size = activeDrive.Size;
                    }
                    else
                    {
                        drive.IsConnected = false;
                        allConnected = false;
                    }
                }

                // Set PoolEnabled based on whether all drives are connected
                pool.AllDrivesConnected = allConnected;

                if (pool.PoolEnabled && !string.IsNullOrEmpty(pool.MountPath))
                {
                    var (size, used, available, usePercent) = GetMountPointSize(pool.MountPath);
                    pool.Size = size;
                    pool.Used = used;
                    pool.Available = available;
                    pool.UsePercent = usePercent;
                }
            }

            // NewDrives: drives that are active but not in any pool and not protected
            var pooledDriveSerials = PoolGroups
                .SelectMany(p => p.Drives)
                .Select(d => d.Serial)
                .ToHashSet();
            var protectedSerials = ProtectedDrives.Select(pd => pd.Serial).ToHashSet();
            NewDrives = activeDrives
                .Where(d =>
                    !pooledDriveSerials.Contains(d.Serial) && !protectedSerials.Contains(d.Serial)
                )
                .ToList();

            _logger.LogDebug(
                $"Drives organized. PoolGroups: {PoolGroups.Count}, NewDrives: {NewDrives.Count}"
            );
        }

        /// <summary>
        /// Updates the list of active drives by parsing the output of 'lsblk'.
        /// Skips the system drive (sda) and collects information about connected drives.
        /// </summary>
        /// <returns>List of active drives.</returns>
        private List<Drive> UpdateActiveDrives()
        {
            var activeDrives = new List<Drive>();
            _logger.LogDebug("Updating active drives...");

            try
            {
                // Use lsblk to find drives
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "bash",
                        Arguments =
                            $"-c \"lsblk -J -b -o NAME,SIZE,TYPE,MOUNTPOINT,UUID,SERIAL,VENDOR,MODEL,FSTYPE,PATH,ID-LINK\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    },
                };
                process.Start();
                string jsonOutput = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                _logger.LogDebug($"lsblk output: {jsonOutput}");

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
                                IsConnected = true,
                                Partitions = new List<PartitionInfo>(),
                                IdLink = !string.IsNullOrEmpty(device.IdLink)
                                    ? $"/dev/disk/by-id/{device.IdLink}"
                                    : device.Path ?? string.Empty,
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

                                    driveData.Partitions.Add(partitionData);
                                }
                            }

                            activeDrives.Add(driveData);
                            _logger.LogDebug(
                                $"Active Drive added: {JsonSerializer.Serialize(driveData)}"
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating active drives: {ex.Message}");
            }

            return activeDrives;
        }

        /// <summary>
        /// Protects a drive by adding it to the list of protected drives.
        /// Prevents the drive from being included in pool creation.
        /// </summary>
        /// <param name="serial">The serial number of the drive to protect.</param>
        /// <returns>An IActionResult indicating success or failure.</returns>
        public IActionResult OnPostProtectDrive(string serial)
        {
            var drive = _context.ProtectedDrives.FirstOrDefault(d => d.Serial == serial);
            if (drive == null)
            {
                // Find the drive in the active drives list
                var activeDrive = UpdateActiveDrives().FirstOrDefault(d => d.Serial == serial);
                if (activeDrive == null)
                {
                    return BadRequest(new { success = false, message = "Drive not found." });
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
                _context.SaveChanges();
                return new JsonResult(
                    new { success = true, message = "Drive protected successfully." }
                );
            }
            return BadRequest(new { success = false, message = "Drive is already protected." });
        }

        /// <summary>
        /// Unprotects a drive by removing it from the list of protected drives.
        /// </summary>
        /// <param name="serial">The serial number of the drive to unprotect.</param>
        /// <returns>An IActionResult indicating success or failure.</returns>
        public IActionResult OnPostUnprotectDrive(string serial)
        {
            var drive = _context.ProtectedDrives.FirstOrDefault(d => d.Serial == serial);
            if (drive != null)
            {
                _context.ProtectedDrives.Remove(drive);
                _context.SaveChanges();
                return new JsonResult(
                    new { success = true, message = "Drive unprotected successfully." }
                );
            }
            return BadRequest(
                new { success = false, message = "Drive not found in protected list." }
            );
        }

        /// <summary>
        /// Handles the creation of a new pool.
        /// </summary>
        /// <returns>Action result.</returns>
        public async Task<IActionResult> OnPostCreatePool()
        {
            _logger.LogInformation($"OnPostCreatePool called.");
            string requestBody = await new StreamReader(Request.Body).ReadToEndAsync();
            CreatePoolRequest? request = JsonSerializer.Deserialize<CreatePoolRequest>(requestBody);

            if (
                request == null
                || string.IsNullOrEmpty(request.PoolLabel)
                || request.DriveSerials == null
                || request.DriveSerials.Count == 0
            )
            {
                return BadRequest(
                    new
                    {
                        success = false,
                        message = "Pool Label and at least one drive must be selected",
                    }
                );
            }

            _logger.LogInformation(
                $"Creating pool with label: {request.PoolLabel} and drives: {string.Join(",", request.DriveSerials)}"
            );

            var activeDrives = UpdateActiveDrives();
            var drives = activeDrives.Where(d => request.DriveSerials.Contains(d.Serial)).ToList();

            if (!drives.Any())
            {
                return BadRequest(
                    new
                    {
                        success = false,
                        message = "No active drives found matching the provided serials.",
                    }
                );
            }

            // Safety check to prevent operating on protected drives
            var protectedSerials = _context.ProtectedDrives.Select(pd => pd.Serial).ToHashSet();
            if (drives.Any(d => protectedSerials.Contains(d.Serial)))
            {
                return BadRequest(
                    new { success = false, message = "One or more selected drives are protected." }
                );
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

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
                Guid poolGroupGuid = newPoolGroup.PoolGroupGuid;

                // Initialize a counter for default labels
                int defaultLabelCounter = 1;

                // Update drives and associate with PoolGroup
                foreach (var drive in drives)
                {
                    var dbDrive = _context.PoolDrives.FirstOrDefault(d => d.Serial == drive.Serial);
                    string assignedLabel;

                    // Check if a label was provided for this drive
                    if (
                        request.DriveLabels != null
                        && request.DriveLabels.TryGetValue(drive.Serial, out string? providedLabel)
                    )
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

                var commandOutputs = new List<string>();

                // Build mdadm command
                string mdadmCommand =
                    $"mdadm --create /dev/md{poolGroupId} --level=1 --raid-devices={drives.Count} ";
                mdadmCommand += string.Join(" ", drives.Select(d => d.IdLink));
                mdadmCommand += " --run --force";

                var mdadmResult = ExecuteShellCommand(mdadmCommand, commandOutputs);
                if (!mdadmResult.success)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(
                        new
                        {
                            success = false,
                            message = mdadmResult.message,
                            outputs = commandOutputs,
                        }
                    );
                }

                // Format and mount the md device
                string mkfsCommand = $"mkfs.ext4 -F /dev/md{poolGroupId}";
                var mkfsResult = ExecuteShellCommand(mkfsCommand, commandOutputs);
                if (!mkfsResult.success)
                {
                    ExecuteShellCommand($"mdadm --stop /dev/md{poolGroupId}", commandOutputs);
                    await transaction.RollbackAsync();
                    return BadRequest(
                        new
                        {
                            success = false,
                            message = mkfsResult.message,
                            outputs = commandOutputs,
                        }
                    );
                }

                // Mount the md device
                string mountPath = $"/mnt/backy/md{poolGroupId}";
                string mountCommand =
                    $"mkdir -p {mountPath} && mount /dev/md{poolGroupId} {mountPath}";
                var mountResult = ExecuteShellCommand(mountCommand, commandOutputs);
                if (!mountResult.success)
                {
                    ExecuteShellCommand($"umount {mountPath}", commandOutputs);
                    ExecuteShellCommand($"mdadm --stop /dev/md{poolGroupId}", commandOutputs);
                    await transaction.RollbackAsync();
                    return BadRequest(
                        new
                        {
                            success = false,
                            message = mountResult.message,
                            outputs = commandOutputs,
                        }
                    );
                }

                // Update MountPath
                newPoolGroup.MountPath = mountPath;
                await _context.SaveChangesAsync();

                // Commit transaction
                await transaction.CommitAsync();

                return new JsonResult(
                    new
                    {
                        success = true,
                        message = $"Pool '{request.PoolLabel}' created successfully.",
                        outputs = commandOutputs,
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error creating pool: {ex.Message}");
                await transaction.RollbackAsync();
                return BadRequest(
                    new { success = false, message = "An error occurred while creating the pool." }
                );
            }
        }

        // New handler for renaming pools
        public async Task<IActionResult> OnPostRenamePoolGroupAsync()
        {
            try
            {
                // Read form data
                var form = Request.Form;

                if (!form.ContainsKey("PoolGroupGuid") || !form.ContainsKey("NewPoolLabel"))
                {
                    return BadRequest(
                        new { success = false, message = "Missing required fields." }
                    );
                }

                if (!Guid.TryParse(form["PoolGroupGuid"], out Guid poolGroupGuid))
                {
                    return BadRequest(
                        new { success = false, message = "Invalid Pool Group GUID format." }
                    );
                }

                // Extract 'newPoolLabel' from the form data
                string newPoolLabel = form["NewPoolLabel"].ToString();

                // Retrieve the PoolGroup with its Drives
                var poolGroup = await _context
                    .PoolGroups.Include(pg => pg.Drives)
                    .FirstOrDefaultAsync(pg => pg.PoolGroupGuid == poolGroupGuid);

                if (poolGroup == null)
                {
                    return BadRequest(new { success = false, message = "Pool group not found." });
                }

                // Deserialize DriveLabels JSON string
                string driveLabelsJson = form.ContainsKey("DriveLabels")
                    ? form["DriveLabels"].ToString()
                    : "{}";
                Dictionary<int, string?> driveLabels;
                try
                {
                    driveLabels =
                        JsonSerializer.Deserialize<Dictionary<int, string?>>(driveLabelsJson)
                        ?? new Dictionary<int, string?>();
                }
                catch (JsonException)
                {
                    return BadRequest(
                        new { success = false, message = "Invalid DriveLabels JSON format." }
                    );
                }

                // Begin transaction
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    // Update pool label
                    poolGroup.GroupLabel = newPoolLabel;

                    // Update drive labels
                    foreach (var drive in poolGroup.Drives)
                    {
                        if (driveLabels.TryGetValue(drive.Id, out string? newLabel))
                        {
                            // Only update if a new label is provided; otherwise, retain existing
                            if (!string.IsNullOrWhiteSpace(newLabel))
                            {
                                drive.Label = newLabel.Trim();
                            }
                        }
                    }

                    await _context.SaveChangesAsync();

                    // Commit transaction
                    await transaction.CommitAsync();

                    // Prepare the response DTO
                    var response = new RenamePoolResponse
                    {
                        Success = true,
                        Message = "Pool and drive labels updated successfully.",
                        NewPoolLabel = poolGroup.GroupLabel,
                        UpdatedDriveLabels = poolGroup
                            .Drives.Where(d => driveLabels.ContainsKey(d.Id))
                            .ToDictionary(d => d.Id, d => d.Label),
                    };

                    return new JsonResult(response);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error renaming pool: {ex.Message}");
                    await transaction.RollbackAsync();
                    return BadRequest(
                        new
                        {
                            success = false,
                            message = "An error occurred while renaming the pool.",
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing rename pool request: {ex.Message}");
                return BadRequest(
                    new
                    {
                        success = false,
                        message = "An error occurred while processing the request.",
                    }
                );
            }
        }

        /// <summary>
        /// Executes a shell command and captures its output.
        /// </summary>
        /// <param name="command">The command to execute.</param>
        /// <param name="outputList">An optional list to store the output.</param>
        /// <returns>A tuple indicating success and the command output or error message.</returns>
        private (bool success, string message) ExecuteShellCommand(
            string command,
            List<string>? outputList = null
        )
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
                    _logger.LogWarning(
                        $"Command failed with exit code {exitCode}. Output: {output}"
                    );
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

        /// <summary>
        /// Handles the unmounting of a pool.
        /// </summary>
        /// <param name="poolGroupGuid">Pool group GUID.</param>
        /// <returns>Action result.</returns>
        public IActionResult OnPostUnmountPool(Guid poolGroupGuid)
        {
            var poolGroup = _context
                .PoolGroups.Include(pg => pg.Drives)
                .FirstOrDefault(pg => pg.PoolGroupGuid == poolGroupGuid);

            if (poolGroup == null)
            {
                return BadRequest(new { success = false, message = "Pool group not found." });
            }

            int poolGroupId = poolGroup.PoolGroupId; // Retrieve poolGroupId

            var commandOutputs = new List<string>();
            string mountPoint = $"/mnt/backy/md{poolGroupId}";
            string unmountCommand = $"umount {mountPoint}";
            var unmountResult = ExecuteShellCommand(unmountCommand, commandOutputs);
            if (!unmountResult.success)
            {
                if (unmountResult.message.Contains("target is busy"))
                {
                    // Run lsof to find processes using the mount point
                    string lsofCommand = $"lsof +f -- {mountPoint}";
                    var lsofResult = ExecuteShellCommand(lsofCommand, commandOutputs);
                    if (lsofResult.success)
                    {
                        // Parse lsof output into a list of processes
                        var processes = ParseLsofOutput(lsofResult.message);
                        return new JsonResult(
                            new
                            {
                                success = false,
                                message = "Target is busy",
                                processes = processes,
                            }
                        );
                    }
                    else
                    {
                        return BadRequest(
                            new
                            {
                                success = false,
                                message = "Failed to run lsof.",
                                outputs = commandOutputs,
                            }
                        );
                    }
                }
                else
                {
                    return BadRequest(
                        new
                        {
                            success = false,
                            message = unmountResult.message,
                            outputs = commandOutputs,
                        }
                    );
                }
            }

            string stopCommand = $"mdadm --stop /dev/md{poolGroup.PoolGroupId}";
            var stopResult = ExecuteShellCommand(stopCommand, commandOutputs);
            if (!stopResult.success)
            {
                return BadRequest(
                    new
                    {
                        success = false,
                        message = stopResult.message,
                        outputs = commandOutputs,
                    }
                );
            }

            foreach (var drive in poolGroup.Drives)
            {
                drive.IsMounted = false;
            }

            // Set PoolEnabled to false
            poolGroup.PoolEnabled = false;

            _context.SaveChanges();

            return new JsonResult(
                new
                {
                    success = true,
                    message = "Pool unmounted successfully.",
                    outputs = commandOutputs,
                }
            );
        }

        // Handler for Removing PoolGroup
        public IActionResult OnPostRemovePoolGroup(Guid poolGroupGuid)
        {
            var poolGroup = _context
                .PoolGroups.Include(pg => pg.Drives)
                .FirstOrDefault(pg => pg.PoolGroupGuid == poolGroupGuid);

            if (poolGroup == null)
            {
                return BadRequest(new { success = false, message = "Pool group not found." });
            }

            int poolGroupId = poolGroup.PoolGroupId;

            if (!poolGroup.PoolEnabled)
            {
                // Pool is disabled; remove directly
                _context.PoolGroups.Remove(poolGroup);
                _context.SaveChanges();
                return new JsonResult(
                    new { success = true, message = "Pool group removed successfully." }
                );
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
                    if (unmountResult.message.Contains("target is busy"))
                    {
                        // Find processes using the mount point
                        string lsofCommand = $"lsof +f -- {mountPoint}";
                        var lsofResult = ExecuteShellCommand(lsofCommand, commandOutputs);

                        if (lsofResult.success)
                        {
                            var processes = ParseLsofOutput(lsofResult.message);
                            return new JsonResult(
                                new
                                {
                                    success = false,
                                    message = "Target is busy",
                                    processes = processes,
                                }
                            );
                        }
                        else
                        {
                            return BadRequest(
                                new
                                {
                                    success = false,
                                    message = "Failed to run lsof.",
                                    outputs = commandOutputs,
                                }
                            );
                        }
                    }
                    else
                    {
                        return BadRequest(
                            new
                            {
                                success = false,
                                message = unmountResult.message,
                                outputs = commandOutputs,
                            }
                        );
                    }
                }

                // Stop the RAID array
                string stopCommand = $"mdadm --stop /dev/md{poolGroup.PoolGroupId}";
                var stopResult = ExecuteShellCommand(stopCommand, commandOutputs);

                if (!stopResult.success)
                {
                    return BadRequest(
                        new
                        {
                            success = false,
                            message = stopResult.message,
                            outputs = commandOutputs,
                        }
                    );
                }

                // Clean up drives by wiping filesystem signatures
                foreach (var drive in poolGroup.Drives)
                {
                    string wipeCommand = $"wipefs -a {drive.DevPath}";
                    var wipeResult = ExecuteShellCommand(wipeCommand, commandOutputs);

                    if (!wipeResult.success)
                    {
                        return BadRequest(
                            new
                            {
                                success = false,
                                message = $"Failed to clean drive {drive.Serial}.",
                                outputs = commandOutputs,
                            }
                        );
                    }
                }

                // Remove the PoolGroup from the database
                _context.PoolGroups.Remove(poolGroup);
                _context.SaveChanges();

                return new JsonResult(
                    new { success = true, message = "Pool group removed successfully." }
                );
            }
        }

        /// <summary>
        /// Parses the output of the 'lsof' command to retrieve processes using a mount point.
        /// </summary>
        /// <param name="lsofOutput">The output string from 'lsof'.</param>
        /// <returns>A list of ProcessInfo objects representing the processes.</returns>
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
                    _logger.LogWarning(
                        $"Line {i + 1} has fewer columns than headers. Skipping line."
                    );
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
                                    _logger.LogWarning(
                                        $"Invalid PID value '{columns[j]}' at line {i + 1}."
                                    );
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
                        _logger.LogError(
                            $"Error parsing column {j + 1} ('{headers[j]}') at line {i + 1}: {ex.Message}"
                        );
                    }
                }
                processes.Add(process);
            }
            return processes;
        }

        public IActionResult OnPostKillProcesses([FromBody] KillProcessesRequest request)
        {
            if (request == null)
            {
                return BadRequest(new { success = false, message = "Invalid request data." });
            }

            if (request.PoolGroupGuid == Guid.Empty)
            {
                return BadRequest(new { success = false, message = "Invalid Pool Group GUID." });
            }

            var poolGroup = _context
                .PoolGroups.Include(pg => pg.Drives)
                .FirstOrDefault(pg => pg.PoolGroupGuid == request.PoolGroupGuid);

            if (poolGroup == null)
            {
                return BadRequest(new { success = false, message = "Pool group not found." });
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
                    return BadRequest(
                        new
                        {
                            success = false,
                            message = $"Failed to kill process {pid}.",
                            outputs = commandOutputs,
                        }
                    );
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
                    return BadRequest(
                        new
                        {
                            success = false,
                            message = unmountResult.message,
                            outputs = commandOutputs,
                        }
                    );
                }

                string stopCommand = $"mdadm --stop /dev/md{poolGroupId}";
                var stopResult = ExecuteShellCommand(stopCommand, commandOutputs);
                if (!stopResult.success)
                {
                    return BadRequest(
                        new
                        {
                            success = false,
                            message = stopResult.message,
                            outputs = commandOutputs,
                        }
                    );
                }

                // Update drive statuses and pool
                foreach (var drive in poolGroup.Drives)
                {
                    drive.IsMounted = false;
                }

                poolGroup.PoolEnabled = false;
                _context.SaveChanges();

                return new JsonResult(
                    new
                    {
                        success = true,
                        message = "Pool unmounted successfully after killing processes.",
                        outputs = commandOutputs,
                    }
                );
            }
            else if (request.Action.Equals("RemovePoolGroup", StringComparison.OrdinalIgnoreCase))
            {
                // Retry unmount and mdadm --stop
                string mountPoint = poolGroup.MountPath;
                string unmountCommand = $"umount {mountPoint}";
                var unmountResult = ExecuteShellCommand(unmountCommand, commandOutputs);
                if (!unmountResult.success)
                {
                    return BadRequest(
                        new
                        {
                            success = false,
                            message = unmountResult.message,
                            outputs = commandOutputs,
                        }
                    );
                }

                string stopCommand = $"mdadm --stop /dev/md{poolGroupId}";
                var stopResult = ExecuteShellCommand(stopCommand, commandOutputs);
                if (!stopResult.success)
                {
                    return BadRequest(
                        new
                        {
                            success = false,
                            message = stopResult.message,
                            outputs = commandOutputs,
                        }
                    );
                }

                // Wipe filesystem signatures
                foreach (var drive in poolGroup.Drives)
                {
                    string wipeCommand = $"wipefs -a {drive.DevPath}";
                    var wipeResult = ExecuteShellCommand(wipeCommand, commandOutputs);
                    if (!wipeResult.success)
                    {
                        return BadRequest(
                            new
                            {
                                success = false,
                                message = $"Failed to clean drive {drive.Serial}.",
                                outputs = commandOutputs,
                            }
                        );
                    }
                }

                // Remove the PoolGroup from the database
                _context.PoolGroups.Remove(poolGroup);
                _context.SaveChanges();

                return new JsonResult(
                    new
                    {
                        success = true,
                        message = "Pool group removed successfully after killing processes.",
                    }
                );
            }
            else
            {
                return BadRequest(
                    new { success = false, message = "Unknown action for kill processes." }
                );
            }
        }

        // Implement OnPostMountPool to assemble mdadm and mount
        public IActionResult OnPostMountPool(Guid poolGroupGuid)
        {
            var poolGroup = _context
                .PoolGroups.Include(pg => pg.Drives)
                .FirstOrDefault(pg => pg.PoolGroupGuid == poolGroupGuid);

            if (poolGroup == null)
            {
                return BadRequest(new { success = false, message = "Pool group not found." });
            }

            int poolGroupId = poolGroup.PoolGroupId; // Retrieve poolGroupId

            var commandOutputs = new List<string>();
            string assembleCommand = $"mdadm --assemble /dev/md{poolGroupId} ";
            assembleCommand += string.Join(" ", poolGroup.Drives.Select(d => d.DevPath));
            var assembleResult = ExecuteShellCommand(assembleCommand, commandOutputs);
            if (!assembleResult.success)
            {
                return BadRequest(
                    new
                    {
                        success = false,
                        message = assembleResult.message,
                        outputs = commandOutputs,
                    }
                );
            }

            string mountPath = $"/mnt/backy/md{poolGroupId}";
            string mountCommand = $"mkdir -p {mountPath} && mount /dev/md{poolGroupId} {mountPath}";
            var mountResult = ExecuteShellCommand(mountCommand, commandOutputs);
            if (!mountResult.success)
            {
                return BadRequest(
                    new
                    {
                        success = false,
                        message = mountResult.message,
                        outputs = commandOutputs,
                    }
                );
            }

            foreach (var Drive in poolGroup.Drives)
            {
                Drive.IsMounted = true;
            }

            poolGroup.PoolEnabled = true;
            _context.SaveChanges();

            return new JsonResult(
                new
                {
                    success = true,
                    message = "Pool mounted successfully.",
                    outputs = commandOutputs,
                }
            );
        }

        // Implement OnPostInspectPool to get mdadm --detail output
        public IActionResult OnPostInspectPool(Guid poolGroupGuid)
        {
            var poolGroup = _context.PoolGroups.FirstOrDefault(pg =>
                pg.PoolGroupGuid == poolGroupGuid
            );
            if (poolGroup == null)
            {
                return BadRequest(new { success = false, message = "Pool group not found." });
            }

            int poolGroupId = poolGroup.PoolGroupId; // Retrieve poolGroupId

            string inspectCommand = $"mdadm --detail /dev/md{poolGroupId}";
            var commandOutputs = new List<string>();
            var inspectResult = ExecuteShellCommand(inspectCommand, commandOutputs);

            if (inspectResult.success)
            {
                return new JsonResult(new { success = true, output = inspectResult.message });
            }
            else
            {
                return BadRequest(new { success = false, message = inspectResult.message });
            }
        }

        /// <summary>
        /// Retrieves the size, used space, available space, and usage percentage of a mount point.
        /// </summary>
        /// <param name="mountPoint">The path to the mount point.</param>
        /// <returns>A tuple containing size, used space, available space, and usage percentage.</returns>
        private (long Size, long Used, long Available, string UsePercent) GetMountPointSize(
            string mountPoint
        )
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
                        RedirectStandardError = true, // Capture stderr for error handling
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    },
                };
                process.Start();

                string output = process.StandardOutput.ReadToEnd();
                string errorOutput = process.StandardError.ReadToEnd();
                process.WaitForExit();

                // Log the command outputs for debugging
                _logger.LogDebug($"df output: {output}");
                if (!string.IsNullOrEmpty(errorOutput))
                {
                    _logger.LogError($"df error output: {errorOutput}");
                    throw new Exception($"df error: {errorOutput}");
                }

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
                // parts[1] = Size, parts[2] = Used, parts[3] = Available, parts[4] = Use%, parts[5] = Mounted on
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

                // Optionally, validate the mount point
                if (!mountedOn.Equals(mountPoint, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        $"Mount point mismatch: expected {mountPoint}, got {mountedOn}"
                    );
                    // Decide how to handle this scenario
                }

                return (Size: size, Used: used, Available: avail, UsePercent: usePercent);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting mount point size for {mountPoint}: {ex.Message}");
                return (0, 0, 0, "0%");
            }
        }
    }
}
