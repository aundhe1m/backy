using Backy.Data;
using Backy.Models;
using Backy.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Renci.SshNet;
using System.Text.Json;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

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
            var remoteScans = await _context.RemoteScans.ToListAsync();

            foreach (var storage in remoteScans)
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

            // Custom validation for authentication method
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

            // Encrypt sensitive data before saving
            if (RemoteScan.AuthenticationMethod == "Password" && !string.IsNullOrEmpty(RemoteScan.Password))
            {
                RemoteScan.Password = Encrypt(RemoteScan.Password);
            }
            else if (RemoteScan.AuthenticationMethod == "SSH Key" && !string.IsNullOrEmpty(RemoteScan.SSHKey))
            {
                RemoteScan.SSHKey = Encrypt(RemoteScan.SSHKey);
            }

            // Validate the connection to the remote storage
            bool isValid = ValidateConnection(RemoteScan);
            if (!isValid)
            {
                ModelState.AddModelError(string.Empty, "Unable to connect with the provided details.");
                await OnGetAsync();
                return Page();
            }

            // Add the new RemoteScan to the database
            _context.RemoteScans.Add(RemoteScan);
            await _context.SaveChangesAsync();

            // Check and update storage status
            await StorageStatusChecker.CheckAndUpdateStorageStatusAsync(RemoteScan, _context, _protector, _logger);

            // Enqueue indexing for the new storage source without blocking the frontend
            _indexingQueue.EnqueueIndexing(RemoteScan.Id);

            // Redirect to the same page
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

        // Handler to update storage sources via AJAX
        public async Task<JsonResult> OnGetUpdateStorageSourcesAsync()
        {
            var storageDtos = new List<StorageSourceDto>();

            var remoteScans = await _context.RemoteScans.ToListAsync();

            foreach (var storage in remoteScans)
            {
                var files = await _context.Files
                    .Where(f => f.RemoteScanId == storage.Id)
                    .ToListAsync();

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

                var scheduleData = JsonSerializer.Deserialize<ScheduleSaveRequest>(body);

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
                        if (timeParts.Length != 2 ||
                            !int.TryParse(timeParts[0], out int hours) ||
                            !int.TryParse(timeParts[1], out int minutes))
                        {
                            continue; // Skip invalid time formats
                        }

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

        public async Task<JsonResult> OnGetFileExplorerAsync(Guid storageId, string? path, CancellationToken cancellationToken)
        {
            _logger.LogInformation("OnGetFileExplorerAsync called with StorageId: {StorageId}, Path: {Path}", storageId, path);

            // Fetch the pre-generated storageContent from the database
            var storageContentJson = await _context.StorageContents
                .Where(sc => sc.RemoteScanId == storageId)
                .Select(sc => sc.ContentJson)
                .FirstOrDefaultAsync(cancellationToken);

            if (storageContentJson == null)
            {
                _logger.LogWarning("Storage content not found for storageId: {StorageId}", storageId);
                return new JsonResult(new { success = false, message = "Storage content not available. Please index the storage first." });
            }

            // Deserialize the storageContent JSON
            var rootItem = JsonSerializer.Deserialize<StorageContentItem>(storageContentJson);

            StorageContentItem nodeToReturn = rootItem;

            if (!string.IsNullOrEmpty(path))
            {
                nodeToReturn = FindNodeByPath(rootItem, path) ?? rootItem;
            }

            var data = new
            {
                success = true,
                storageContent = nodeToReturn
            };

            _logger.LogInformation("Returning stored storageContent for storageId: {StorageId}", storageId);
            return new JsonResult(data);
        }

        private StorageContentItem? FindNodeByPath(StorageContentItem node, string path)
        {
            if (node.FullPath == path)
            {
                return node;
            }

            foreach (var child in node.Children)
            {
                var result = FindNodeByPath(child, path);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private async Task TraverseAndBuildStorageContent(
            SftpClient client,
            string remotePath,
            StorageContentItem parentNode,
            Guid storageId,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Traversing remote path: {RemotePath}", remotePath);

            var directories = client.ListDirectory(remotePath)
                .Where(d => d.IsDirectory && d.Name != "." && d.Name != "..");

            _logger.LogInformation("Found {DirectoryCount} directories in path: {RemotePath}", directories.Count(), remotePath);

            foreach (var dir in directories)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Traversal canceled for storageId: {StorageId}", storageId);
                    break;
                }

                var dirFullPath = dir.FullName.EndsWith("/") ? dir.FullName : dir.FullName + "/";
                _logger.LogInformation("Processing directory: {DirFullPath}", dirFullPath);

                var dirNode = new StorageContentItem
                {
                    Name = dir.Name,
                    FullPath = dirFullPath,
                    Type = "directory",
                    Children = new List<StorageContentItem>()
                };

                parentNode.Children.Add(dirNode);

                // Recursively traverse subdirectories
                await TraverseAndBuildStorageContent(client, dirFullPath, dirNode, storageId, cancellationToken);
            }

            // Fetch files directly under the current remotePath
            var filesDirect = await _context.Files
                .Where(f => f.RemoteScanId == storageId
                            && f.FullPath.StartsWith(remotePath)
                            && !f.IsDeleted)
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Retrieved {FileCount} files directly under path: {RemotePath}", filesDirect.Count, remotePath);

            // Filter in-memory for files directly under remotePath
            var directFiles = filesDirect
                .Where(f => GetDirectoryPath(f.FullPath) == remotePath)
                .ToList();

            _logger.LogInformation("Filtered {DirectFileCount} direct files under path: {RemotePath}", directFiles.Count, remotePath);

            // Add files as children to the current node
            foreach (var file in directFiles)
            {
                var fileNode = new StorageContentItem
                {
                    Name = file.FileName,
                    Size = file.Size,
                    FullPath = file.FullPath,
                    Type = "file",
                    BackupExists = file.BackupExists,
                    Children = new List<StorageContentItem>() // Files have no children
                };
                parentNode.Children.Add(fileNode);
            }

            // Calculate total size for the current directory
            parentNode.Size = parentNode.Children.Sum(c => c.Type == "file" ? c.Size : c.Size);
            _logger.LogInformation("Calculated total size for path {RemotePath}: {TotalSize}", remotePath, parentNode.Size);

            // Determine backupExists for the current directory
            if (parentNode.Children.Any(c => c.Type == "file" && !c.BackupExists))
            {
                parentNode.BackupExists = false;
                _logger.LogInformation("BackupExists set to false for path: {RemotePath}", remotePath);
            }
            else if (parentNode.Children.Any(c => c.Type == "file"))
            {
                parentNode.BackupExists = true;
                _logger.LogInformation("BackupExists set to true for path: {RemotePath}", remotePath);
            }
            else
            {
                parentNode.BackupExists = false; // No files in directory
                _logger.LogInformation("BackupExists set to false (no files) for path: {RemotePath}", remotePath);
            }
        }

        private string GetDirectoryPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return string.Empty;

            // Standardize directory separators
            fullPath = fullPath.Replace('\\', '/');

            // Extract directory name
            var directoryName = Path.GetDirectoryName(fullPath);
            return directoryName != null ? directoryName + "/" : "/";
        }

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
            public DateTimeOffset? LastChecked { get; set; }
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
