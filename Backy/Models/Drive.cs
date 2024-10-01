using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Backy.Models
{
    public class Drive
    {
        [Key]
        public int Id { get; set; }

        public string? Name { get; set; }

        public string? Label { get; set; }

        public string Serial { get; set; } = "No Serial";

        public string UUID { get; set; } = "No UUID";

        public string Vendor { get; set; } = "Unknown Vendor";

        public string Model { get; set; } = "Unknown Model";

        public long Size { get; set; } = 0;

        public long PartitionSize { get; set; } = 0;

        public bool IsConnected { get; set; } = false;

        public bool IsMounted { get; set; } = false;

        public string IdLink { get; set; } = string.Empty;

        public int? PoolGroupId { get; set; }

        public PoolGroup? PoolGroup { get; set; }

        public List<PartitionInfo> Partitions { get; set; } = new List<PartitionInfo>();
    }

    // Models and DTOs
    public class DiskArrayResult
    {
        [JsonPropertyName("discarray")]
        public List<DiskInfo> Discarray { get; set; } = new List<DiskInfo>();
    }

    public class KillProcessesRequest
    {
        [JsonPropertyName("poolGroupId")]
        public int PoolGroupId { get; set; }

        [JsonPropertyName("pids")]
        public List<int> Pids { get; set; } = new List<int>();
    }

    public class DiskInfo
    {
        [JsonPropertyName("mount")]
        public string Mount { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public string Size { get; set; } = "0";

        [JsonPropertyName("used")]
        public string Used { get; set; } = "0";

        [JsonPropertyName("avail")]
        public string Avail { get; set; } = "0";

        [JsonPropertyName("use%")]
        public string UsePercent { get; set; } = "0%";
    }

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