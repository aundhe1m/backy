using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
using System.IO;
using System.Diagnostics;

namespace Backy.Pages
{
    public class DriveModel : PageModel
    {
        private readonly ILogger<DriveModel> _logger;
        private static string metaDirectory = "/mnt/backy";
        private static string persistentFilePath = "/mnt/backy/drives.json";

        public List<DriveMetaData> Drives { get; set; } = new List<DriveMetaData>();

        public DriveModel(ILogger<DriveModel> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {
            // Load persistent data first
            var persistentDrives = LoadPersistentData();

            // Merge with active data from drive_meta.json
            Drives = LoadDrivesFromMeta(persistentDrives);

            // Save the merged data to persistent JSON file
            SavePersistentData(Drives);
        }

        private List<DriveMetaData> LoadDrivesFromMeta(Dictionary<string, DriveMetaData> persistentDrives)
        {
            var activeDrives = new List<DriveMetaData>();

            try
            {
                // Find all directories in /mnt/backy and check for drive_meta.json files
                var directories = Directory.GetDirectories(metaDirectory);
                foreach (var dir in directories)
                {
                    var metaFilePath = Path.Combine(dir, "drive_meta.json");

                    if (System.IO.File.Exists(metaFilePath))
                    {
                        var jsonData = System.IO.File.ReadAllText(metaFilePath);
                        var driveData = JsonSerializer.Deserialize<DriveMetaData>(jsonData);

                        // Only add drive if valid metadata is found
                        if (driveData != null && !string.IsNullOrEmpty(driveData.UUID))
                        {
                            driveData.IsConnected = true; // Drive is connected
                            driveData.PartitionSize = GetPartitionSize(driveData.UUID);
                            driveData.UsedSpace = GetUsedSpace(driveData.UUID);
                            activeDrives.Add(driveData);

                            // Remove the drive from persistent list, as it is actively connected now
                            persistentDrives.Remove(driveData.UUID);

                            _logger.LogInformation($"Loaded connected drive: {driveData.Label} with UUID: {driveData.UUID}");
                        }
                    }
                }

                // Add remaining drives from persistent data as disconnected
                foreach (var persistentDrive in persistentDrives.Values)
                {
                    persistentDrive.IsConnected = false; // Mark as disconnected
                    activeDrives.Add(persistentDrive);
                    _logger.LogInformation($"Loaded disconnected drive: {persistentDrive.Label} with UUID: {persistentDrive.UUID}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading drives: {ex.Message}");
            }

            return activeDrives;
        }

        private long GetPartitionSize(string uuid)
        {
            try
            {
                // Use lsblk to get partition size based on UUID
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "bash",
                        Arguments = $"-c \"lsblk -b -o SIZE -n /dev/disk/by-uuid/{uuid}\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                string result = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                return long.TryParse(result, out long size) ? size : 0;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting partition size for UUID {uuid}: {ex.Message}");
                return 0;
            }
        }

        private long GetUsedSpace(string uuid)
        {
            try
            {
                // Use df to get used space based on UUID
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

                    // Only add drives with a valid, non-null UUID to the dictionary
                    return persistentDrives
                        .Where(d => !string.IsNullOrEmpty(d.UUID))  // Ensure UUID is not null or empty
                        .ToDictionary(d => d.UUID!);  // The `!` operator asserts that UUID is non-null here
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
                _logger.LogInformation("Successfully saved persistent drive data.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error saving persistent data: {ex.Message}");
            }
        }

        public class DriveMetaData
        {
            public string? Label { get; set; } = "No Label"; // Nullable with default
            public string? Serial { get; set; } = "No Serial"; // Nullable with default
            public string? UUID { get; set; } = "No UUID"; // Nullable with default
            public string? Vendor { get; set; } = "Unknown Vendor"; // Nullable with default
            public string? Model { get; set; } = "Unknown Model"; // Nullable with default
            public long PartitionSize { get; set; } = 0;
            public long UsedSpace { get; set; } = 0;
            public bool IsConnected { get; set; } = false;
        }
    }
}
