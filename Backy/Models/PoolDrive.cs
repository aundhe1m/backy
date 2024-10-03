// File: Backy/Models/PoolDrive.cs

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backy.Models
{
    public class PoolDrive
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public Guid DriveGuid { get; set; } = Guid.NewGuid();

        public string Label { get; set; } = string.Empty;

        public string Serial { get; set; } = "No Serial";

        public string UUID { get; set; } = "No UUID";

        public string Vendor { get; set; } = "Unknown Vendor";

        public string Model { get; set; } = "Unknown Model";

        public long Size { get; set; } = 0;

        public bool IsConnected { get; set; } = false;

        public bool IsMounted { get; set; } = false;

        public string DevPath { get; set; } = string.Empty; // Renamed from IdLink

        // Foreign key to PoolGroup
        public Guid? PoolGroupGuid { get; set; }

        [ForeignKey(nameof(PoolGroupGuid))]
        public PoolGroup? PoolGroup { get; set; }
    }
}
