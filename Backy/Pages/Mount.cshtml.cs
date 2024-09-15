using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Backy.Pages;

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



    public IActionResult OnPostAddPartitionToLibrary(string partitionName, string uuid, string serial, string vendor, string model, string label)
    {
        try
        {
            // Define the path where the file will be created
            var mountPath = $"/mnt/backy/{uuid}";
            var filePath = Path.Combine(mountPath, "drive_meta.json");

            // Log the path where the file will be created
            Console.WriteLine($"Attempting to create file at: {filePath}");

            // Prepare the content to be written to the file
            var driveMetadata = new
            {
                Label = label,
                Serial = serial,
                UUID = uuid,
                Vendor = vendor,
                Model = model
            };

            // Convert the object to JSON
            var jsonContent = JsonSerializer.Serialize(driveMetadata, new JsonSerializerOptions { WriteIndented = true });

            // Log the content that will be written to the file
            Console.WriteLine("Content to be written to file:");
            Console.WriteLine(jsonContent);

            // Ensure the directory exists
            if (!Directory.Exists(mountPath))
            {
                Console.WriteLine($"Directory {mountPath} does not exist. Attempting to create it.");
                Directory.CreateDirectory(mountPath);
            }

            // Write the JSON content to the file
            System.IO.File.WriteAllText(filePath, jsonContent);

            // Verify that the file was created and log the success or failure
            if (System.IO.File.Exists(filePath))
            {
                Console.WriteLine($"File created successfully at: {filePath}");
                return new JsonResult(new { success = true, message = "Partition added to library successfully." });
            }
            else
            {
                Console.WriteLine($"Failed to create the file at: {filePath}");
                return new JsonResult(new { success = false, message = "Failed to create the file." });
            }
        }
        catch (Exception ex)
        {
            // Log any exceptions that occur during the process
            Console.WriteLine($"Error occurred while adding partition to library: {ex.Message}");
            return new JsonResult(new { success = false, message = $"Error: {ex.Message}" });
        }
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
                                Label = GetPartitionLabel(p.Uuid) // Retrieve Label if available
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

    // Method to check for drive_meta.json and extract the Label if it exists
    private string? GetPartitionLabel(string? uuid)
    {
        if (string.IsNullOrEmpty(uuid)) return null;

        var filePath = $"/mnt/backy/{uuid}/drive_meta.json";
        if (System.IO.File.Exists(filePath))
        {
            try
            {
                var jsonContent = System.IO.File.ReadAllText(filePath);
                var metadata = JsonSerializer.Deserialize<DriveMetadata>(jsonContent);

                if (metadata != null && !string.IsNullOrEmpty(metadata.Label))
                {
                    Console.WriteLine($"Found label for UUID {uuid}: {metadata.Label}");
                    return metadata.Label;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading metadata for UUID {uuid}: {ex.Message}");
            }
        }

        return null; // Return null if no file found or error occurred
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

    public class DriveMetadata
    {
        public string? Label { get; set; } = null;
        public string? Serial { get; set; } = null;
        public string? UUID { get; set; } = null;
        public string? Vendor { get; set; } = null;
        public string? Model { get; set; } = null;
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

