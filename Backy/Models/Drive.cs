using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Backy.Models
{
    public class Drive
    {
        public string? Name { get; set; }

        public string? Label { get; set; }

        public required string Serial { get; set; }

        public string Vendor { get; set; } = "Unknown Vendor";

        public string Model { get; set; } = "Unknown Model";

        public long Size { get; set; } = 0;

        public bool IsConnected { get; set; } = false;

        public bool IsMounted { get; set; } = false;

        public string IdLink { get; set; } = string.Empty;

        public List<PartitionInfo> Partitions { get; set; } = new List<PartitionInfo>();
    }

    // Models and DTOs
    public class CreatePoolRequest
    {
        public required string PoolLabel { get; set; }

        public required List<string> DriveSerials { get; set; }

        public Dictionary<string, string> DriveLabels { get; set; } =
            new Dictionary<string, string>();
    }

    public class RenamePoolResponse
    {
        public bool Success { get; set; }
        public required string Message { get; set; }
        public required string NewPoolLabel { get; set; }
        public Dictionary<int, string> UpdatedDriveLabels { get; set; } =
            new Dictionary<int, string>();
    }

    public class RenamePoolRequest
    {
        public Guid PoolGroupGuid { get; set; }
        public required string NewPoolLabel { get; set; }
        public required List<DriveLabel> DriveLabels { get; set; }
    }

    public class DriveLabel
    {
        public int DriveId { get; set; }
        public string? Label { get; set; }
    }


    public class KillProcessesRequest
    {
        public Guid PoolGroupGuid { get; set; }
        public List<int> Pids { get; set; } = new List<int>();
        public string Action { get; set; } = string.Empty;
    }

    public class LsblkOutput
    {
        [JsonPropertyName("blockdevices")]
        public List<BlockDevice>? Blockdevices { get; set; }
    }

    public class ProcessInfo
    {
        [JsonPropertyName("command")]
        public string? Command { get; set; }

        [JsonPropertyName("pid")]
        public int PID { get; set; }

        [JsonPropertyName("user")]
        public string? User { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
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

        [JsonPropertyName("id-link")]
        public string? IdLink { get; set; }

        [JsonPropertyName("children")]
        public List<BlockDevice>? Children { get; set; }
    }
}
