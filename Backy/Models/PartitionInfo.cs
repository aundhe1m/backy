using System.ComponentModel.DataAnnotations;

namespace Backy.Models
{
    public class PartitionInfo
    {
        [Key]
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string UUID { get; set; } = "No UUID";

        public string MountPoint { get; set; } = "Not Mounted";

        public long Size { get; set; } = 0;

        public long UsedSpace { get; set; } = 0;

        public string Fstype { get; set; } = "Unknown";

        public int DriveId { get; set; }

        public Drive? Drive { get; set; }
    }
}
