using Backy.Data;
using Backy.Models;
using Backy.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Renci.SshNet;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System.Text.Json;
using System.Linq;
using System.IO;

namespace Backy.Pages
{
    public class FileIndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<FileIndexModel> _logger;
        public List<IndexSchedule> Schedules { get; set; } = new List<IndexSchedule>();

        private readonly IDataProtector _protector;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IIndexingQueue _indexingQueue;

        public FileIndexModel(
            ApplicationDbContext context,
            IDataProtectionProvider provider,
            ILogger<FileIndexModel> logger,
            IServiceScopeFactory scopeFactory,
            IIndexingQueue indexingQueue)
        {
            _context = context;
            _protector = provider.CreateProtector("Backy.RemoteScan");
            _logger = logger;
            _scopeFactory = scopeFactory;
            _indexingQueue = indexingQueue;
        }

        [BindProperty]
        public Guid? SelectedStorageId { get; set; }

        public RemoteScan? SelectedStorage { get; set; }

        public List<SelectListItem> StorageOptions { get; set; } = new List<SelectListItem>();

        public bool IsIndexing { get; set; }

        public long TotalSize { get; set; }
        public long TotalBackupSize { get; set; }
        public int TotalFiles { get; set; }
        public int BackupCount { get; set; }
        public double BackupPercentage { get; set; }

        public void OnGet()
        {
            LoadStorageOptions();
            LoadSchedules();

            if (SelectedStorageId.HasValue)
            {
                LoadStorageData(SelectedStorageId.Value);
            }
        }

        private void LoadSchedules()
        {
            if (SelectedStorageId.HasValue)
            {
                Schedules = _context.IndexSchedules
                    .Where(s => s.RemoteScanId == SelectedStorageId.Value)
                    .ToList();
            }
        }

        public IActionResult OnPostSelectStorage()
        {
            LoadStorageOptions();

            if (SelectedStorageId.HasValue)
            {
                LoadStorageData(SelectedStorageId.Value);
            }

            return Page();
        }

        public IActionResult OnPostStartIndexing(Guid storageId)
        {
            _logger.LogInformation("Enqueueing indexing for storage: {Id}", storageId);

            // Enqueue the indexing request
            _indexingQueue.EnqueueIndexing(storageId);

            return RedirectToPage();
        }


        private void LoadStorageOptions()
        {
            StorageOptions = _context.RemoteScans.Select(s => new SelectListItem
            {
                Value = s.Id.ToString(),
                Text = s.Name
            }).ToList();
        }

        private void LoadStorageData(Guid storageId)
        {
            SelectedStorage = _context.RemoteScans.Find(storageId);
            if (SelectedStorage == null)
            {
                _logger.LogWarning("Storage not found: {Id}", storageId);
                return;
            }

            IsIndexing = SelectedStorage.IsIndexing;

            var files = _context.Files.Where(f => f.RemoteScanId == storageId).ToList();

            TotalSize = files.Sum(f => f.Size); // In bytes
            TotalBackupSize = files.Where(f => f.BackupExists).Sum(f => f.Size); // In bytes
            TotalFiles = files.Count;
            BackupCount = files.Count(f => f.BackupExists);
            BackupPercentage = TotalFiles > 0 ? Math.Round((double)BackupCount / TotalFiles * 100, 2) : 0;
        }

        public JsonResult OnGetGetFileExplorer(Guid storageId, string? path, string? search)
        {
            _logger.LogInformation("GetFileExplorer called with storageId={StorageId}, path={Path}", storageId, path);

            var storage = _context.RemoteScans.Find(storageId);
            if (storage == null)
            {
                _logger.LogWarning("Storage not found: {Id}", storageId);
                return new JsonResult(new { success = false, message = "Storage not found" });
            }

            var rootPath = NormalizePath(storage.RemotePath);
            var currentPath = NormalizePath(path ?? storage.RemotePath);

            var filesQuery = _context.Files
                .Where(f => f.RemoteScanId == storageId && !f.IsDeleted);

            if (!string.IsNullOrEmpty(search))
            {
                filesQuery = filesQuery.Where(f => f.FileName.Contains(search));
            }

            var allFiles = filesQuery.ToList();

            // Files and directories in the current directory
            var filesInCurrentDir = allFiles
                .Where(f => NormalizePath(Path.GetDirectoryName(f.FullPath)) == currentPath)
                .OrderBy(f => f.FileName)
                .ToList();

            var directoriesInCurrentDir = allFiles
                .Select(f => NormalizePath(Path.GetDirectoryName(f.FullPath)))
                .Where(d => d != null && NormalizePath(Path.GetDirectoryName(d)) == currentPath)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            // Build directory tree for the left navigation
            var directoryTree = BuildDirectoryTree(allFiles, rootPath);

            // Compute navPath
            var navPath = currentPath.StartsWith(rootPath)
                ? currentPath.Substring(rootPath.Length)
                : currentPath;

            navPath = navPath.TrimStart('/');

            // Compute parentPath
            var parentPath = NormalizePath(Path.GetDirectoryName(currentPath));
            if (string.IsNullOrEmpty(parentPath) || parentPath.Length < rootPath.Length)
            {
                parentPath = rootPath;
            }

            var model = new
            {
                success = true,
                storageId = storage.Id,
                currentPath = currentPath,
                navPath = "/" + navPath,
                remotePath = rootPath,
                parentPath = parentPath,
                files = filesInCurrentDir.Select(f => new
                {
                    name = f.FileName,
                    size = f.Size,
                    lastModified = f.LastModified,
                    backupExists = f.BackupExists
                }),
                directories = directoriesInCurrentDir.Select(d => new
                {
                    name = Path.GetFileName(d),
                    fullPath = d
                }),
                directoryTree = directoryTree
            };

            return new JsonResult(model);
        }

        private DirectoryNode BuildDirectoryTree(List<FileEntry> files, string rootPath)
        {
            rootPath = NormalizePath(rootPath);

            var root = new DirectoryNode
            {
                Name = "Root",
                FullPath = rootPath
            };

            var directories = files
                .Select(f => NormalizePath(Path.GetDirectoryName(f.FullPath)))
                .Where(d => d != null)
                .Distinct()
                .ToList();

            foreach (var dir in directories)
            {
                var relativePath = Path.GetRelativePath(rootPath, dir);
                relativePath = NormalizePath(relativePath);

                var parts = relativePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

                AddPathToTree(root, parts, rootPath);
            }

            // Sort the directory tree after building it
            SortDirectoryTree(root);

            return root;
        }

        public JsonResult OnGetSearchFiles(Guid storageId, string query)
        {
            var storage = _context.RemoteScans.Find(storageId);
            if (storage == null)
            {
                return new JsonResult(new { success = false, message = "Storage not found" });
            }

            var remotePath = NormalizePath(storage.RemotePath);

            // Normalize query for case-insensitive search
            query = query.ToLowerInvariant();

            // Search for matching files
            var matchingFilesQuery = _context.Files
                .Where(f => f.RemoteScanId == storageId && !f.IsDeleted && EF.Functions.Like(f.FileName.ToLower(), $"%{query}%"))
                .Select(f => new
                {
                    f.FileName,
                    f.FullPath
                });

            var matchingFiles = matchingFilesQuery
                .AsEnumerable() // Switch to client-side evaluation
                .Select(f => new SearchResultItem
                {
                    Type = "File",
                    Name = f.FileName,
                    FullPath = NormalizePath(f.FullPath),
                    NavPath = GetNavPath(NormalizePath(f.FullPath), remotePath)
                });

            // Get all directories
            var allDirectoriesQuery = _context.Files
                .Where(f => f.RemoteScanId == storageId && !f.IsDeleted)
                .Select(f => Path.GetDirectoryName(f.FullPath));

            var allDirectories = allDirectoriesQuery
                .AsEnumerable()
                .Where(d => !string.IsNullOrEmpty(d))
                .Select(d => NormalizePath(d))
                .Distinct()
                .ToList();

            // Filter directories on the client side
            var matchingDirectories = allDirectories
                .Where(d => Path.GetFileName(d).ToLower().Contains(query))
                .Select(d => new SearchResultItem
                {
                    Type = "Directory",
                    Name = Path.GetFileName(d),
                    FullPath = d, // Already normalized
                    NavPath = GetNavPath(d, remotePath)
                });

            // Combine results and eliminate duplicates
            var results = matchingFiles.Concat(matchingDirectories)
                .GroupBy(r => new { r.Type, r.FullPath })
                .Select(g => g.First())
                .OrderBy(r => r.Name)
                .Take(8)
                .ToList();

            return new JsonResult(new { success = true, results = results });
        }

        private static string GetNavPath(string fullPath, string remotePath)
        {
            if (fullPath.StartsWith(remotePath))
            {
                return fullPath.Substring(remotePath.Length).TrimStart('/', '\\');
            }
            return fullPath;
        }


        private void AddPathToTree(DirectoryNode currentNode, string[] parts, string currentFullPath)
        {
            if (parts.Length == 0)
                return;

            var part = parts[0];
            var childFullPath = NormalizePath($"{currentFullPath}/{part}");

            var childNode = currentNode.Children.FirstOrDefault(n => n.Name == part && n.FullPath == childFullPath);

            if (childNode == null)
            {
                childNode = new DirectoryNode
                {
                    Name = part,
                    FullPath = childFullPath
                };
                currentNode.Children.Add(childNode);
            }

            AddPathToTree(childNode, parts.Skip(1).ToArray(), childFullPath);
        }


        private string NormalizePath(string path)
        {
            return path.Replace('\\', '/').TrimEnd('/');
        }

        private void SortDirectoryTree(DirectoryNode node)
        {
            node.Children = node.Children.OrderBy(n => n.Name).ToList();
            foreach (var child in node.Children)
            {
                SortDirectoryTree(child);
            }
        }

    }
}
