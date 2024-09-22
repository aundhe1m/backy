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
        public int? SelectedStorageId { get; set; }

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

        public async Task<IActionResult> OnPostStartIndexingAsync(int storageId)
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

        private void LoadStorageData(int storageId)
        {
            SelectedStorage = _context.RemoteStorages.Find(storageId);
            if (SelectedStorage == null)
            {
                _logger.LogWarning("Storage not found: {Id}", storageId);
                return;
            }

            IsIndexing = SelectedStorage.IsIndexing;

            var files = _context.Files.Where(f => f.RemoteStorageId == storageId).ToList();

            TotalSize = files.Sum(f => f.Size) / (1024 * 1024); // Convert to MB
            TotalBackupSize = files.Where(f => f.BackupExists).Sum(f => f.Size) / (1024 * 1024); // MB
            TotalFiles = files.Count;
            BackupCount = files.Count(f => f.BackupExists);
            BackupPercentage = TotalFiles > 0 ? Math.Round((double)BackupCount / TotalFiles * 100, 2) : 0;
        }

        private async Task TraverseRemoteDirectory(SftpClient client, string remotePath, List<FileEntry> files, int storageId)
        {
            var items = client.ListDirectory(remotePath);
            foreach (var item in items)
            {
                if (item.Name == "." || item.Name == "..")
                    continue;

                var fullPath = item.FullName;
                if (item.IsDirectory)
                {
                    await TraverseRemoteDirectory(client, fullPath, files, storageId);
                }
                else if (item.IsRegularFile)
                {
                    files.Add(new FileEntry
                    {
                        RemoteStorageId = storageId,
                        FileName = item.Name,
                        FullPath = fullPath,
                        Size = item.Attributes.Size,
                        LastModified = item.LastWriteTime
                    });
                }
            }
        }

        public IActionResult OnGetGetFileExplorer(int storageId, string? path)
        {
            _logger.LogInformation("GetFileExplorer called with storageId={StorageId}, path={Path}", storageId, path);

            var storage = _context.RemoteStorages.Find(storageId);
            if (storage == null)
            {
                _logger.LogWarning("Storage not found: {Id}", storageId);
                return NotFound();
            }

            var currentPath = path ?? storage.RemotePath;

            var files = _context.Files
                .Where(f => f.RemoteStorageId == storageId && !f.IsDeleted)
                .ToList();

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

            var model = new FileExplorerModel
            {
                StorageId = storage.Id,
                CurrentPath = currentPath,
                Files = filesInCurrentDir,
                Directories = directories
            };

            return new PartialViewResult
            {
                ViewName = "_FileExplorerPartial",
                ViewData = new ViewDataDictionary<FileExplorerModel>(ViewData, model)
            };
        }
    }
}
