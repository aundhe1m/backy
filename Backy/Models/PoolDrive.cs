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

        public required string Label { get; set; }

        public required string Serial { get; set; }

        public required string Vendor { get; set; }

        public required string Model { get; set; }

        public required long Size { get; set; }

        public bool IsConnected { get; set; } = false;

        public bool IsMounted { get; set; } = false;

        public required string DevPath { get; set; }

        public Guid PoolGroupGuid { get; set; }

        [ForeignKey(nameof(PoolGroupGuid))]
        public PoolGroup? PoolGroup { get; set; }
    }
}
