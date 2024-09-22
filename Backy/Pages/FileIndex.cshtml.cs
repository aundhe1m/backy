using Backy.Data;
using Backy.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Backy.Pages
{
    public class FileIndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<FileIndexModel> _logger;

        public FileIndexModel(ApplicationDbContext context, ILogger<FileIndexModel> logger)
        {
            _context = context;
            _logger = logger;
        }

        [BindProperty]
        public int? SelectedStorageId { get; set; }

        public RemoteStorage? SelectedStorage { get; set; }

        public List<SelectListItem> StorageOptions { get; set; } = new List<SelectListItem>();

        public string FileTreeHtml { get; set; } = string.Empty;

        public long TotalSize { get; set; }
        public int TotalFiles { get; set; }
        public int BackupCount { get; set; }
        public double BackupPercentage { get; set; }

        public async Task OnGetAsync()
        {
            await LoadStorageOptions();

            if (SelectedStorageId.HasValue)
            {
                await LoadStorageData(SelectedStorageId.Value);
            }
        }

        public async Task OnPostAsync()
        {
            await LoadStorageOptions();

            if (SelectedStorageId.HasValue)
            {
                await LoadStorageData(SelectedStorageId.Value);
            }
        }

        private async Task LoadStorageOptions()
        {
            StorageOptions = await _context.RemoteStorages.Select(s => new SelectListItem
            {
                Value = s.Id.ToString(),
                Text = s.Name
            }).ToListAsync();
        }

        private async Task LoadStorageData(int storageId)
        {
            SelectedStorage = await _context.RemoteStorages.FindAsync(storageId);
            if (SelectedStorage == null)
            {
                _logger.LogWarning("Storage not found: {Id}", storageId);
                return;
            }

            var files = await _context.Files.Where(f => f.RemoteStorageId == storageId).ToListAsync();

            TotalSize = files.Sum(f => f.Size) / (1024 * 1024); // Convert to MB
            TotalFiles = files.Count;
            BackupCount = files.Count(f => f.BackupExists);
            BackupPercentage = TotalFiles > 0 ? Math.Round((double)BackupCount / TotalFiles * 100, 2) : 0;

            // Build the file tree HTML
            FileTreeHtml = BuildFileTreeHtml(files);
        }

        private string BuildFileTreeHtml(List<FileEntry> files)
        {
            var tree = new FileTreeNode("/");
            foreach (var file in files)
            {
                tree.AddFile(file.FullPath, file);
            }
            return tree.ToHtml();
        }
    }

    // Helper classes to build the file tree
    public class FileTreeNode
    {
        public string Name { get; set; }
        public Dictionary<string, FileTreeNode> Children { get; set; } = new Dictionary<string, FileTreeNode>();
        public FileEntry? File { get; set; }

        public FileTreeNode(string name)
        {
            Name = name;
        }

        public void AddFile(string path, FileEntry file)
        {
            var parts = path.Trim('/').Split('/');
            AddFile(parts, 0, file);
        }

        private void AddFile(string[] parts, int index, FileEntry file)
        {
            if (index >= parts.Length)
                return;

            var part = parts[index];
            if (!Children.ContainsKey(part))
            {
                Children[part] = new FileTreeNode(part);
            }

            if (index == parts.Length - 1)
            {
                Children[part].File = file;
            }
            else
            {
                Children[part].AddFile(parts, index + 1, file);
            }
        }

        public string ToHtml()
        {
            var html = "<ul>";
            foreach (var child in Children.Values.OrderBy(c => c.Name))
            {
                html += "<li>";
                if (child.File == null)
                {
                    html += $"<span>{child.Name}/</span>";
                    html += child.ToHtml();
                }
                else
                {
                    var backupStatus = child.File.BackupExists ? "Backed Up" : "Pending";
                    html += $"<span>{child.Name} - {backupStatus}</span>";
                }
                html += "</li>";
            }
            html += "</ul>";
            return html;
        }
    }
}
