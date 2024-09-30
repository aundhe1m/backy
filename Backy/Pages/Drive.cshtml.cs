using Backy.Data;
using Backy.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

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

        public void OnGet()
        {
            _logger.LogDebug("Starting OnGet...");

            OrganizeDrives();

            _logger.LogDebug("OnGet completed.");
        }

        private void OrganizeDrives()
        {
            _logger.LogDebug("Organizing drives...");

            var activeDrives = UpdateActiveDrives();
            PoolGroups = _context.PoolGroups.Include(pg => pg.Drives).ThenInclude(d => d.Partitions).ToList();
            ProtectedDrives = _context.ProtectedDrives.ToList();

            // Update connected status and properties for drives in pools
            foreach (var pool in PoolGroups)
            {
                bool allConnected = true;
                foreach (var Drive in pool.Drives)
                {
                    // Find matching active drive
                    var activeDrive = activeDrives.FirstOrDefault(d => d.Serial == Drive.Serial);
                    if (activeDrive != null)
                    {
                        // Update properties
                        Drive.IsConnected = true;
                        Drive.UsedSpace = activeDrive.UsedSpace;
                        Drive.PartitionSize = activeDrive.PartitionSize;
                        Drive.Name = activeDrive.Name;
                        Drive.Vendor = activeDrive.Vendor;
                        Drive.Model = activeDrive.Model;
                        Drive.IsMounted = activeDrive.IsMounted;
                        Drive.IdLink = activeDrive.IdLink;
                        Drive.Partitions = activeDrive.Partitions;
                    }
                    else
                    {
                        Drive.IsConnected = false;
                        allConnected = false;
                    }
                }

                // Set PoolEnabled based on whether all drives are connected
                pool.PoolEnabled = allConnected;
            }

            // NewDrives: drives that are active but not in any pool and not protected
            var pooledDriveSerials = PoolGroups.SelectMany(p => p.Drives).Select(d => d.Serial).ToHashSet();
            var protectedSerials = ProtectedDrives.Select(pd => pd.Serial).ToHashSet();
            NewDrives = activeDrives.Where(d => !pooledDriveSerials.Contains(d.Serial) && !protectedSerials.Contains(d.Serial)).ToList();

            _logger.LogDebug($"Drives organized. PoolGroups: {PoolGroups.Count}, NewDrives: {NewDrives.Count}");
        }

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
                        Arguments = $"-c \"lsblk -J -b -o NAME,SIZE,TYPE,MOUNTPOINT,UUID,SERIAL,VENDOR,MODEL,FSTYPE,PATH\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
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
                        if (device.Name == "sda") continue;

                        if (device.Type == "disk")
                        {
                            var DriveData = new Drive
                            {
                                Name = device.Name ?? "Unknown",
                                Serial = device.Serial ?? "No Serial",
                                Vendor = device.Vendor ?? "Unknown Vendor",
                                Model = device.Model ?? "Unknown Model",
                                IsConnected = true,
                                Partitions = new List<PartitionInfo>(),
                                IdLink = device.Path ?? string.Empty
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
                                        Size = partition.Size ?? 0
                                    };

                                    // Update used space if mounted
                                    if (!string.IsNullOrEmpty(partition.Mountpoint))
                                    {
                                        partitionData.UsedSpace = GetUsedSpace(partition.Mountpoint);
                                        DriveData.IsMounted = true;
                                    }

                                    DriveData.Partitions.Add(partitionData);
                                }

                                // Sum up partition sizes and used spaces
                                DriveData.PartitionSize = DriveData.Partitions.Sum(p => p.Size);
                                DriveData.UsedSpace = DriveData.Partitions.Sum(p => p.UsedSpace);
                            }
                            else
                            {
                                // No partitions, set size to disk size
                                DriveData.PartitionSize = device.Size ?? 0;
                            }

                            // Use disk UUID if available
                            DriveData.UUID = device.Uuid ?? DriveData.Partitions.FirstOrDefault()?.UUID ?? "No UUID";

                            activeDrives.Add(DriveData);
                            _logger.LogDebug($"Active Drive added: {JsonSerializer.Serialize(DriveData)}");
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

        // Handle POST request to protect a drive
        public IActionResult OnPostProtectDrive(string serial)
        {
            var Drive = _context.ProtectedDrives.FirstOrDefault(d => d.Serial == serial);
            if (Drive == null)
            {
                // Find the drive in the active drives list
                var activeDrive = UpdateActiveDrives().FirstOrDefault(d => d.Serial == serial);
                if (activeDrive == null)
                {
                    return BadRequest(new { success = false, message = "Drive not found." });
                }

                Drive = new ProtectedDrive
                {
                    Serial = serial,
                    Vendor = activeDrive.Vendor,
                    Model = activeDrive.Model,
                    Name = activeDrive.Name,
                    Label = activeDrive.Label
                };
                _context.ProtectedDrives.Add(Drive);
                _context.SaveChanges();
                return new JsonResult(new { success = true, message = "Drive protected successfully." });
            }
            return BadRequest(new { success = false, message = "Drive is already protected." });
        }


        // Handle POST request to unprotect a drive
        public IActionResult OnPostUnprotectDrive(string serial)
        {
            var Drive = _context.ProtectedDrives.FirstOrDefault(d => d.Serial == serial);
            if (Drive != null)
            {
                _context.ProtectedDrives.Remove(Drive);
                _context.SaveChanges();
                return new JsonResult(new { success = true, message = "Drive unprotected successfully." });
            }
            return BadRequest(new { success = false, message = "Drive not found in protected list." });
        }

        // Implement OnPostCreatePool to use mdadm with /dev/disk/by-id

        public async Task<IActionResult> OnPostCreatePool()
        {
            _logger.LogInformation($"OnPostCreatePool called.");
            string requestBody = await new StreamReader(Request.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<CreatePoolRequest>(requestBody);

            if (request == null || string.IsNullOrEmpty(request.PoolLabel) || request.DriveSerials == null || !request.DriveSerials.Any())
            {
                return BadRequest(new { success = false, message = "Pool Label and at least one drive must be selected" });
            }

            _logger.LogInformation($"Creating pool with label: {request.PoolLabel} and drives: {string.Join(",", request.DriveSerials)}");

            var activeDrives = UpdateActiveDrives();
            var Drives = activeDrives.Where(d => request.DriveSerials.Contains(d.Serial)).ToList();

            if (!Drives.Any())
            {
                return BadRequest(new { success = false, message = "No active drives found matching the provided serials." });
            }

            // Safety check to prevent operating on protected drives
            var protectedSerials = _context.ProtectedDrives.Select(pd => pd.Serial).ToHashSet();
            if (Drives.Any(d => protectedSerials.Contains(d.Serial)))
            {
                return BadRequest(new { success = false, message = "One or more selected drives are protected." });
            }

            var newPoolGroup = new PoolGroup
            {
                GroupLabel = request.PoolLabel,
                Drives = new List<Drive>()
            };

            var errorMessages = new List<string>();
            var commandOutputs = new List<string>();

            // Build mdadm command
            string mdadmCommand = $"mdadm --create /dev/md{newPoolGroup.PoolGroupId} --level=1 --raid-devices={Drives.Count} ";
            mdadmCommand += string.Join(" ", Drives.Select(d => d.IdLink));
            mdadmCommand += " --run --force";

            var mdadmResult = ExecuteShellCommand(mdadmCommand, commandOutputs);
            if (!mdadmResult.success)
            {
                return BadRequest(new { success = false, message = mdadmResult.message, outputs = commandOutputs });
            }

            // Format and mount the md device
            string mkfsCommand = $"mkfs.ext4 -F /dev/md{newPoolGroup.PoolGroupId}";
            var mkfsResult = ExecuteShellCommand(mkfsCommand, commandOutputs);
            if (!mkfsResult.success)
            {
                return BadRequest(new { success = false, message = mkfsResult.message, outputs = commandOutputs });
            }

            // Mount the md device
            string mountPath = $"/mnt/backy/md{newPoolGroup.PoolGroupId}";
            string mountCommand = $"mkdir -p {mountPath} && mount /dev/md{newPoolGroup.PoolGroupId} {mountPath}";
            var mountResult = ExecuteShellCommand(mountCommand, commandOutputs);
            if (!mountResult.success)
            {
                return BadRequest(new { success = false, message = mountResult.message, outputs = commandOutputs });
            }

            // Update drives and pool group
            foreach (var Drive in Drives)
            {
                Drive.PoolGroup = newPoolGroup;
                Drive.IsMounted = true;
                Drive.Label = request.PoolLabel;
            }

            _context.PoolGroups.Add(newPoolGroup);
            _context.SaveChanges();

            return new JsonResult(new { success = true, message = $"Pool '{request.PoolLabel}' created successfully.", outputs = commandOutputs });
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
                    }
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

        // Implement OnPostUnmountPool to stop mdadm and unmount
        public IActionResult OnPostUnmountPool(int poolGroupId)
        {
            var poolGroup = _context.PoolGroups.Include(pg => pg.Drives).FirstOrDefault(pg => pg.PoolGroupId == poolGroupId);
            if (poolGroup == null)
            {
                return BadRequest(new { success = false, message = "Pool group not found." });
            }

            var commandOutputs = new List<string>();
            string unmountCommand = $"umount /mnt/backy/md{poolGroupId}";
            var unmountResult = ExecuteShellCommand(unmountCommand, commandOutputs);
            if (!unmountResult.success)
            {
                return BadRequest(new { success = false, message = unmountResult.message, outputs = commandOutputs });
            }

            string stopCommand = $"mdadm --stop /dev/md{poolGroupId}";
            var stopResult = ExecuteShellCommand(stopCommand, commandOutputs);
            if (!stopResult.success)
            {
                return BadRequest(new { success = false, message = stopResult.message, outputs = commandOutputs });
            }

            foreach (var Drive in poolGroup.Drives)
            {
                Drive.IsMounted = false;
            }

            _context.SaveChanges();

            return new JsonResult(new { success = true, message = "Pool unmounted successfully.", outputs = commandOutputs });
        }

        // Implement OnPostMountPool to assemble mdadm and mount
        public IActionResult OnPostMountPool(int poolGroupId)
        {
            var poolGroup = _context.PoolGroups.Include(pg => pg.Drives).FirstOrDefault(pg => pg.PoolGroupId == poolGroupId);
            if (poolGroup == null)
            {
                return BadRequest(new { success = false, message = "Pool group not found." });
            }

            var commandOutputs = new List<string>();
            string assembleCommand = $"mdadm --assemble /dev/md{poolGroupId} ";
            assembleCommand += string.Join(" ", poolGroup.Drives.Select(d => d.IdLink));
            var assembleResult = ExecuteShellCommand(assembleCommand, commandOutputs);
            if (!assembleResult.success)
            {
                return BadRequest(new { success = false, message = assembleResult.message, outputs = commandOutputs });
            }

            string mountPath = $"/mnt/backy/md{poolGroupId}";
            string mountCommand = $"mkdir -p {mountPath} && mount /dev/md{poolGroupId} {mountPath}";
            var mountResult = ExecuteShellCommand(mountCommand, commandOutputs);
            if (!mountResult.success)
            {
                return BadRequest(new { success = false, message = mountResult.message, outputs = commandOutputs });
            }

            foreach (var Drive in poolGroup.Drives)
            {
                Drive.IsMounted = true;
            }

            _context.SaveChanges();

            return new JsonResult(new { success = true, message = "Pool mounted successfully.", outputs = commandOutputs });
        }

        // Implement OnPostInspectPool to get mdadm --detail output
        public IActionResult OnPostInspectPool(int poolGroupId)
        {
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
    }

    // Models and DTOs
    public class CreatePoolRequest
    {
        public required string PoolLabel { get; set; }
        public required List<string> DriveSerials { get; set; }
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

        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("children")]
        public List<BlockDevice>? Children { get; set; }
    }
}
