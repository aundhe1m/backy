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

        [Column(TypeName = "TEXT")]
        public string ContentJson { get; set; } = string.Empty;

        public DateTimeOffset LastUpdated { get; set; }
    }
}
