using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Backy.Models
{
    public class PoolGroup
    {
        [Key]
        public int PoolGroupId { get; set; }  // EF will make it the primary key
        public string GroupLabel { get; set; } = "Unnamed Group";
        public bool PoolEnabled { get; set; } = true;
        public string MountPath { get; set; } = string.Empty;

        // Updated properties
        public long Size { get; set; } = 0;          // Total size in bytes
        public long Used { get; set; } = 0;          // Used space in bytes
        public long Available { get; set; } = 0;     // Available space in bytes
        public string UsePercent { get; set; } = "0%"; // Usage percentage

        public List<Drive> Drives { get; set; } = new List<Drive>();
    }
}