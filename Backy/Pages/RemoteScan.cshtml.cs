using Backy.Data;
using Backy.Models;
using Backy.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Renci.SshNet;

namespace Backy.Pages
{
    public class RemoteScanModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<RemoteScanModel> _logger;
        private readonly IDataProtector _protector;
        private readonly IIndexingQueue _indexingQueue;

        public RemoteScanModel(
            ApplicationDbContext context,
            IDataProtectionProvider provider,
            ILogger<RemoteScanModel> logger,
            IIndexingQueue indexingQueue)
        {
            _context = context;
            _protector = provider.CreateProtector("Backy.RemoteScan");
            _logger = logger;
            _indexingQueue = indexingQueue;
        }

        public IList<StorageSourceViewModel> StorageSources { get; set; } = new List<StorageSourceViewModel>();

        [BindProperty]
        public RemoteScan RemoteScan { get; set; } = new RemoteScan();

        public async Task OnGetAsync()
        {
            var RemoteScans = await _context.RemoteScans.ToListAsync();

            foreach (var storage in RemoteScans)
            {
                var model = new StorageSourceViewModel
                {
                    RemoteScan = storage,
                    IsIndexing = storage.IsIndexing
                };

                // Calculate backup stats
                var files = await _context.Files.Where(f => f.RemoteScanId == storage.Id).ToListAsync();
                model.TotalSize = files.Sum(f => f.Size);
                model.TotalBackupSize = files.Where(f => f.BackupExists).Sum(f => f.Size);
                model.TotalFiles = files.Count;
                model.BackupCount = files.Count(f => f.BackupExists);
                model.BackupPercentage = model.TotalFiles > 0 ? Math.Round((double)model.BackupCount / model.TotalFiles * 100, 2) : 0;

                StorageSources.Add(model);
            }
        }

        public async Task<IActionResult> OnPostAddAsync()
        {
            // Validate and add new RemoteScan

            if (!ModelState.IsValid)
            {
                await OnGetAsync();
                return Page();
            }

            // Custom validation
            if (RemoteScan.AuthenticationMethod == "Password")
            {
                if (string.IsNullOrWhiteSpace(RemoteScan.Password))
                {
                    ModelState.AddModelError(nameof(RemoteScan.Password), "Password is required when using Password authentication.");
                }
            }
            else if (RemoteScan.AuthenticationMethod == "SSH Key")
            {
                if (string.IsNullOrWhiteSpace(RemoteScan.SSHKey))
                {
                    ModelState.AddModelError(nameof(RemoteScan.SSHKey), "SSH Key is required when using SSH Key authentication.");
                }
            }
            else
            {
                ModelState.AddModelError(nameof(RemoteScan.AuthenticationMethod), "Invalid Authentication Method.");
            }

            if (!ModelState.IsValid)
            {
                await OnGetAsync();
                return Page();
            }

            // Encrypt sensitive data
            if (RemoteScan.AuthenticationMethod == "Password" && !string.IsNullOrEmpty(RemoteScan.Password))
            {
                RemoteScan.Password = Encrypt(RemoteScan.Password);
            }
            else if (RemoteScan.AuthenticationMethod == "SSH Key" && !string.IsNullOrEmpty(RemoteScan.SSHKey))
            {
                RemoteScan.SSHKey = Encrypt(RemoteScan.SSHKey);
            }

            // Validate the connection
            bool isValid = ValidateConnection(RemoteScan);
            if (!isValid)
            {
                ModelState.AddModelError(string.Empty, "Unable to connect with the provided details.");
                await OnGetAsync();
                return Page();
            }

            _context.RemoteScans.Add(RemoteScan);
            await _context.SaveChangesAsync();

            // Check and update storage status
            await StorageStatusChecker.CheckAndUpdateStorageStatusAsync(RemoteScan, _context, _protector, _logger);

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostEditAsync()
        {
            if (!ModelState.IsValid)
            {
                await OnGetAsync();
                return Page();
            }

            // Custom validation
            if (RemoteScan.AuthenticationMethod == "Password")
            {
                if (string.IsNullOrWhiteSpace(RemoteScan.Password))
                {
                    ModelState.AddModelError(nameof(RemoteScan.Password), "Password is required when using Password authentication.");
                }
            }
            else if (RemoteScan.AuthenticationMethod == "SSH Key")
            {
                if (string.IsNullOrWhiteSpace(RemoteScan.SSHKey))
                {
                    ModelState.AddModelError(nameof(RemoteScan.SSHKey), "SSH Key is required when using SSH Key authentication.");
                }
            }
            else
            {
                ModelState.AddModelError(nameof(RemoteScan.AuthenticationMethod), "Invalid Authentication Method.");
            }

            if (!ModelState.IsValid)
            {
                await OnGetAsync();
                return Page();
            }

            var existingStorage = await _context.RemoteScans.FindAsync(RemoteScan.Id);
            if (existingStorage == null)
            {
                return NotFound();
            }

            // Update fields
            existingStorage.Name = RemoteScan.Name;
            existingStorage.Host = RemoteScan.Host;
            existingStorage.Port = RemoteScan.Port;
            existingStorage.Username = RemoteScan.Username;
            existingStorage.AuthenticationMethod = RemoteScan.AuthenticationMethod;
            existingStorage.RemotePath = RemoteScan.RemotePath;

            // Encrypt sensitive data
            if (RemoteScan.AuthenticationMethod == "Password")
            {
                if (!string.IsNullOrEmpty(RemoteScan.Password) && RemoteScan.Password != "********")
                {
                    existingStorage.Password = Encrypt(RemoteScan.Password);
                }
            }
            else if (RemoteScan.AuthenticationMethod == "SSH Key")
            {
                if (!string.IsNullOrEmpty(RemoteScan.SSHKey) && RemoteScan.SSHKey != "********")
                {
                    existingStorage.SSHKey = Encrypt(RemoteScan.SSHKey);
                }
            }

            // Validate the connection
            bool isValid = ValidateConnection(existingStorage);
            if (!isValid)
            {
                ModelState.AddModelError(string.Empty, "Unable to connect with the provided details.");
                await OnGetAsync();
                return Page();
            }

            try
            {
                await _context.SaveChangesAsync();
                await StorageStatusChecker.CheckAndUpdateStorageStatusAsync(existingStorage, _context, _protector, _logger);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!RemoteScanExists(RemoteScan.Id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAsync(Guid id)
        {
            var storage = await _context.RemoteScans.FindAsync(id);
            if (storage != null)
            {
                _context.RemoteScans.Remove(storage);
                await _context.SaveChangesAsync();
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostToggleEnableAsync(Guid id)
        {
            var storage = await _context.RemoteScans.FindAsync(id);
            if (storage != null)
            {
                storage.IsEnabled = !storage.IsEnabled;
                await _context.SaveChangesAsync();
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostStartIndexingAsync(Guid id)
        {
            _indexingQueue.EnqueueIndexing(id);
            return RedirectToPage();
        }

        // Helper methods

        private bool RemoteScanExists(Guid id)
        {
            return _context.RemoteScans.Any(e => e.Id == id);
        }

        private string Encrypt(string input)
        {
            return _protector.Protect(input);
        }

        private string Decrypt(string? input)
        {
            return input != null ? _protector.Unprotect(input) : string.Empty;
        }

        private bool ValidateConnection(RemoteScan storage)
        {
            try
            {
                using var client = CreateSftpClient(storage);
                client.Connect();
                bool isConnected = client.IsConnected;
                client.Disconnect();
                return isConnected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection validation failed for storage: {Name}", storage.Name);
                return false;
            }
        }

        private SftpClient CreateSftpClient(RemoteScan storage)
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

        public async Task<JsonResult> OnGetGetFileExplorer(Guid storageId, string? path, string? search)
        {
            _logger.LogInformation("GetFileExplorer called with storageId={StorageId}, path={Path}", storageId, path);

            var storage = await _context.RemoteScans.FindAsync(storageId);
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
                .AsEnumerable()
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
                    FullPath = d,
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

        // Helper methods from FileIndex.cshtml.cs
        private string NormalizePath(string path)
        {
            return path.Replace('\\', '/').TrimEnd('/');
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

        private void SortDirectoryTree(DirectoryNode node)
        {
            node.Children = node.Children.OrderBy(n => n.Name).ToList();
            foreach (var child in node.Children)
            {
                SortDirectoryTree(child);
            }
        }

        private static string GetNavPath(string fullPath, string remotePath)
        {
            if (fullPath.StartsWith(remotePath))
            {
                return fullPath.Substring(remotePath.Length).TrimStart('/', '\\');
            }
            return fullPath;
        }

        public class StorageSourceViewModel
        {
            public RemoteScan RemoteScan { get; set; } = new RemoteScan();

            public bool IsIndexing { get; set; }

            public long TotalSize { get; set; }
            public long TotalBackupSize { get; set; }
            public int TotalFiles { get; set; }
            public int BackupCount { get; set; }
            public double BackupPercentage { get; set; }
        }
    }
}
