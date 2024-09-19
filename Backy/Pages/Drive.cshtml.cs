using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
using System.IO;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace Backy.Pages;

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

    private List<DriveMetaData> UpdateActiveDrives()
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
                            // Check if the partition is mounted under /mnt/backy/ and has a UUID
                            if (partition.Mountpoint != null && partition.Mountpoint.StartsWith(mountDirectory) && !string.IsNullOrEmpty(partition.Uuid))
                            {
                                var driveData = new DriveMetaData
                                {
                                    UUID = partition.Uuid,
                                    Serial = device.Serial ?? "No Serial",
                                    Vendor = device.Vendor ?? "Unknown Vendor",
                                    Model = device.Model ?? "Unknown Model",
                                    PartitionSize = partition.Size ?? 0,
                                    UsedSpace = GetUsedSpace(partition.Uuid),
                                    IsConnected = true
                                };

                                activeDrives.Add(driveData);
                                _logger.LogInformation($"Active drive added: {JsonSerializer.Serialize(driveData)}");
                            }
                        }
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

    public async Task<IActionResult> OnPostCreatePool()
    {
        _logger.LogInformation($"OnPostCreatePool called.");
        // Read the request body manually
        string requestBody = await new StreamReader(Request.Body).ReadToEndAsync();
        var request = JsonSerializer.Deserialize<CreatePoolRequest>(requestBody);

        if (request == null || string.IsNullOrEmpty(request.PoolLabel) || request.Uuids == null || !request.Uuids.Any())
        {
            return BadRequest(new { success = false, message = "Pool Label and at least one drive must be selected" });
        }

        _logger.LogInformation($"Creating pool with label: {request.PoolLabel} and drives: {string.Join(",", request.Uuids)}");

        var persistentData = LoadPersistentData();
        var newPoolGroup = new PoolGroup
        {
            PoolGroupId = Guid.NewGuid().ToString(),
            GroupLabel = request.PoolLabel,
            Drives = new List<DriveMetaData>()
        };

        // Get the active drives
        var activeDrives = UpdateActiveDrives();
        int driveIndex = 1;

        foreach (var uuid in request.Uuids)
        {
            // Find the drive in activeDrives
            var drive = activeDrives.FirstOrDefault(d => d.UUID == uuid);
            if (drive != null)
            {
                drive.Label = $"{request.PoolLabel}-{driveIndex++}";
                newPoolGroup.Drives.Add(drive);
                _logger.LogInformation($"Drive {drive.UUID} added to pool {request.PoolLabel}");
            }
            else
            {
                _logger.LogWarning($"Drive with UUID {uuid} not found among active drives.");
            }
        }

        persistentData.Pools.Add(newPoolGroup);
        SavePersistentData(persistentData);

        return new JsonResult(new { success = true, message = $"Pool '{request.PoolLabel}' created successfully." });
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
}




public class CreatePoolRequest
{
    public required string PoolLabel { get; set; }
    public required List<string> Uuids { get; set; }
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
