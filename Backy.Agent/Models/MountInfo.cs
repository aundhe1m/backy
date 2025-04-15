using System.Text.Json.Serialization;

namespace Backy.Agent.Models
{
    /// <summary>
    /// Represents information about a mounted filesystem
    /// </summary>
    public class MountInfo
    {
        /// <summary>
        /// The device path (e.g. /dev/md0)
        /// </summary>
        [JsonPropertyName("device")]
        public string Device { get; set; } = string.Empty;
        
        /// <summary>
        /// The mount point path
        /// </summary>
        [JsonPropertyName("mountPoint")]
        public string MountPoint { get; set; } = string.Empty;
        
        /// <summary>
        /// The filesystem type (e.g. ext4, xfs)
        /// </summary>
        [JsonPropertyName("filesystemType")]
        public string FilesystemType { get; set; } = string.Empty;
        
        /// <summary>
        /// The mount options
        /// </summary>
        [JsonPropertyName("options")]
        public string Options { get; set; } = string.Empty;
    }
}