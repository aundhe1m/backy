// File: Backy/Models/PoolGroup.cs

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Backy.Models
{
    public class PoolGroup
    {
        [Key]
        public int PoolGroupId { get; set; }

        [Required]
        public Guid PoolGroupGuid { get; set; } = Guid.NewGuid();

        public string GroupLabel { get; set; } = "Unnamed Group";

        public bool PoolEnabled { get; set; } = true;

        public bool AllDrivesConnected { get; set; } = true;

        public string MountPath { get; set; } = string.Empty;

        public long Size { get; set; } = 0;

        public long Used { get; set; } = 0;

        public long Available { get; set; } = 0;

        public string UsePercent { get; set; } = "0%";

        public List<PoolDrive> Drives { get; set; } = new List<PoolDrive>();
    }
}
