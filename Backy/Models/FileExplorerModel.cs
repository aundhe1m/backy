using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Backy.Models
{
    public class FileExplorerModel
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("storageContent")]
        public StorageContentItem StorageContent { get; set; } = new StorageContentItem();
    }

    public class StorageContentItem
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("fullPath")]
        public string FullPath { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty; // "directory" or "file"

        [JsonPropertyName("backupExists")]
        public bool BackupExists { get; set; }

        [JsonPropertyName("children")]
        public List<StorageContentItem> Children { get; set; } = new List<StorageContentItem>();
    }



    public class SearchResultItem
    {
        public string Type { get; set; } = string.Empty; // "File" or "Directory"
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string NavPath { get; set; } = string.Empty;
    }
}
