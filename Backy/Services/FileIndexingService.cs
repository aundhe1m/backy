using Backy.Data;
using Backy.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Renci.SshNet;

namespace Backy.Services
{
    public class FileIndexingService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IDataProtector _protector;
        private readonly ILogger<FileIndexingService> _logger;
        private readonly TimeSpan _indexingInterval = TimeSpan.FromHours(6); // Adjust as needed

        public FileIndexingService(IServiceScopeFactory scopeFactory, IDataProtectionProvider provider, ILogger<FileIndexingService> logger)
        {
            _scopeFactory = scopeFactory;
            _protector = provider.CreateProtector("Backy.RemoteStorage");
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("FileIndexingService is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                await IndexAllStoragesAsync(stoppingToken);
                await Task.Delay(_indexingInterval, stoppingToken);
            }
        }

        private async Task IndexAllStoragesAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var storages = await context.RemoteStorages.Where(s => s.IsEnabled && !s.IsIndexing).ToListAsync(cancellationToken);

            foreach (var storage in storages)
            {
                _ = Task.Run(() => IndexStorageAsync(storage.Id), cancellationToken);
            }
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
                storage.IsIndexing = false;
                context.RemoteStorages.Update(storage);
                await context.SaveChangesAsync();
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
                    _logger.LogInformation("Traversing directory: {FullPath}", fullPath);
                    await TraverseRemoteDirectory(client, fullPath, files, storageId);
                }
                else if (item.IsRegularFile)
                {
                    _logger.LogInformation("Found file: {FullPath}", fullPath);
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
}
