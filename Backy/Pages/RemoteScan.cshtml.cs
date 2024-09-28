using Backy.Data;
using Backy.Models;
using Backy.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Renci.SshNet;
using Newtonsoft.Json;

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

            // Fetch existing storage first
            var existingStorage = await _context.RemoteScans.FindAsync(RemoteScan.Id);
            if (existingStorage == null)
            {
                return NotFound();
            }

            // Custom validation
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
            else
            {
                ModelState.AddModelError(nameof(RemoteScan.AuthenticationMethod), "Invalid Authentication Method.");
            }

            if (!ModelState.IsValid)
            {
                await OnGetAsync();
                return Page();
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

        public async Task<JsonResult> OnPostToggleEnableAsync(Guid id)
        {
            var storage = await _context.RemoteScans.FindAsync(id);
            if (storage != null)
            {
                storage.IsEnabled = !storage.IsEnabled;
                await _context.SaveChangesAsync();
                return new JsonResult(new { success = true });
            }
            return new JsonResult(new { success = false, message = "Storage not found." });
        }

        public JsonResult OnPostStartIndexing(Guid id)
        {
            _indexingQueue.EnqueueIndexing(id);
            return new JsonResult(new { success = true });
        }

        // New handler to update storage sources via AJAX
        public async Task<JsonResult> OnGetUpdateStorageSourcesAsync()
        {
            var storageDtos = new List<StorageSourceDto>();

            var RemoteScans = await _context.RemoteScans.ToListAsync();

            foreach (var storage in RemoteScans)
            {
                var files = await _context.Files.Where(f => f.RemoteScanId == storage.Id).ToListAsync();

                var dto = new StorageSourceDto
                {
                    Id = storage.Id,
                    Name = storage.Name,
                    IsEnabled = storage.IsEnabled,
                    IsIndexing = storage.IsIndexing,
                    Status = storage.Status,
                    LastChecked = storage.LastChecked,
                    TotalSize = files.Sum(f => f.Size),
                    TotalBackupSize = files.Where(f => f.BackupExists).Sum(f => f.Size),
                    TotalFiles = files.Count,
                    BackupCount = files.Count(f => f.BackupExists),
                    BackupPercentage = files.Count > 0 ? Math.Round((double)files.Count(f => f.BackupExists) / files.Count * 100, 2) : 0
                };

                storageDtos.Add(dto);
            }

            return new JsonResult(new { success = true, storageSources = storageDtos });
        }

        public async Task<JsonResult> OnGetGetStorageAsync(Guid id)
        {
            var storage = await _context.RemoteScans.FindAsync(id);
            if (storage == null)
            {
                return new JsonResult(new { success = false, message = "Storage not found." });
            }

            return new JsonResult(new
            {
                success = true,
                id = storage.Id,
                name = storage.Name,
                host = storage.Host,
                port = storage.Port,
                username = storage.Username,
                authenticationMethod = storage.AuthenticationMethod,
                remotePath = storage.RemotePath,
                passwordSet = !string.IsNullOrEmpty(storage.Password),
                sshKeySet = !string.IsNullOrEmpty(storage.SSHKey)
            });
        }

        // Handler to get index schedules
        public async Task<JsonResult> OnGetGetIndexSchedulesAsync(Guid id)
        {
            var schedules = await _context.IndexSchedules
                .Where(s => s.RemoteScanId == id)
                .GroupBy(s => s.TimeOfDayMinutes)
                .Select(g => new
                {
                    Time = $"{g.Key / 60:D2}:{g.Key % 60:D2}",
                    Days = g.Select(s => s.DayOfWeek).ToList()
                })
                .ToListAsync();

            return new JsonResult(new { success = true, schedules = schedules });
        }

        // Handler to save index schedules
        public async Task<JsonResult> OnPostSaveIndexSchedulesAsync()
        {
            try
            {
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync();

                var scheduleData = JsonConvert.DeserializeObject<ScheduleSaveRequest>(body);

                if (scheduleData == null)
                {
                    return new JsonResult(new { success = false, message = "Invalid data." });
                }

                var existingSchedules = _context.IndexSchedules.Where(s => s.RemoteScanId == scheduleData.StorageId);
                _context.IndexSchedules.RemoveRange(existingSchedules);

                foreach (var schedule in scheduleData.Schedules)
                {
                    foreach (var day in schedule.Days)
                    {
                        var timeParts = schedule.Time.Split(':');
                        int hours = int.Parse(timeParts[0]);
                        int minutes = int.Parse(timeParts[1]);
                        int totalMinutes = hours * 60 + minutes;

                        var indexSchedule = new IndexSchedule
                        {
                            RemoteScanId = scheduleData.StorageId,
                            DayOfWeek = day,
                            TimeOfDayMinutes = totalMinutes
                        };
                        _context.IndexSchedules.Add(indexSchedule);
                    }
                }

                await _context.SaveChangesAsync();

                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving index schedules.");
                return new JsonResult(new { success = false, message = "An error occurred." });
            }
        }

        public async Task<JsonResult> OnGetGetFileExplorerAsync(Guid storageId, string? path, CancellationToken cancellationToken)
        {
            var storage = await _context.RemoteScans.FindAsync(storageId);
            if (storage == null)
            {
                return new JsonResult(new { success = false, message = "Storage not found." });
            }

            string currentPath = string.IsNullOrEmpty(path) ? storage.RemotePath : path;
            string parentPath = Path.GetDirectoryName(currentPath.Replace('\\', '/')) ?? storage.RemotePath;

            string currentPathWithSlash = currentPath.EndsWith("/") ? currentPath : currentPath + "/";

            // Initialize directory tree node
            DirectoryNode directoryTree = new DirectoryNode
            {
                Name = "Root",
                FullPath = currentPathWithSlash,
                Children = new List<DirectoryNode>()
            };

            try
            {
                using var client = CreateSftpClient(storage);
                client.Connect();

                // Traverse directories using SFTP
                await TraverseRemoteDirectory(client, currentPathWithSlash, directoryTree, storageId, cancellationToken);

                client.Disconnect();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error traversing directories for storage: {Name}", storage.Name);
                return new JsonResult(new { success = false, message = "Error traversing directories." });
            }

            // Extract immediate subdirectories for the current path
            var immediateDirectories = directoryTree.Children.Select(c => new DirectoryDto
            {
                Name = c.Name,
                FullPath = c.FullPath
            }).ToList();

            var data = new
            {
                success = true,
                directoryTree = directoryTree,
                directories = immediateDirectories, // Added directories field
                files = await _context.Files
                    .Where(f => f.RemoteScanId == storageId && !f.IsDeleted && f.FullPath.StartsWith(currentPathWithSlash))
                    .Select(f => new
                    {
                        name = f.FileName,
                        size = f.Size,
                        fullPath = f.FullPath,
                        backupExists = f.BackupExists
                    })
                    .ToListAsync(),
                navPath = currentPath.Replace(storage.RemotePath, "").TrimStart('/'),
                remotePath = storage.RemotePath,
                currentPath = currentPath,
                parentPath = parentPath
            };

            return new JsonResult(data);
        }


        private async Task TraverseRemoteDirectory(SftpClient client, string remotePath, DirectoryNode parentNode, Guid storageId, CancellationToken cancellationToken)
        {
            var directories = client.ListDirectory(remotePath).Where(d => d.IsDirectory && d.Name != "." && d.Name != "..");

            foreach (var dir in directories)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var dirNode = new DirectoryNode
                {
                    Name = dir.Name,
                    FullPath = dir.FullName + "/",
                    Children = new List<DirectoryNode>()
                };

                // Check if the directory contains any files
                bool hasFiles = await _context.Files.AnyAsync(f => f.RemoteScanId == storageId && f.FullPath.StartsWith(dir.FullName + "/") && !f.IsDeleted, cancellationToken);

                if (hasFiles)
                {
                    parentNode.Children.Add(dirNode);
                    // Recursively traverse subdirectories
                    await TraverseRemoteDirectory(client, dir.FullName + "/", dirNode, storageId, cancellationToken);
                }
                // Else, skip adding the directory as it contains no files
            }
        }
        private DirectoryNode BuildDirectoryTree(List<string> subDirectories, string currentPath)
        {
            var root = new DirectoryNode
            {
                Name = "Root",
                FullPath = currentPath,
                Children = new List<DirectoryNode>()
            };

            foreach (var dirPath in subDirectories)
            {
                var relativePath = dirPath.Substring(currentPath.Length);
                var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var currentNode = root;

                foreach (var segment in segments)
                {
                    var existingChild = currentNode.Children.FirstOrDefault(c => c.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
                    if (existingChild == null)
                    {
                        existingChild = new DirectoryNode
                        {
                            Name = segment,
                            FullPath = currentNode.FullPath + segment + "/",
                            Children = new List<DirectoryNode>()
                        };
                        currentNode.Children.Add(existingChild);
                    }

                    currentNode = existingChild;
                }
            }

            // Sort the directory tree alphabetically
            SortDirectoryTree(root);

            return root;
        }

        private void SortDirectoryTree(DirectoryNode node)
        {
            node.Children = node.Children.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var child in node.Children)
            {
                SortDirectoryTree(child);
            }
        }


        public async Task<JsonResult> OnGetSearchFilesAsync(Guid storageId, string query)
        {
            var storage = await _context.RemoteScans.FindAsync(storageId);
            if (storage == null)
            {
                return new JsonResult(new { success = false, message = "Storage not found." });
            }

            // Search for files matching the query
            var files = await _context.Files
                .Where(f => f.RemoteScanId == storageId && f.FileName.Contains(query) && !f.IsDeleted)
                .Select(f => new
                {
                    type = "File",
                    name = f.FileName,
                    fullPath = f.FullPath,
                    navPath = f.FullPath.Replace(storage.RemotePath, "")
                })
                .ToListAsync();

            // Search for directories matching the query
            var directories = await _context.Files
                .Where(f => f.RemoteScanId == storageId
                            && !f.IsDeleted
                            && f.FullPath.Contains("/" + query + "/")) // Simple pattern matching
                .Select(f => new
                {
                    type = "Directory",
                    name = System.IO.Path.GetFileName(f.FullPath.TrimEnd('/')),
                    fullPath = f.FullPath.TrimEnd('/'),
                    navPath = f.FullPath.Replace(storage.RemotePath, "")
                })
                .Distinct()
                .ToListAsync();

            // Combine files and directories
            var results = files.Concat(directories).ToList();

            return new JsonResult(new { success = true, results = results });
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

        // Additional classes for data transfer
        public class StorageSourceDto
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public bool IsEnabled { get; set; }
            public bool IsIndexing { get; set; }
            public string Status { get; set; } = string.Empty;
            public DateTime? LastChecked { get; set; }
            public long TotalSize { get; set; }
            public long TotalBackupSize { get; set; }
            public int TotalFiles { get; set; }
            public int BackupCount { get; set; }
            public double BackupPercentage { get; set; }
        }

        public class ScheduleSaveRequest
        {
            public Guid StorageId { get; set; }
            public List<ScheduleDto> Schedules { get; set; } = new List<ScheduleDto>();
        }

        public class ScheduleDto
        {
            public List<int> Days { get; set; } = new List<int>();
            public string Time { get; set; } = "";
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