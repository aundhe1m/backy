using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backy.Models
{
    public class StorageContent
    {
        [Key]
        public Guid RemoteScanId { get; set; }

        [ForeignKey("RemoteScanId")]
        public RemoteScan? RemoteScan { get; set; }

        [Column(TypeName = "LONGTEXT")] // Use appropriate type based on your database
        public string ContentJson { get; set; } = string.Empty;

        public DateTime LastUpdated { get; set; }
    }
}
