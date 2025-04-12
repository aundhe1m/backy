using System.Text.Json.Serialization;

namespace Backy.Models
{
    /// <summary>
    /// Represents the response from the Agent API for pool details
    /// </summary>
    public class PoolDetailResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = "Unknown";

        [JsonPropertyName("size")]
        public long Size { get; set; } = 0;

        [JsonPropertyName("used")]
        public long Used { get; set; } = 0;

        [JsonPropertyName("available")]
        public long Available { get; set; } = 0;

        [JsonPropertyName("usePercent")]
        public string UsePercent { get; set; } = "0%";

        [JsonPropertyName("mountPath")]
        public string MountPath { get; set; } = string.Empty;

        [JsonPropertyName("drives")]
        public List<DriveDetailInfo>? Drives { get; set; }
    }

    /// <summary>
    /// Represents drive details in the pool details response
    /// </summary>
    public class DriveDetailInfo
    {
        [JsonPropertyName("serial")]
        public string? Serial { get; set; }

        [JsonPropertyName("vendor")]
        public string? Vendor { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("path")]
        public string? Path { get; set; }
    }
}