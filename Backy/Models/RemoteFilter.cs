using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backy.Models
{
    public class RemoteFilter
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public Guid RemoteConnectionId { get; set; }

        [Required]
        public string Pattern { get; set; } = string.Empty;

        [Required]
        public bool IsInclude { get; set; } // true for include, false for exclude

        public int FilteredFileCount { get; set; } = 0;

        // Navigation property
        public RemoteConnection RemoteConnection { get; set; } = default!;
    }
}
