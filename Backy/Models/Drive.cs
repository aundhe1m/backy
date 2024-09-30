using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

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
        public long PartitionSize { get; set; } = 0;
        public long UsedSpace { get; set; } = 0;
        public bool IsConnected { get; set; } = false;
        public bool IsMounted { get; set; } = false;
        public string IdLink { get; set; } = string.Empty;

        // Navigation properties
        public int? PoolGroupId { get; set; }
        public PoolGroup? PoolGroup { get; set; }

        public List<PartitionInfo> Partitions { get; set; } = new List<PartitionInfo>();
    }
}
