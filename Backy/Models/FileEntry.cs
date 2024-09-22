using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backy.Models
{
    public class FileEntry
    {
        public int Id { get; set; }

        [Required]
        public int RemoteStorageId { get; set; }

        [ForeignKey("RemoteStorageId")]
        public RemoteStorage RemoteStorage { get; set; } = new RemoteStorage();

        [Required]
        public string FileName { get; set; } = string.Empty;

        [Required]
        public string FullPath { get; set; } = string.Empty;

        public long Size { get; set; }

        public DateTime LastModified { get; set; }

        public string Checksum { get; set; } = string.Empty;

        public bool BackupExists { get; set; } = false;

        public string BackupPoolGroup { get; set; } = string.Empty;

        public string BackupDriveSerials { get; set; } = string.Empty; // JSON array or serialized list

        public DateTime? BackupDate { get; set; }

        public bool IsDeleted { get; set; } = false;
    }
}
