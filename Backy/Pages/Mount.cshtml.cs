using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Backy.Pages
{
    public class MountModel : PageModel
    {
        public List<DriveInfoModel> Drives { get; set; } = new List<DriveInfoModel>();

        public void OnGet()
        {
            Drives = ScanDrives();
        }

        public IActionResult OnPostCreatePartition(string driveName)
        {
            var command = $"parted /dev/{driveName} --script mklabel gpt mkpart primary ext4 0% 100%";
            return ExecuteShellCommandWithExitCode(command);
        }

        public IActionResult OnPostFormatPartition(string partitionName)
        {
            var command = $"mkfs.ext4 /dev/{partitionName}";
            return ExecuteShellCommandWithExitCode(command);
        }

        public IActionResult OnPostMountPartition(string partitionName, string uuid)
        {
            var mountPath = $"/mnt/backy/{uuid}";
            var command = $"mkdir -p {mountPath} && mount /dev/{partitionName} {mountPath}";
            return ExecuteShellCommandWithExitCode(command);
        }

        public IActionResult OnPostUnmountPartition(string partitionName)
        {
            var command = $"umount -f /dev/{partitionName}";
            return ExecuteShellCommandWithExitCode(command);
        }

        public IActionResult OnPostRemovePartition(string partitionName)
        {
            // Extract the drive name and partition number from partitionName (e.g., sda1 -> sda and 1)
            string driveName = new string(partitionName.TakeWhile(c => !char.IsDigit(c)).ToArray());
            string partitionNumber = new string(partitionName.SkipWhile(c => !char.IsDigit(c)).ToArray());

            var partedCommand = $"parted /dev/{driveName} --script rm {partitionNumber}";
            return ExecuteShellCommandWithExitCode(partedCommand);
        }

        private JsonResult ExecuteShellCommandWithExitCode(string command)
        {
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

                if (exitCode == 0)
                {
                    Console.WriteLine($"Success Output: {output}");
                    return new JsonResult(new { success = true, message = output });
                }
                else
                {
                    Console.WriteLine($"Failure Output: {output}");
                    return new JsonResult(new { success = false, message = output });
                }
            }
            catch (Exception ex)
            {
                output = $"Command execution failed: {ex.Message}";
                return new JsonResult(new { success = false, message = output });
            }
        }

        private List<DriveInfoModel> ScanDrives()
        {
            var driveList = new List<DriveInfoModel>();

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "lsblk",
                        Arguments = "-J -b -o NAME,SIZE,TYPE,MOUNTPOINT,UUID,SERIAL,VENDOR,MODEL,WWN,FSTYPE",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                };

                process.Start();
                string jsonOutput = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                var lsblkOutput = JsonSerializer.Deserialize<LsblkOutput>(jsonOutput);

                if (lsblkOutput?.Blockdevices != null)
                {
                    foreach (var device in lsblkOutput.Blockdevices)
                    {
                        if (device.Type == "disk" && device.Name != "sda")
                        {
                            var drive = new DriveInfoModel
                            {
                                Name = device.Name ?? "Unknown",
                                Size = device.Size ?? 0,
                                Serial = device.Serial ?? "No Serial",
                                UUID = device.Uuid ?? "No UUID",
                                Vendor = device.Vendor ?? "Unknown Vendor",
                                Model = device.Model ?? "Unknown Model",
                                Partitions = device.Children?.Select(p => new PartitionInfoModel
                                {
                                    Name = p.Name ?? "Unknown",
                                    Size = p.Size ?? 0,
                                    MountPoint = GetMountPoint(p),
                                    UUID = p.Uuid ?? "No UUID",
                                    Fstype = p.Fstype ?? "Unknown",
                                    Label = GetPartitionLabel(p.Uuid) // Retrieve Label from drives.json
                                }).ToList() ?? new List<PartitionInfoModel>()
                            };

                            driveList.Add(drive);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            return driveList;
        }

        // Method to retrieve the Label from /mnt/backy/drives.json
        private string? GetPartitionLabel(string? uuid)
        {
            if (string.IsNullOrEmpty(uuid)) return null;

            var persistentFilePath = "/mnt/backy/drives.json";
            if (System.IO.File.Exists(persistentFilePath))
            {
                try
                {
                    var jsonContent = System.IO.File.ReadAllText(persistentFilePath);
                    var persistentData = JsonSerializer.Deserialize<PersistentData>(jsonContent);

                    if (persistentData != null && persistentData.Pools != null)
                    {
                        // Search for the drive with matching UUID
                        foreach (var pool in persistentData.Pools)
                        {
                            var drive = pool.Drives.FirstOrDefault(d => d.UUID == uuid);
                            if (drive != null && !string.IsNullOrEmpty(drive.Label))
                            {
                                Console.WriteLine($"Found label for UUID {uuid}: {drive.Label}");
                                return drive.Label;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading persistent data for UUID {uuid}: {ex.Message}");
                }
            }

            return null; // Return null if no label found
        }

        private string GetMountPoint(BlockDevice device)
        {
            if (!string.IsNullOrEmpty(device.Mountpoint))
            {
                return device.Mountpoint;
            }

            if (device.Children != null)
            {
                foreach (var child in device.Children)
                {
                    if (!string.IsNullOrEmpty(child.Mountpoint))
                    {
                        return child.Mountpoint;
                    }
                }
            }

            return "Not Mounted";
        }

        // Drive and partition models
        public class DriveInfoModel
        {
            public string Name { get; set; } = string.Empty;
            public long? Size { get; set; } = null;
            public string Serial { get; set; } = "No Serial";
            public string? UUID { get; set; } = null;
            public string Vendor { get; set; } = "Unknown Vendor";
            public string Model { get; set; } = "Unknown Model";
            public string? Label { get; set; } = null;
            public List<PartitionInfoModel> Partitions { get; set; } = new List<PartitionInfoModel>();
        }

        public class PartitionInfoModel
        {
            public string Name { get; set; } = string.Empty;
            public long? Size { get; set; } = null;
            public string? MountPoint { get; set; }
            public string? UUID { get; set; }
            public string Fstype { get; set; } = "Unknown";
            public int? Partn { get; set; }
            public string? Label { get; set; } = null;
        }

        public class LsblkOutput
        {
            [JsonPropertyName("blockdevices")]
            public List<BlockDevice> Blockdevices { get; set; } = new List<BlockDevice>();
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

            [JsonPropertyName("partn")]
            public int Partn { get; set; }

            [JsonPropertyName("children")]
            public List<BlockDevice>? Children { get; set; }
        }

        // Persistent Data Models
        public class PersistentData
        {
            public List<PoolGroup> Pools { get; set; } = new List<PoolGroup>();
        }

        public class PoolGroup
        {
            public required string PoolGroupId { get; set; }
            public required string GroupLabel { get; set; }
            public bool PoolEnabled { get; set; }
            public List<DriveMetaData> Drives { get; set; } = new List<DriveMetaData>();
        }

        public class DriveMetaData
        {
            [JsonPropertyName("label")]
            public string? Label { get; set; }

            [JsonPropertyName("serial")]
            public required string Serial { get; set; }

            [JsonPropertyName("uuid")]
            public required string UUID { get; set; }

            [JsonPropertyName("vendor")]
            public required string Vendor { get; set; }

            [JsonPropertyName("model")]
            public required string Model { get; set; }

            [JsonPropertyName("partition_size")]
            public long PartitionSize { get; set; }

            [JsonPropertyName("used_space")]
            public long UsedSpace { get; set; }

            [JsonPropertyName("is_connected")]
            public bool IsConnected { get; set; }
        }

    }
}
