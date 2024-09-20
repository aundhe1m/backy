using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
using System.IO;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Backy.Pages
{
    [IgnoreAntiforgeryToken]
    public class DriveModel : PageModel
    {
        private readonly ILogger<DriveModel> _logger;
        private static string mountDirectory = "/mnt/backy";
        private static string persistentFilePath = Path.Combine(mountDirectory, "drives.json");

        public List<PoolGroup> PoolGroups { get; set; } = new List<PoolGroup>(); // For pool groups
        public List<DriveMetaData> NewDrives { get; set; } = new List<DriveMetaData>(); // For newly connected drives

        public DriveModel(ILogger<DriveModel> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Handles GET requests and organizes drives into pools and new drives.
        /// </summary>
        public void OnGet()
        {
            _logger.LogInformation("Starting OnGet...");

            // Load persistent data
            var persistentData = LoadPersistentData();
            _logger.LogInformation($"Loaded Persistent Data: {JsonSerializer.Serialize(persistentData)}");

            // Organize drives into PoolGroups and NewDrives
            OrganizeDrives(persistentData);

            // Save the updated data back to persistent storage
            SavePersistentData(persistentData);

            _logger.LogInformation("OnGet completed.");
        }

        /// <summary>
        /// Loads persistent data from the JSON file.
        /// </summary>
        private PersistentData LoadPersistentData()
        {
            try
            {
                if (System.IO.File.Exists(persistentFilePath))
                {
                    _logger.LogInformation($"Loading persistent data from {persistentFilePath}");
                    var json = System.IO.File.ReadAllText(persistentFilePath);
                    return JsonSerializer.Deserialize<PersistentData>(json) ?? new PersistentData();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading persistent data: {ex.Message}");
            }

            return new PersistentData(); // Return empty PersistentData if loading fails
        }

        /// <summary>
        /// Saves persistent data to the JSON file.
        /// </summary>
        private void SavePersistentData(PersistentData data)
        {
            try
            {
                _logger.LogInformation("Saving Persistent Data...");
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(persistentFilePath, json);
                _logger.LogInformation($"Data saved: {json}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error saving persistent data: {ex.Message}");
            }
        }

        /// <summary>
        /// Organizes drives into pool groups and new drives.
        /// </summary>
        private void OrganizeDrives(PersistentData persistentData)
        {
            _logger.LogInformation("Organizing drives...");

            var activeDrives = UpdateActiveDrives();
            PoolGroups = persistentData.Pools;

            // Update connected status and properties for drives in pools
            foreach (var pool in PoolGroups)
            {
                bool allConnected = true;
                foreach (var drive in pool.Drives)
                {
                    // Find matching active drive
                    var activeDrive = activeDrives.FirstOrDefault(d => d.UUID == drive.UUID);
                    if (activeDrive != null)
                    {
                        // Update properties
                        drive.IsConnected = true;
                        drive.UsedSpace = activeDrive.UsedSpace;
                        drive.PartitionSize = activeDrive.PartitionSize;
                        drive.Serial = activeDrive.Serial;
                        drive.Vendor = activeDrive.Vendor;
                        drive.Model = activeDrive.Model;
                        drive.IsMounted = activeDrive.IsMounted;
                    }
                    else
                    {
                        drive.IsConnected = false;
                        allConnected = false;
                    }
                }

                // Set PoolEnabled based on whether all drives are connected
                pool.PoolEnabled = allConnected;
            }

            // NewDrives: drives that are active but not in any pool
            var pooledDriveUUIDs = PoolGroups.SelectMany(p => p.Drives).Select(d => d.UUID).ToHashSet();
            NewDrives = activeDrives.Where(d => !pooledDriveUUIDs.Contains(d.UUID)).ToList();

            _logger.LogInformation($"Drives organized. PoolGroups: {PoolGroups.Count}, NewDrives: {NewDrives.Count}");
        }

        /// <summary>
        /// Handles POST requests to rename a pool group.
        /// </summary>
        public IActionResult OnPostRenamePoolGroup(string poolGroupId, string newLabel)
        {
            var persistentData = LoadPersistentData();
            var poolGroup = persistentData.Pools.FirstOrDefault(p => p.PoolGroupId == poolGroupId);
            if (poolGroup != null)
            {
                poolGroup.GroupLabel = newLabel;
                SavePersistentData(persistentData);
                return new JsonResult(new { success = true, message = "Pool group renamed successfully." });
            }
            return BadRequest(new { success = false, message = "Pool group not found." });
        }

        /// <summary>
        /// Handles POST requests to remove a pool group.
        /// </summary>
        public IActionResult OnPostRemovePoolGroup(string poolGroupId)
        {
            var persistentData = LoadPersistentData();
            var poolGroup = persistentData.Pools.FirstOrDefault(p => p.PoolGroupId == poolGroupId);
            if (poolGroup != null)
            {
                persistentData.Pools.Remove(poolGroup);
                SavePersistentData(persistentData);
                return new JsonResult(new { success = true, message = "Pool group removed successfully." });
            }
            return BadRequest(new { success = false, message = "Pool group not found." });
        }

        /// <summary>
        /// Updates the list of active drives, including unpartitioned and unmounted drives.
        /// </summary>
        private List<DriveMetaData> UpdateActiveDrives()
        {
            var activeDrives = new List<DriveMetaData>();
            _logger.LogInformation("Updating active drives...");

            try
            {
                // Use lsblk to find drives
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "bash",
                        Arguments = $"-c \"lsblk -J -b -o NAME,SIZE,TYPE,MOUNTPOINT,UUID,SERIAL,VENDOR,MODEL,FSTYPE\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                string jsonOutput = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                _logger.LogInformation($"lsblk output: {jsonOutput}");

                var lsblkOutput = JsonSerializer.Deserialize<LsblkOutput>(jsonOutput);
                if (lsblkOutput?.Blockdevices != null)
                {
                    foreach (var device in lsblkOutput.Blockdevices)
                    {
                        // Skip sda and its children
                        if (device.Name == "sda") continue;

                        if (device.Type == "disk")
                        {
                            var driveData = new DriveMetaData
                            {
                                Name = device.Name ?? "Unknown",
                                Serial = device.Serial ?? "No Serial",
                                Vendor = device.Vendor ?? "Unknown Vendor",
                                Model = device.Model ?? "Unknown Model",
                                IsConnected = true,
                                Partitions = new List<PartitionInfoModel>()
                            };

                            // If the disk has partitions
                            if (device.Children != null)
                            {
                                foreach (var partition in device.Children)
                                {
                                    var partitionData = new PartitionInfoModel
                                    {
                                        Name = partition.Name ?? "Unknown",
                                        UUID = partition.Uuid ?? "No UUID",
                                        Fstype = partition.Fstype ?? "Unknown",
                                        MountPoint = partition.Mountpoint ?? "Not Mounted",
                                        Size = partition.Size ?? 0
                                    };

                                    // Update used space if mounted
                                    if (!string.IsNullOrEmpty(partition.Mountpoint))
                                    {
                                        partitionData.UsedSpace = GetUsedSpace(partition.Mountpoint);
                                        driveData.IsMounted = true;
                                    }

                                    driveData.Partitions.Add(partitionData);
                                }

                                // Sum up partition sizes and used spaces
                                driveData.PartitionSize = driveData.Partitions.Sum(p => p.Size);
                                driveData.UsedSpace = driveData.Partitions.Sum(p => p.UsedSpace);
                            }
                            else
                            {
                                // No partitions, set size to disk size
                                driveData.PartitionSize = device.Size ?? 0;
                            }

                            // Use disk UUID if available
                            driveData.UUID = device.Uuid ?? driveData.Partitions.FirstOrDefault()?.UUID ?? "No UUID";

                            activeDrives.Add(driveData);
                            _logger.LogInformation($"Active drive added: {JsonSerializer.Serialize(driveData)}");
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
        /// Handles POST requests to create a pool, including wiping, partitioning, formatting, and mounting drives.
        /// </summary>
        public async Task<IActionResult> OnPostCreatePool()
        {
            _logger.LogInformation($"OnPostCreatePool called.");
            // Read the request body manually
            string requestBody = await new StreamReader(Request.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<CreatePoolRequest>(requestBody);

            if (request == null || string.IsNullOrEmpty(request.PoolLabel) || request.DriveNames == null || !request.DriveNames.Any())
            {
                return BadRequest(new { success = false, message = "Pool Label and at least one drive must be selected" });
            }

            _logger.LogInformation($"Creating pool with label: {request.PoolLabel} and drives: {string.Join(",", request.DriveNames)}");

            var persistentData = LoadPersistentData();
            var newPoolGroup = new PoolGroup
            {
                PoolGroupId = Guid.NewGuid().ToString(),
                GroupLabel = request.PoolLabel,
                Drives = new List<DriveMetaData>()
            };

            var errorMessages = new List<string>();
            var commandOutputs = new List<string>();
            int driveIndex = 1;

            foreach (var driveName in request.DriveNames)
            {
                // Unmount the drive before wiping
                var unmountResult = UnmountDrive(driveName, commandOutputs);
                if (!unmountResult.success)
                {
                    errorMessages.Add($"Failed to unmount drive {driveName}: {unmountResult.message}");
                    _logger.LogError($"Failed to unmount drive {driveName}: {unmountResult.message}");
                    continue;
                }

                // Wipe the drive
                var wipeResult = WipeDrive(driveName, commandOutputs);
                if (!wipeResult.success)
                {
                    errorMessages.Add($"Failed to wipe drive {driveName}: {wipeResult.message}");
                    _logger.LogError($"Failed to wipe drive {driveName}: {wipeResult.message}");
                    continue;
                }

                // Create partition
                var (partitionName, createPartSuccess, createPartMessage) = CreatePartition(driveName, commandOutputs);
                if (!createPartSuccess || string.IsNullOrEmpty(partitionName))
                {
                    errorMessages.Add($"Failed to create partition on drive {driveName}: {createPartMessage}");
                    _logger.LogError($"Failed to create partition on drive {driveName}: {createPartMessage}");
                    continue;
                }

                // Format partition
                var (formatSuccess, formatMessage) = FormatPartition(partitionName, commandOutputs);
                if (!formatSuccess)
                {
                    errorMessages.Add($"Failed to format partition {partitionName}: {formatMessage}");
                    _logger.LogError($"Failed to format partition {partitionName}: {formatMessage}");
                    continue;
                }

                // Mount partition
                var uuid = GetPartitionUUID(partitionName);
                var (mountSuccess, mountMessage) = MountPartition(partitionName, uuid, commandOutputs);
                if (!mountSuccess)
                {
                    errorMessages.Add($"Failed to mount partition {partitionName}: {mountMessage}");
                    _logger.LogError($"Failed to mount partition {partitionName}: {mountMessage}");
                    continue;
                }

                // Create DriveMetaData
                var driveData = new DriveMetaData
                {
                    UUID = uuid,
                    Serial = "No Serial",
                    Vendor = "Unknown Vendor",
                    Model = "Unknown Model",
                    PartitionSize = GetPartitionSize(partitionName),
                    UsedSpace = GetUsedSpace($"/mnt/backy/{uuid}"),
                    IsConnected = true,
                    IsMounted = true,
                    Label = $"{request.PoolLabel}-{driveIndex++}",
                    Partitions = new List<PartitionInfoModel>
                    {
                        new PartitionInfoModel
                        {
                            Name = partitionName,
                            UUID = uuid,
                            MountPoint = $"/mnt/backy/{uuid}",
                            Size = GetPartitionSize(partitionName),
                            UsedSpace = GetUsedSpace($"/mnt/backy/{uuid}"),
                            Fstype = "ext4"
                        }
                    }
                };

                newPoolGroup.Drives.Add(driveData);
                _logger.LogInformation($"Drive {driveData.UUID} added to pool {request.PoolLabel}");
            }

            if (errorMessages.Any())
            {
                return BadRequest(new { success = false, message = string.Join("; ", errorMessages), outputs = commandOutputs });
            }

            persistentData.Pools.Add(newPoolGroup);
            SavePersistentData(persistentData);

            return new JsonResult(new { success = true, message = $"Pool '{request.PoolLabel}' created successfully.", outputs = commandOutputs });
        }

        /// <summary>
        /// Wipes all partitions on a drive.
        /// </summary>
        private (bool success, string message) WipeDrive(string driveName, List<string> outputList)
        {
            var command = $"wipefs -a /dev/{driveName}";
            return ExecuteShellCommand(command, outputList);
        }

        /// <summary>
        /// Creates a new partition on a drive and returns the partition name.
        /// </summary>
        private (string partitionName, bool success, string message) CreatePartition(string driveName, List<string> outputList)
        {
            var command = $"parted /dev/{driveName} --script mklabel gpt mkpart primary ext4 0% 100%";
            var result = ExecuteShellCommand(command, outputList);
            if (result.success)
            {
                return ($"{driveName}1", true, result.message);
            }
            return (string.Empty, false, result.message);
        }

        /// <summary>
        /// Formats a partition to ext4.
        /// </summary>
        private (bool success, string message) FormatPartition(string partitionName, List<string> outputList)
        {
            var command = $"mkfs.ext4 /dev/{partitionName}";
            return ExecuteShellCommand(command, outputList);
        }

        /// <summary>
        /// Mounts a partition.
        /// </summary>
        private (bool success, string message) MountPartition(string partitionName, string uuid, List<string> outputList)
        {
            var mountPath = $"/mnt/backy/{uuid}";
            var command = $"mkdir -p {mountPath} && mount /dev/{partitionName} {mountPath}";
            return ExecuteShellCommand(command, outputList);
        }


        /// <summary>
        /// Gets the UUID of a partition.
        /// </summary>
        private string GetPartitionUUID(string partitionName)
        {
            var command = $"blkid -s UUID -o value /dev/{partitionName}";
            var result = ExecuteShellCommand(command);
            return result.success ? result.message.Trim() : "No UUID";
        }

        /// <summary>
        /// Gets the size of a partition.
        /// </summary>
        private long GetPartitionSize(string partitionName)
        {
            var command = $"blockdev --getsize64 /dev/{partitionName}";
            var result = ExecuteShellCommand(command);
            return result.success && long.TryParse(result.message.Trim(), out long size) ? size : 0;
        }

        // Handles POST requests to unmount a partition
        public IActionResult OnPostUnmountPartition(string partitionName)
        {
            var result = UnmountPartition(partitionName);
            if (result.success)
            {
                return new JsonResult(new { success = true, message = $"Partition {partitionName} unmounted successfully." });
            }
            else
            {
                return new JsonResult(new { success = false, message = result.message });
            }
        }

        // Handles POST requests to remove a drive
        public IActionResult OnPostRemoveDrive(string uuid)
        {
            var persistentData = LoadPersistentData();
            var poolGroup = persistentData.Pools.FirstOrDefault(p => p.Drives.Any(d => d.UUID == uuid));
            if (poolGroup != null)
            {
                var drive = poolGroup.Drives.FirstOrDefault(d => d.UUID == uuid);
                if (drive != null)
                {
                    poolGroup.Drives.Remove(drive);
                    SavePersistentData(persistentData);
                    return new JsonResult(new { success = true, message = "Drive removed successfully." });
                }
            }
            return BadRequest(new { success = false, message = "Drive not found." });
        }

        // Handles POST requests to rename a drive label
        public IActionResult OnPostRenameDriveLabel(string uuid, string newLabel)
        {
            var persistentData = LoadPersistentData();
            var drive = persistentData.Pools.SelectMany(p => p.Drives).FirstOrDefault(d => d.UUID == uuid);
            if (drive != null)
            {
                drive.Label = newLabel;
                SavePersistentData(persistentData);
                return new JsonResult(new { success = true, message = "Drive label renamed successfully." });
            }
            return BadRequest(new { success = false, message = "Drive not found." });
        }

        // Unmounts a partition
        private (bool success, string message) UnmountPartition(string partitionName)
        {
            var command = $"umount -f /dev/{partitionName}";
            return ExecuteShellCommand(command);
        }

        // Unmounts a drive
        private (bool success, string message) UnmountDrive(string driveName, List<string> outputList)
        {
            var command = $"umount -f /dev/{driveName}*";
            return ExecuteShellCommand(command, outputList);
        }

        // Handles POST requests to wipe a drive
        public IActionResult OnPostWipeDrive(string driveName)
        {
            var commandOutputs = new List<string>();
            var result = WipeDrive(driveName, commandOutputs);
            if (result.success)
            {
                return new JsonResult(new { success = true, message = $"Drive {driveName} wiped successfully." });
            }
            else
            {
                return new JsonResult(new { success = false, message = result.message });
            }
        }

        // Handles POST requests to mount a partition
        public IActionResult OnPostMountPartition(string partitionName)
        {
            var uuid = GetPartitionUUID(partitionName);
            var commandOutputs = new List<string>(); // Initialize the output list
            var result = MountPartition(partitionName, uuid, commandOutputs); // Pass the list
            if (result.success)
            {
                return new JsonResult(new { success = true, message = $"Partition {partitionName} mounted successfully." });
            }
            else
            {
                return new JsonResult(new { success = false, message = result.message });
            }
        }


        /// <summary>
        /// Executes a shell command and returns success status and output message.
        /// </summary>
        private (bool success, string message) ExecuteShellCommand(string command, List<string> outputList = null)
        {
            outputList ??= new List<string>();
            string output;
            int exitCode;

            try
            {
                Console.WriteLine($"Executing: {command}");
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
                    }
                };
                process.Start();
                output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit();
                System.Threading.Thread.Sleep(500);
                exitCode = process.ExitCode;

                if (outputList != null)
                {
                    outputList.Add($"$ {command}");
                    outputList.Add(output);
                }

                if (exitCode == 0)
                {
                    Console.WriteLine($"Success Output: {output}");
                    return (true, output);
                }
                else
                {
                    Console.WriteLine($"Failure Output: {output}");
                    return (false, output);
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
        /// Gets the used space of a mounted partition.
        /// </summary>
        private long GetUsedSpace(string mountpoint)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "bash",
                        Arguments = $"-c \"df -B1 | grep {mountpoint} | awk '{{print $3}}'\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                string result = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                return long.TryParse(result, out long used) ? used : 0;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting used space for mountpoint {mountpoint}: {ex.Message}");
                return 0;
            }
        }
    }

    // Models and DTOs
    public class CreatePoolRequest
    {
        public required string PoolLabel { get; set; }
        public required List<string> DriveNames { get; set; } // Changed from Uuids to DriveNames
    }

    public class PersistentData
    {
        public List<PoolGroup> Pools { get; set; } = new List<PoolGroup>();
    }

    public class PoolGroup
    {
        public required string PoolGroupId { get; set; }  // Unique identifier for the pool
        public string GroupLabel { get; set; } = "Unnamed Group";
        public bool PoolEnabled { get; set; } = false;
        public List<DriveMetaData> Drives { get; set; } = new List<DriveMetaData>();
    }

    public class DriveMetaData
    {
        public string? Name { get; set; }
        public string? Label { get; set; }
        public string Serial { get; set; } = "No Serial";
        public string UUID { get; set; } = "No UUID";
        public string Vendor { get; set; } = "Unknown Vendor";
        public string Model { get; set; } = "Unknown Model";
        public long PartitionSize { get; set; } = 0;
        public long UsedSpace { get; set; } = 0;
        public bool IsConnected { get; set; } = false;
        public bool IsMounted { get; set; } = false;
        public List<PartitionInfoModel> Partitions { get; set; } = new List<PartitionInfoModel>();
    }

    public class PartitionInfoModel
    {
        public string Name { get; set; } = string.Empty;
        public string UUID { get; set; } = "No UUID";
        public string MountPoint { get; set; } = "Not Mounted";
        public long Size { get; set; } = 0;
        public long UsedSpace { get; set; } = 0;
        public string Fstype { get; set; } = "Unknown";
    }

    public class LsblkOutput
    {
        [JsonPropertyName("blockdevices")]
        public List<BlockDevice>? Blockdevices { get; set; }
    }

    public class BlockDevice
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("size")]
        public long? Size { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("mountpoint")]
        public string? Mountpoint { get; set; }

        [JsonPropertyName("uuid")]
        public string? Uuid { get; set; }

        [JsonPropertyName("serial")]
        public string? Serial { get; set; }

        [JsonPropertyName("vendor")]
        public string? Vendor { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("fstype")]
        public string? Fstype { get; set; }

        [JsonPropertyName("children")]
        public List<BlockDevice>? Children { get; set; }
    }
}
