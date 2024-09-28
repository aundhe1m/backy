using Backy.Data;
using Backy.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Renci.SshNet;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace Backy.Services
{
    public class FileIndexingService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IDataProtector _protector;
        private readonly ILogger<FileIndexingService> _logger;
        private readonly IIndexingQueue _indexingQueue;
        private readonly TimeSpan _indexingInterval = TimeSpan.FromHours(6); // Adjust as needed

        public FileIndexingService(
            IServiceScopeFactory scopeFactory,
            IDataProtectionProvider provider,
            ILogger<FileIndexingService> logger,
            IIndexingQueue indexingQueue)
        {
            _scopeFactory = scopeFactory;
            _protector = provider.CreateProtector("Backy.RemoteScan");
            _logger = logger;
            _indexingQueue = indexingQueue;
        }

        private async Task CheckScheduledIndexingAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var now = DateTime.UtcNow;
            int nowDayOfWeek = (int)now.DayOfWeek;
            int nowMinutes = now.Hour * 60 + now.Minute;

            var schedules = await context.IndexSchedules
                .Include(s => s.RemoteScan)
                .Where(s => s.DayOfWeek == nowDayOfWeek && s.TimeOfDayMinutes == nowMinutes)
                .ToListAsync(cancellationToken);

            foreach (var schedule in schedules)
            {
                _logger.LogInformation("Scheduled indexing for storage: {Id}", schedule.RemoteScanId);
                _indexingQueue.EnqueueIndexing(schedule.RemoteScanId); // Guid
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("FileIndexingService is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                // Check for scheduled indexing every minute
                await CheckScheduledIndexingAsync(stoppingToken);

                _logger.LogInformation("Waiting for indexing signals or periodic interval.");

                var delayTask = Task.Delay(_indexingInterval, stoppingToken);
                var dequeueTask = _indexingQueue.DequeueAsync(stoppingToken);

                var completedTask = await Task.WhenAny(delayTask, dequeueTask);

                if (completedTask == dequeueTask)
                {
                    var storageId = await dequeueTask; // Guid
                    _logger.LogInformation("Processing indexing for storage: {Id}", storageId);
                    try
                    {
                        await IndexStorageAsync(storageId, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during indexing for storage: {Id}", storageId);
                    }
                }
                else
                {
                    // Periodic indexing
                    _logger.LogInformation("Starting periodic indexing of all enabled storages.");
                    try
                    {
                        await IndexAllStoragesAsync(stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during periodic indexing.");
                    }
                }
            }

            _logger.LogInformation("FileIndexingService is stopping.");
        }

        private async Task IndexAllStoragesAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var storages = await context.RemoteScans
                .Where(s => s.IsEnabled && !s.IsIndexing)
                .ToListAsync(cancellationToken);

            foreach (var storage in storages)
            {
                _logger.LogInformation("Enqueueing periodic indexing for storage: {Id}", storage.Id);
                _indexingQueue.EnqueueIndexing(storage.Id); // Guid
            }
        }

        private async Task IndexStorageAsync(Guid storageId, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var storage = await context.RemoteScans.FindAsync(new object[] { storageId }, cancellationToken);
            if (storage == null)
            {
                _logger.LogWarning("Storage not found during indexing: {Id}", storageId);
                return;
            }

            if (storage.IsIndexing)
            {
                _logger.LogInformation("Storage {Id} is already being indexed.", storageId);
                return;
            }

            storage.IsIndexing = true;
            context.RemoteScans.Update(storage);
            await context.SaveChangesAsync(cancellationToken);

            try
            {
                using var client = CreateSftpClient(storage);
                _logger.LogInformation("Connecting to storage: {Name} at {Host}:{Port}", storage.Name, storage.Host, storage.Port);
                client.Connect();

                _logger.LogInformation("Connected to storage: {Name}", storage.Name);

                var files = new Dictionary<string, FileEntry>();

                _logger.LogInformation("Traversing remote directory: {Path}", storage.RemotePath);
                await TraverseRemoteDirectory(client, storage.RemotePath, files, storage.Id, cancellationToken);

                _logger.LogInformation("Found {FileCount} files in storage: {Name}", files.Count, storage.Name);

                // Update the database
                foreach (var file in files.Values)
                {
                    var existingFile = await context.Files
                        .FirstOrDefaultAsync(f => f.RemoteScanId == storage.Id && f.FullPath == file.FullPath, cancellationToken);
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

                    try
                    {
                        await context.SaveChangesAsync(cancellationToken);
                    }
                    catch (DbUpdateException ex)
                    {
                        _logger.LogError(ex, "Error saving file: {FullPath}", file.FullPath);
                        // Optionally, handle specific exceptions or implement retry logic
                    }
                }

                // Mark files that are no longer present as deleted
                var existingFiles = await context.Files
                    .Where(f => f.RemoteScanId == storage.Id)
                    .ToListAsync(cancellationToken);
                foreach (var existingFile in existingFiles)
                {
                    if (!files.ContainsKey(existingFile.FullPath))
                    {
                        _logger.LogInformation("Marking file as deleted: {FullPath}", existingFile.FullPath);
                        existingFile.IsDeleted = true;
                        context.Files.Update(existingFile);
                    }
                }

                await context.SaveChangesAsync(cancellationToken);

                client.Disconnect();
                _logger.LogInformation("Successfully indexed storage: {Name}", storage.Name);

                // Generate and store storageContent after indexing
                await GenerateAndStoreStorageContentAsync(context, storageId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error indexing storage: {Name}", storage.Name);
            }
            finally
            {
                storage.IsIndexing = false;
                context.RemoteScans.Update(storage);
                await context.SaveChangesAsync(cancellationToken);
            }
        }

        private async Task GenerateAndStoreStorageContentAsync(ApplicationDbContext context, Guid storageId, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Generating storage content for storageId: {StorageId}", storageId);

            // Fetch the RemoteScan to get the root path
            var storage = await context.RemoteScans.FindAsync(new object[] { storageId }, cancellationToken);
            if (storage == null)
            {
                _logger.LogWarning("Storage not found for storageId: {StorageId}", storageId);
                return;
            }

            string rootPath = storage.RemotePath.EndsWith("/") ? storage.RemotePath : storage.RemotePath + "/";

            // Fetch all files for the storage
            var files = await context.Files
                .Where(f => f.RemoteScanId == storageId && !f.IsDeleted)
                .ToListAsync(cancellationToken);

            // Build the storageContent tree
            var rootItem = new StorageContentItem
            {
                Name = Path.GetFileName(rootPath.TrimEnd('/')) ?? "/",
                FullPath = rootPath.TrimEnd('/'),
                Type = "directory",
                Children = new List<StorageContentItem>()
            };

            foreach (var file in files)
            {
                AddFileToStorageContent(rootItem, file, rootPath);
            }

            // Calculate sizes and backup statuses
            CalculateDirectoryProperties(rootItem);

            // Serialize the storageContent to JSON
            var contentJson = JsonSerializer.Serialize(rootItem);

            // Store or update the StorageContent in the database
            var storageContent = await context.StorageContents.FindAsync(new object[] { storageId }, cancellationToken);
            if (storageContent == null)
            {
                storageContent = new StorageContent
                {
                    RemoteScanId = storageId,
                    ContentJson = contentJson,
                    LastUpdated = DateTime.UtcNow
                };
                context.StorageContents.Add(storageContent);
            }
            else
            {
                storageContent.ContentJson = contentJson;
                storageContent.LastUpdated = DateTime.UtcNow;
                context.StorageContents.Update(storageContent);
            }

            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Storage content generated and stored for storageId: {StorageId}", storageId);
        }


        private void AddFileToStorageContent(StorageContentItem rootItem, FileEntry file, string rootPath)
        {
            // Normalize the file path
            var relativePath = file.FullPath.Substring(rootPath.Length).Trim('/');
            var pathParts = relativePath.Split('/');
            var currentNode = rootItem;

            for (int i = 0; i < pathParts.Length; i++)
            {
                var part = pathParts[i];
                var isFile = (i == pathParts.Length - 1);
                var existingChild = currentNode.Children.FirstOrDefault(c => c.Name == part);

                if (existingChild == null)
                {
                    var newItem = new StorageContentItem
                    {
                        Name = part,
                        FullPath = Path.Combine(currentNode.FullPath, part).Replace("\\", "/"),
                        Type = isFile ? "file" : "directory",
                        Size = isFile ? file.Size : 0,
                        BackupExists = isFile ? file.BackupExists : false,
                        Children = new List<StorageContentItem>()
                    };

                    currentNode.Children.Add(newItem);
                    currentNode = newItem;
                }
                else
                {
                    currentNode = existingChild;

                    // If this is a file node, update its properties
                    if (isFile && currentNode.Type == "file")
                    {
                        currentNode.Size = file.Size;
                        currentNode.BackupExists = file.BackupExists;
                    }
                }
            }
        }

        private void CalculateDirectoryProperties(StorageContentItem node)
        {
            if (node.Type == "file")
            {
                // For files, size and backup status are already set
                return;
            }

            long totalSize = 0;
            bool backupExists = true;

            foreach (var child in node.Children)
            {
                CalculateDirectoryProperties(child);

                totalSize += child.Size;

                if (!child.BackupExists)
                {
                    backupExists = false;
                }
            }

            node.Size = totalSize;
            node.BackupExists = backupExists;
        }


        private async Task TraverseRemoteDirectory(SftpClient client, string remotePath, Dictionary<string, FileEntry> files, Guid storageId, CancellationToken cancellationToken)
        {
            var items = client.ListDirectory(remotePath);
            foreach (var item in items)
            {
                if (item.Name == "." || item.Name == "..")
                    continue;

                var fullPath = item.FullName;

                if (item.IsRegularFile)
                {
                    if (!files.ContainsKey(fullPath))
                    {
                        files[fullPath] = new FileEntry
                        {
                            RemoteScanId = storageId,
                            FileName = item.Name,
                            FullPath = fullPath,
                            Size = item.Attributes.Size,
                            LastModified = item.LastWriteTime
                        };
                    }
                }
                else if (item.IsDirectory)
                {
                    await TraverseRemoteDirectory(client, fullPath, files, storageId, cancellationToken);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Indexing canceled for storage: {Id}", storageId);
                    break;
                }
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

        private string Decrypt(string? input)
        {
            return input != null ? _protector.Unprotect(input) : string.Empty;
        }
    }
}
