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
            _protector = provider.CreateProtector("Backy.RemoteStorage");
            _logger = logger;
            _scopeFactory = scopeFactory;
            _indexingQueue = indexingQueue;
        }

        [BindProperty]
        public Guid? SelectedStorageId { get; set; }

        public RemoteStorage? SelectedStorage { get; set; }

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
                    .Where(s => s.RemoteStorageId == SelectedStorageId.Value)
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
            StorageOptions = _context.RemoteStorages.Select(s => new SelectListItem
            {
                Value = s.Id.ToString(),
                Text = s.Name
            }).ToList();
        }

        private void LoadStorageData(Guid storageId)
        {
            SelectedStorage = _context.RemoteStorages.Find(storageId);
            if (SelectedStorage == null)
            {
                _logger.LogWarning("Storage not found: {Id}", storageId);
                return;
            }

            IsIndexing = SelectedStorage.IsIndexing;

            var files = _context.Files.Where(f => f.RemoteStorageId == storageId).ToList();

            TotalSize = files.Sum(f => f.Size); // In bytes
            TotalBackupSize = files.Where(f => f.BackupExists).Sum(f => f.Size); // In bytes
            TotalFiles = files.Count;
            BackupCount = files.Count(f => f.BackupExists);
            BackupPercentage = TotalFiles > 0 ? Math.Round((double)BackupCount / TotalFiles * 100, 2) : 0;
        }

        public JsonResult OnGetGetFileExplorer(Guid storageId, string? path, string? search)
        {
            _logger.LogInformation("GetFileExplorer called with storageId={StorageId}, path={Path}", storageId, path);

            var storage = _context.RemoteStorages.Find(storageId);
            if (storage == null)
            {
                _logger.LogWarning("Storage not found: {Id}", storageId);
                return new JsonResult(new { success = false, message = "Storage not found" });
            }

            var currentPath = path ?? storage.RemotePath;

            var filesQuery = _context.Files
                .Where(f => f.RemoteStorageId == storageId && !f.IsDeleted);

            if (!string.IsNullOrEmpty(search))
            {
                filesQuery = filesQuery.Where(f => f.FileName.Contains(search));
            }

            var files = filesQuery.ToList();

            var directories = files
                .Where(f => f.FullPath != currentPath && f.FullPath.StartsWith(currentPath))
                .Select(f => System.IO.Path.GetDirectoryName(f.FullPath))
                .Where(d => d != null && d != currentPath)
                .Distinct()
                .Select(d => d!)
                .ToList();

            var filesInCurrentDir = files
                .Where(f => System.IO.Path.GetDirectoryName(f.FullPath) == currentPath)
                .ToList();

            // Build directory tree for the left navigation
            var directoryTree = BuildDirectoryTree(files, storage.RemotePath);

            var model = new
            {
                success = true,
                storageId = storage.Id,
                currentPath = currentPath,
                files = filesInCurrentDir.Select(f => new
                {
                    name = f.FileName,
                    size = f.Size,
                    lastModified = f.LastModified,
                    backupExists = f.BackupExists
                }),
                directories = directories.Select(d => new
                {
                    name = System.IO.Path.GetFileName(d),
                    fullPath = d
                }),
                directoryTree = directoryTree
            };

            return new JsonResult(model);
        }

        private List<dynamic> BuildDirectoryTree(List<FileEntry> files, string rootPath)
        {
            var directories = files
                .Select(f => System.IO.Path.GetDirectoryName(f.FullPath))
                .Where(d => d != null)
                .Distinct()
                .ToList();

            var tree = new List<dynamic>();

            foreach (var dir in directories)
            {
                tree.Add(new
                {
                    name = dir!.Replace(rootPath, ""),
                    fullPath = dir
                });
            }

            return tree;
        }
    }
}
