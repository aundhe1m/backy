using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
using System.IO;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;


namespace Backy.Pages;

public class DriveModel : PageModel
{
    private readonly ILogger<DriveModel> _logger;
    private static string mountDirectory = "/mnt/backy";
    private static string persistentFilePath = Path.Combine(mountDirectory, "drives.json");

    public List<DriveMetaData> Drives { get; set; } = new List<DriveMetaData>();

    public DriveModel(ILogger<DriveModel> logger)
    {
        _logger = logger;
    }

    public void OnGet()
    {
        // Load persistent data first (includes history of drives)
        var persistentDrives = LoadPersistentData();

        // Merge persistent data with active mounted drives
        Drives = UpdateActiveDrives(persistentDrives);

        // Save the updated data back to persistent storage
        SavePersistentData(Drives);
    }

    private List<DriveMetaData> UpdateActiveDrives(Dictionary<string, DriveMetaData> persistentDrives)
    {
        var activeDrives = new List<DriveMetaData>();

        try
        {
            // Use lsblk to find drives mounted at /mnt/backy/*
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bash",
                    Arguments = $"-c \"lsblk -J -b -o NAME,SIZE,TYPE,MOUNTPOINT,UUID,SERIAL,VENDOR,MODEL\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
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
                    // Skip sda and its children
                    if (device.Name == "sda")
                    {
                        continue;
                    }

                    if (device.Type == "disk" && device.Children != null)
                    {
                        foreach (var partition in device.Children)
                        {
                            if (partition.Mountpoint != null && partition.Mountpoint.StartsWith(mountDirectory))
                            {
                                // Drive is mounted under /mnt/backy/, hence active
                                if (!string.IsNullOrEmpty(partition.Uuid))
                                {
                                    var uuid = partition.Uuid;

                                    // Check if the drive is already in persistent data
                                    var driveData = persistentDrives.ContainsKey(uuid) ? persistentDrives[uuid] : new DriveMetaData
                                    {
                                        UUID = uuid,
                                        Serial = device.Serial ?? "No Serial",  // Serial from parent
                                        Vendor = device.Vendor ?? "Unknown Vendor",  // Vendor from parent
                                        Model = device.Model ?? "Unknown Model",    // Model from parent
                                        Label = null
                                    };

                                    // Update drive's current state
                                    driveData.IsConnected = true;
                                    driveData.PartitionSize = partition.Size ?? 0;  // Use the partition size from child
                                    driveData.UsedSpace = GetUsedSpace(uuid);  // Keep the logic to fetch used space
                                    activeDrives.Add(driveData);

                                    // Remove from persistent list to avoid duplication
                                    persistentDrives.Remove(uuid);

                                    _logger.LogInformation($"Loaded connected drive: {driveData.Label} with UUID: {driveData.UUID}");
                                }
                            }
                        }
                    }
                }
            }

            // Add remaining disconnected drives from persistent data
            foreach (var persistentDrive in persistentDrives.Values)
            {
                persistentDrive.IsConnected = false;
                activeDrives.Add(persistentDrive);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error updating active drives: {ex.Message}");
        }

        return activeDrives;
    }

    public IActionResult OnPostRenameDriveLabel(string uuid, string newLabel)
    {
        // Load persistent data
        var persistentDrives = LoadPersistentData();

        // Find the drive by UUID
        if (persistentDrives.ContainsKey(uuid))
        {
            var drive = persistentDrives[uuid];
            drive.Label = newLabel;  // Update the label

            // Save the updated data
            SavePersistentData(persistentDrives.Values.ToList());

            return new JsonResult(new { success = true, message = "Label updated successfully." });
        }

        return new JsonResult(new { success = false, message = "Drive not found." });
    }

    private long GetUsedSpace(string uuid)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bash",
                    Arguments = $"-c \"df -B1 | grep /mnt/backy/{uuid} | awk '{{print $3}}'\"",
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
            _logger.LogError($"Error getting used space for UUID {uuid}: {ex.Message}");
            return 0;
        }
    }

    private Dictionary<string, DriveMetaData> LoadPersistentData()
    {
        try
        {
            if (System.IO.File.Exists(persistentFilePath))
            {
                var json = System.IO.File.ReadAllText(persistentFilePath);
                var persistentDrives = JsonSerializer.Deserialize<List<DriveMetaData>>(json) ?? new List<DriveMetaData>();

                return persistentDrives
                    .Where(d => !string.IsNullOrEmpty(d.UUID))
                    .ToDictionary(d => d.UUID!);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error loading persistent data: {ex.Message}");
        }

        return new Dictionary<string, DriveMetaData>();
    }

    private void SavePersistentData(List<DriveMetaData> drives)
    {
        try
        {
            var json = JsonSerializer.Serialize(drives, new JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(persistentFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error saving persistent data: {ex.Message}");
        }
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

        [JsonPropertyName("children")]
        public List<BlockDevice>? Children { get; set; }
    }
    public class DriveMetaData
    {
        public string? Label { get; set; }
        public string Serial { get; set; } = "No Serial";
        public string UUID { get; set; } = "No UUID";
        public string Vendor { get; set; } = "Unknown Vendor";
        public string Model { get; set; } = "Unknown Model";
        public long PartitionSize { get; set; } = 0;
        public long UsedSpace { get; set; } = 0;
        public bool IsConnected { get; set; } = false;
        public bool BackupDestEnabled { get; set; } = false;
    }
}

