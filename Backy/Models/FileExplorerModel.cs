namespace Backy.Models
{
    public class FileExplorerModel
    {
        public int StorageId { get; set; }
        public string CurrentPath { get; set; } = string.Empty;
        public List<FileEntry> Files { get; set; } = new List<FileEntry>();
        public List<string> Directories { get; set; } = new List<string>();
    }
}