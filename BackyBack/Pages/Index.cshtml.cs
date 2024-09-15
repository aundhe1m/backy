using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace BackyBack.Pages
{
    public class IndexModel : PageModel
    {
        public List<DriveInfoModel> Drives { get; set; } = new List<DriveInfoModel>();

        public void OnGet()
        {
            Drives = ScanDrives();
        }

        public IActionResult OnPostCreatePartition(string driveName)
        {
            Console.WriteLine($"Received request to create partition on drive: {driveName}");
            var command = $"parted /dev/{driveName} --script mklabel gpt mkpart primary ext4 0% 100%";
            Console.WriteLine($"Running command: {command}");
            ExecuteShellCommand(command, "Partition created successfully.", $"Error creating partition on {driveName}");

            if (Request.Headers.XRequestedWith == "XMLHttpRequest")
            {
                System.Threading.Thread.Sleep(500); 
                return new JsonResult(new { success = true, message = "Partition created successfully." });
            }

            System.Threading.Thread.Sleep(500); 
            return RedirectToPage();
        }

        public IActionResult OnPostFormatPartition(string partitionName)
        {
            var command = $"mkfs.ext4 /dev/{partitionName}";
            Console.WriteLine($"Running command: {command}");
            ExecuteShellCommand(command, "Partition formatted successfully.", $"Error formatting partition {partitionName}");

            if (Request.Headers.XRequestedWith == "XMLHttpRequest")
            {
                System.Threading.Thread.Sleep(500); 
                return new JsonResult(new { success = true, message = "Partition formatted successfully." });
            }

            System.Threading.Thread.Sleep(500); 
            return RedirectToPage();
        }

        public IActionResult OnPostMountPartition(string partitionName, string uuid)
        {
            var mountPath = $"/mnt/{uuid}";
            var command = $"mkdir -p {mountPath} && mount /dev/{partitionName} {mountPath}";
            Console.WriteLine($"Running command: {command}");
            ExecuteShellCommand(command, "Partition mounted successfully.", $"Error mounting partition {partitionName}");

            if (Request.Headers.XRequestedWith == "XMLHttpRequest")
            {
                System.Threading.Thread.Sleep(500); 
                return new JsonResult(new { success = true, message = "Partition mounted successfully." });
            }
            System.Threading.Thread.Sleep(500); 
            return RedirectToPage();
        }

        public IActionResult OnPostUnmountPartition(string partitionName)
        {
            var command = $"umount /dev/{partitionName}";
            Console.WriteLine($"Running command: {command}");
            ExecuteShellCommand(command, "Partition unmounted successfully.", $"Error unmounting partition {partitionName}");

            if (Request.Headers.XRequestedWith == "XMLHttpRequest")
            {
                System.Threading.Thread.Sleep(500); 
                return new JsonResult(new { success = true, message = "Partition unmounted successfully." });
            }
            System.Threading.Thread.Sleep(500); 
            return RedirectToPage();
        }

        public IActionResult OnPostRemovePartition(string partitionName)
        {
            // Extract the drive name and partition number from partitionName (e.g., sda1 -> sda and 1)
            string driveName = new string(partitionName.TakeWhile(c => !char.IsDigit(c)).ToArray());
            string partitionNumber = new string(partitionName.SkipWhile(c => !char.IsDigit(c)).ToArray());

            var umountCommand = $"umount /dev/{partitionName}";
            var partedCommand = $"parted /dev/{driveName} --script rm {partitionNumber}";

            Console.WriteLine($"Running command: {umountCommand}");
            if (ExecuteShellCommandWithExitCode(umountCommand, out string umountOutput, out int exitCode) && (exitCode == 0 || exitCode == 32))
            {
                Console.WriteLine($"Running command: {partedCommand}");
                ExecuteShellCommand(partedCommand, "Partition removed successfully.", $"Error removing partition {partitionName}");
            }
            else
            {
                Console.WriteLine($"Error unmounting partition {partitionName}: {umountOutput}");
            }

            if (Request.Headers.XRequestedWith == "XMLHttpRequest")
            {
                System.Threading.Thread.Sleep(500); 
                return new JsonResult(new { success = true, message = "Partition removed successfully." });
            }

            System.Threading.Thread.Sleep(500); 
            return RedirectToPage();
        }



        // Helper method to execute shell commands
        private void ExecuteShellCommand(string command, string successMessage, string errorMessage)
        {
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
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    Console.WriteLine(successMessage);
                    Console.WriteLine(output);
                }
                else
                {
                    Console.WriteLine(errorMessage);
                    Console.WriteLine(error);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Command execution failed: {ex.Message}");
            }
        }

        // This method will handle commands like 'umount' that need output checking
        private bool ExecuteShellCommandWithExitCode(string command, out string output, out int exitCode)
        {
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
                exitCode = process.ExitCode;

                return exitCode == 0 || exitCode == 32; // Returns true if the command was successful or not mounted
            }
            catch (Exception ex)
            {
                output = $"Command execution failed: {ex.Message}";
                exitCode = -1; // Return -1 to indicate a failure in running the command
                return false;
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
                                    Fstype = p.Fstype ?? "Unknown"
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
            public string UUID { get; set; } = "No UUID";
            public string Vendor { get; set; } = "Unknown Vendor";
            public string Model { get; set; } = "Unknown Model";
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
    }
}
