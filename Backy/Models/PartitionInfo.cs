// File: Backy/Models/PartitionInfo.cs

namespace Backy.Models
{
    public class PartitionInfo
    {
        public string Name { get; set; } = string.Empty;

        public string UUID { get; set; } = "No UUID";

        public string MountPoint { get; set; } = "Not Mounted";

        public long Size { get; set; } = 0;

        public long UsedSpace { get; set; } = 0;

        public string Fstype { get; set; } = "Unknown";
    }
}
