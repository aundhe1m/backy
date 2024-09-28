using System.Text.Json.Serialization;

namespace Backy.Models
{
    public class FileExplorerModel
    {
        public Guid StorageId { get; set; } // Changed from int to Guid
        public string CurrentPath { get; set; } = string.Empty;
        public List<FileEntry> Files { get; set; } = new List<FileEntry>();
        public List<DirectoryDto> Directories { get; set; } = new List<DirectoryDto>();

        // Added properties
        public string navPath { get; set; } = string.Empty;
        public string remotePath { get; set; } = string.Empty;
        public DirectoryNode DirectoryTree { get; set; } = new DirectoryNode();
    }

    public class DirectoryNode
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("fullPath")]
        public string FullPath { get; set; } = string.Empty;

        [JsonPropertyName("children")]
        public List<DirectoryNode> Children { get; set; } = new List<DirectoryNode>();
    }

    public class SearchResultItem
    {
        public string Type { get; set; } = string.Empty; // "File" or "Directory"
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string NavPath { get; set; } = string.Empty;
    }

    public class DirectoryDto
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
    }
}