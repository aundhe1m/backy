using System.Collections.Generic;

namespace Backy.Models
{
    public class DirectoryNodeModel
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool IsExpanded { get; set; } = false;
        public bool IsLoading { get; set; } = false;
        public List<DirectoryNodeModel> Children { get; set; } = new List<DirectoryNodeModel>();
    }
}