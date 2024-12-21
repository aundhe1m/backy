using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backy.Models
{
    public class RemoteFile
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public Guid RemoteConnectionId { get; set; }

        [Required]
        [Column(TypeName = "TEXT")]
        public string FileName { get; set; } = string.Empty;

        [Required]
        [Column(TypeName = "VARCHAR(512)")]
        public string RelativePath { get; set; } = string.Empty;

        public long Size { get; set; }

        public bool BackupExists { get; set; } = false;

        public string BackupPoolGroup { get; set; } = string.Empty;

        public string BackupDriveSerials { get; set; } = string.Empty;

        public bool IsDeleted { get; set; } = false;
        public bool IsExcluded { get; set; } = false;

        // Navigation property
        public RemoteConnection RemoteConnection { get; set; } = default!;
    }
}
