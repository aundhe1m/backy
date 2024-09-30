using System.ComponentModel.DataAnnotations;

namespace Backy.Models
{
    public class ProtectedDrive
    {
        [Key]
        public int Id { get; set; }
        public string Serial { get; set; } = "No Serial";
        public string Vendor { get; set; } = "Unknown Vendor";
        public string Model { get; set; } = "Unknown Model";
        public string? Name { get; set; }
        public string? Label { get; set; }
    }
}
