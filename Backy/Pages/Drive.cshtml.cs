using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
using System.IO;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using static Backy.Pages.MountModel;

namespace Backy.Pages;

public class DriveModel : PageModel
{
    private readonly ILogger<DriveModel> _logger;
    private static string mountDirectory = "/mnt/backy";
    private static string persistentFilePath = Path.Combine(mountDirectory, "drives.json");

    public List<MirrorGroup> MirrorGroups { get; set; } = new List<MirrorGroup>(); // For mirror groups
    public List<DriveMetaData> StandaloneDrives { get; set; } = new List<DriveMetaData>(); // For standalone drives

    public DriveModel(ILogger<DriveModel> logger)
    {
        _logger = logger;
    }

    public void OnGet()
    {
        _logger.LogInformation("Starting OnGet...");

        // Load persistent data
        var persistentData = LoadPersistentData();
        _logger.LogInformation($"Loaded Persistent Data: {JsonSerializer.Serialize(persistentData)}");

        // Organize drives into MirrorGroups and StandaloneDrives
        OrganizeDrives(persistentData);

        // Save the updated data back to persistent storage
        SavePersistentData(persistentData);

        _logger.LogInformation("OnGet completed.");
    }

    private PersistentData LoadPersistentData()
    {
        try
        {
            if (System.IO.File.Exists(persistentFilePath))
            {
                _logger.LogInformation($"Loading persistent data from {persistentFilePath}");
                var json = System.IO.File.ReadAllText(persistentFilePath);
                var persistentData = JsonSerializer.Deserialize<PersistentData>(json) ?? new PersistentData();
                return persistentData;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error loading persistent data: {ex.Message}");
        }

        return new PersistentData(); // Return empty PersistentData if loading fails
    }

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

    private void OrganizeDrives(PersistentData persistentData)
    {
        _logger.LogInformation("Organizing drives...");

        // Update active drives
        var activeDrives = UpdateActiveDrives(persistentData.Drives);

        // Organize drives into MirrorGroups and StandaloneDrives
        MirrorGroups = persistentData.Mirrors;
        StandaloneDrives = activeDrives.Where(d => string.IsNullOrEmpty(d.GroupId)).ToList();

        // Update persistentData with the new active drives
        persistentData.Drives = activeDrives;

        _logger.LogInformation($"Drives organized. MirrorGroups: {MirrorGroups.Count}, StandaloneDrives: {StandaloneDrives.Count}");
    }

    private List<DriveMetaData> UpdateActiveDrives(List<DriveMetaData> persistentDrives)
    {
        var activeDrives = new List<DriveMetaData>();
        _logger.LogInformation("Updating active drives...");

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

            _logger.LogInformation($"lsblk output: {jsonOutput}");

            var lsblkOutput = JsonSerializer.Deserialize<LsblkOutput>(jsonOutput);
            if (lsblkOutput?.Blockdevices != null)
            {
                foreach (var device in lsblkOutput.Blockdevices)
                {
                    // Skip sda and its children
                    if (device.Name == "sda") continue;

                    if (device.Type == "disk" && device.Children != null)
                    {
                        foreach (var partition in device.Children)
                        {
                            if (partition.Mountpoint != null && partition.Mountpoint.StartsWith(mountDirectory))
                            {
                                if (!string.IsNullOrEmpty(partition.Uuid))
                                {
                                    var uuid = partition.Uuid;

                                    // Update or create drive metadata
                                    var driveData = persistentDrives.FirstOrDefault(d => d.UUID == uuid) ?? new DriveMetaData
                                    {
                                        UUID = uuid,
                                        Serial = device.Serial ?? "No Serial", // Serial from parent
                                        Vendor = device.Vendor ?? "Unknown Vendor", // Vendor from parent
                                        Model = device.Model ?? "Unknown Model",
                                        Label = null
                                    };

                                    // Update active state
                                    driveData.IsConnected = true;
                                    driveData.PartitionSize = partition.Size ?? 0;
                                    driveData.UsedSpace = GetUsedSpace(uuid);
                                    activeDrives.Add(driveData);

                                    _logger.LogInformation($"Active drive added: {JsonSerializer.Serialize(driveData)}");
                                }
                            }
                        }
                    }
                }
            }

            // Add remaining disconnected drives from persistent data
            foreach (var persistentDrive in persistentDrives)
            {
                if (!activeDrives.Any(d => d.UUID == persistentDrive.UUID))
                {
                    persistentDrive.IsConnected = false;
                    activeDrives.Add(persistentDrive);
                    _logger.LogInformation($"Disconnected drive added: {JsonSerializer.Serialize(persistentDrive)}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error updating active drives: {ex.Message}");
        }

        return activeDrives;
    }

    public IActionResult OnPostToggleBackupDest(string uuid, bool isEnabled)
    {
        // Locate the drive by UUID in persistent data
        var persistentData = LoadPersistentData();
        var drive = persistentData.Drives.FirstOrDefault(d => d.UUID == uuid);

        if (drive != null)
        {

            if (drive.IsConnected || !isEnabled)
            {
                drive.BackupDestEnabled = isEnabled;
                SavePersistentData(persistentData); // Save changes

                return new JsonResult(new { success = true, message = "Backup destination updated." });
            }

            return new JsonResult(new { success = false, message = "Cannot enable backup for disconnected drive." });
        }

        return new JsonResult(new { success = false, message = "Drive not found." });
    }

    public IActionResult OnPostRemoveDrive(string uuid)
    {
        var persistentData = LoadPersistentData();
        var drive = persistentData.Drives.FirstOrDefault(d => d.UUID == uuid);

        if (drive != null)
        {
            // Remove the drive from the persistent data
            persistentData.Drives.Remove(drive);

            // Check if the drive belongs to a mirror group and remove it
            if (!string.IsNullOrEmpty(drive.GroupId))
            {
                var mirrorGroup = persistentData.Mirrors.FirstOrDefault(mg => mg.GroupId == drive.GroupId);
                if (mirrorGroup != null)
                {
                    // Remove the drive from the mirror group
                    mirrorGroup.Drives.Remove(drive);

                    // If the mirror group has no more drives, remove the mirror group itself
                    if (!mirrorGroup.Drives.Any())
                    {
                        persistentData.Mirrors.Remove(mirrorGroup);
                        _logger.LogInformation($"Removed empty mirror group with ID: {mirrorGroup.GroupId}");
                    }
                }
            }

            SavePersistentData(persistentData); // Save the updated data
            _logger.LogInformation($"Drive with UUID: {uuid} removed successfully.");
            return new JsonResult(new { success = true, message = "Drive removed successfully." });
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

    public IActionResult OnPostRenameDriveLabel(string uuid, string newLabel)
    {
        var persistentData = LoadPersistentData();
        var drive = persistentData.Drives.FirstOrDefault(d => d.UUID == uuid);

        if (drive != null)
        {
            drive.Label = newLabel; // Update the label
            SavePersistentData(persistentData); // Save changes

            return new JsonResult(new { success = true, message = "Label updated successfully." });
        }

        return new JsonResult(new { success = false, message = "Drive not found." });
    }



    public class ToggleBackupDestRequest
    {
        public required string Uuid { get; set; }
        public bool IsEnabled { get; set; }
    }

    // Classes for handling persistent data
    public class PersistentData
    {
        public List<MirrorGroup> Mirrors { get; set; } = new List<MirrorGroup>();
        public List<DriveMetaData> Drives { get; set; } = new List<DriveMetaData>();
    }

    public class MirrorGroup
    {
        public string GroupId { get; set; } = Guid.NewGuid().ToString(); // Set default unique value
        public string GroupLabel { get; set; } = "Unnamed Group"; // Set default value
        public List<DriveMetaData> Drives { get; set; } = new List<DriveMetaData>();
    }

    public class DriveMetaData
    {
        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("serial")]
        public string Serial { get; set; } = "No Serial";

        [JsonPropertyName("uuid")]
        public string UUID { get; set; } = "No UUID";

        [JsonPropertyName("vendor")]
        public string Vendor { get; set; } = "Unknown Vendor";

        [JsonPropertyName("model")]
        public string Model { get; set; } = "Unknown Model";

        [JsonPropertyName("partition_size")]
        public long PartitionSize { get; set; } = 0;

        [JsonPropertyName("used_space")]
        public long UsedSpace { get; set; } = 0;

        [JsonPropertyName("is_connected")]
        public bool IsConnected { get; set; } = false;

        [JsonPropertyName("backup_dest_enabled")]
        public bool BackupDestEnabled { get; set; } = false;

        [JsonPropertyName("group_id")]
        public string? GroupId { get; set; } // Indicates if this drive is part of a mirror
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
}
