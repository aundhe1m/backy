using Backy.Data;
using Backy.Models;
using Backy.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Renci.SshNet;

namespace Backy.Pages
{
    public class FileIndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<FileIndexModel> _logger;
        private readonly IDataProtector _protector;
        private readonly IServiceScopeFactory _scopeFactory;

        public FileIndexModel(
            ApplicationDbContext context,
            IDataProtectionProvider provider,
            ILogger<FileIndexModel> logger,
            IServiceScopeFactory scopeFactory)
        {
            _context = context;
            _protector = provider.CreateProtector("Backy.RemoteStorage");
            _logger = logger;
            _scopeFactory = scopeFactory;
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
            _logger.LogInformation("Starting indexing for storage: {Id}", storageId);

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var storage = await dbContext.RemoteStorages.FindAsync(storageId);
            if (storage == null)
            {
                _logger.LogWarning("Storage not found: {Id}", storageId);
                return NotFound();
            }

            if (!storage.IsIndexing)
            {
                storage.IsIndexing = true;
                dbContext.RemoteStorages.Update(storage);
                await dbContext.SaveChangesAsync();

                // Start the indexing in a background task with a new scope
                _ = Task.Run(() => IndexStorageAsync(storage.Id), cancellationToken: CancellationToken.None);
            }

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

        private async Task IndexStorageAsync(int storageId)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var storage = await context.RemoteStorages.FindAsync(storageId);
            if (storage == null)
            {
                _logger.LogWarning("Storage not found during indexing: {Id}", storageId);
                return;
            }

            storage.IsIndexing = true;
            context.RemoteStorages.Update(storage);
            await context.SaveChangesAsync();

            try
            {
                using var client = CreateSftpClient(storage);
                _logger.LogInformation("Connecting to storage: {Name} at {Host}:{Port}", storage.Name, storage.Host, storage.Port);
                client.Connect();

                _logger.LogInformation("Connected to storage: {Name}", storage.Name);

                var files = new List<FileEntry>();

                _logger.LogInformation("Traversing remote directory: {Path}", storage.RemotePath);
                await TraverseRemoteDirectory(client, storage.RemotePath, files, storage.Id);

                _logger.LogInformation("Found {FileCount} files in storage: {Name}", files.Count, storage.Name);

                // Update the database
                foreach (var file in files)
                {
                    var existingFile = await context.Files.FirstOrDefaultAsync(f => f.RemoteStorageId == storage.Id && f.FullPath == file.FullPath);
                    if (existingFile == null)
                    {
                        _logger.LogInformation("Adding new file: {FullPath}", file.FullPath);
                        context.Files.Add(file);
                    }
                    else
                    {
                        _logger.LogInformation("Updating existing file: {FullPath}", file.FullPath);
                        existingFile.Size = file.Size;
                        existingFile.LastModified = file.LastModified;
                        existingFile.IsDeleted = false;
                        context.Files.Update(existingFile);
                    }
                }

                // Mark files that are no longer present as deleted
                var existingFiles = await context.Files.Where(f => f.RemoteStorageId == storage.Id).ToListAsync();
                foreach (var existingFile in existingFiles)
                {
                    if (!files.Any(f => f.FullPath == existingFile.FullPath))
                    {
                        _logger.LogInformation("Marking file as deleted: {FullPath}", existingFile.FullPath);
                        existingFile.IsDeleted = true;
                        context.Files.Update(existingFile);
                    }
                }

                await context.SaveChangesAsync();

                client.Disconnect();
                _logger.LogInformation("Successfully indexed storage: {Name}", storage.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error indexing storage: {Name}", storage.Name);
            }
            finally
            {
                using var scopeUpdate = _scopeFactory.CreateScope();
                var dbContextUpdate = scopeUpdate.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var storageToUpdate = await dbContextUpdate.RemoteStorages.FindAsync(storageId);
                if (storageToUpdate != null)
                {
                    storageToUpdate.IsIndexing = false;
                    dbContextUpdate.RemoteStorages.Update(storageToUpdate);
                    await dbContextUpdate.SaveChangesAsync();
                }
            }
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
                var emptyModel = new FileExplorerModel
                {
                    StorageId = 0,
                    CurrentPath = string.Empty,
                    Files = new List<FileEntry>(),
                    Directories = new List<string>()
                };
                return Partial("_FileExplorerPartial", emptyModel);
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

            return Partial("_FileExplorerPartial", model);
        }


        private SftpClient CreateSftpClient(RemoteStorage storage)
        {
            if (storage.AuthenticationMethod == "Password")
            {
                return new SftpClient(storage.Host, storage.Port, storage.Username, Decrypt(storage.Password));
            }
            else
            {
                using var keyStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Decrypt(storage.SSHKey)));
                var keyFile = new PrivateKeyFile(keyStream);
                var keyFiles = new[] { keyFile };
                var authMethod = new PrivateKeyAuthenticationMethod(storage.Username, keyFiles);
                var connectionInfo = new Renci.SshNet.ConnectionInfo(storage.Host, storage.Port, storage.Username, authMethod);
                return new SftpClient(connectionInfo);
            }
        }

        private string Decrypt(string? input)
        {
            return input != null ? _protector.Unprotect(input) : string.Empty;
        }
    }

    public class FileExplorerModel
    {
        public int StorageId { get; set; }
        public string CurrentPath { get; set; } = string.Empty;
        public List<FileEntry> Files { get; set; } = new List<FileEntry>();
        public List<string> Directories { get; set; } = new List<string>();
    }
}
