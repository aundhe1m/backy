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

        private async Task IndexStorageAsync(Guid storageId, CancellationToken cancellationToken) // Changed to Guid
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var storage = await context.RemoteScans.FindAsync(new object[] { storageId }, cancellationToken);
            if (storage == null)
            {
                _logger.LogWarning("Storage not found during indexing: {Id}", storageId);
                return;
            }

            // Prevent multiple indexing operations on the same storage
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

                var files = new List<FileEntry>();

                _logger.LogInformation("Traversing remote directory: {Path}", storage.RemotePath);
                await TraverseRemoteDirectory(client, storage.RemotePath, files, storage.Id, cancellationToken); // Guid

                _logger.LogInformation("Found {FileCount} files in storage: {Name}", files.Count, storage.Name);

                // Update the database
                foreach (var file in files)
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
                }

                // Mark files that are no longer present as deleted
                var existingFiles = await context.Files
                    .Where(f => f.RemoteScanId == storage.Id)
                    .ToListAsync(cancellationToken);
                foreach (var existingFile in existingFiles)
                {
                    if (!files.Any(f => f.FullPath == existingFile.FullPath))
                    {
                        _logger.LogInformation("Marking file as deleted: {FullPath}", existingFile.FullPath);
                        existingFile.IsDeleted = true;
                        context.Files.Update(existingFile);
                    }
                }

                await context.SaveChangesAsync(cancellationToken);

                client.Disconnect();
                _logger.LogInformation("Successfully indexed storage: {Name}", storage.Name);
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

        private async Task TraverseRemoteDirectory(SftpClient client, string remotePath, List<FileEntry> files, Guid storageId, CancellationToken cancellationToken) // Changed to Guid
        {
            var items = client.ListDirectory(remotePath);
            foreach (var item in items)
            {
                if (item.Name == "." || item.Name == "..")
                    continue;

                var fullPath = item.FullName;
                if (item.IsDirectory)
                {
                    _logger.LogInformation("Traversing directory: {FullPath}", fullPath);
                    await TraverseRemoteDirectory(client, fullPath, files, storageId, cancellationToken);
                }
                else if (item.IsRegularFile)
                {
                    _logger.LogInformation("Found file: {FullPath}", fullPath);
                    files.Add(new FileEntry
                    {
                        RemoteScanId = storageId,
                        FileName = item.Name,
                        FullPath = fullPath,
                        Size = item.Attributes.Size,
                        LastModified = item.LastWriteTime
                    });
                }

                // Respect cancellation
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
