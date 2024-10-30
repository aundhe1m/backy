using System.ComponentModel.DataAnnotations;

namespace Backy.Models
{
    public class RemoteFilter
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public Guid RemoteConnectionId { get; set; }

        [Required]
        public string Pattern { get; set; } = string.Empty;

        [Required]
        public bool IsInclude { get; set; } // true for include, false for exclude

        // Navigation property
        public RemoteConnection RemoteConnection { get; set; } = default!;
    }
}
