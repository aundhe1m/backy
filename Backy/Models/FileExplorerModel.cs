namespace Backy.Models
{
    public class FileExplorerModel
    {
        public int StorageId { get; set; }
        public string CurrentPath { get; set; } = string.Empty;
        public List<FileEntry> Files { get; set; } = new List<FileEntry>();
        public List<string> Directories { get; set; } = new List<string>();
    }

    public class DirectoryNode
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public List<DirectoryNode> Children { get; set; } = new List<DirectoryNode>();
    }
}