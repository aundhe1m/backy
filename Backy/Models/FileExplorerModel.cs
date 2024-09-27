namespace Backy.Models
{
    public class FileExplorerModel
    {
        public Guid StorageId { get; set; } // Changed from int to Guid
        public string CurrentPath { get; set; } = string.Empty;
        public List<FileEntry> Files { get; set; } = new List<FileEntry>();
        public List<string> Directories { get; set; } = new List<string>();
    }

    public class DirectoryNode
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public List<DirectoryNode> Children { get; set; } = new List<DirectoryNode>();
    }

    public class SearchResultItem
    {
        public string Type { get; set; } = string.Empty; // "File" or "Directory"
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string NavPath { get; set; } = string.Empty;
    }

    public class DirectoryInfoDto
    {
        public string FullPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
    }
}
