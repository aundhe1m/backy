using System.Text.Json.Serialization;

namespace Backy.Models
{
    public class PartitionInfo
    {
        public string? Name { get; set; }

        public string? UUID { get; set; }

        public string? Fstype { get; set; }

        public string? MountPoint { get; set; }

        public long Size { get; set; }

        public string? Type { get; set; }

        public string? Path { get; set; }
    }
}
