using Backy.Data;
using Backy.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Renci.SshNet;

namespace Backy.Services
{
    public class StorageStatusService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<StorageStatusService> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);
        private readonly IDataProtector _protector;

        public StorageStatusService(IServiceScopeFactory scopeFactory, IDataProtectionProvider provider, ILogger<StorageStatusService> logger)
        {
            _scopeFactory = scopeFactory;
            _protector = provider.CreateProtector("Backy.RemoteStorage");
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("StorageStatusService is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                await CheckStorageStatuses(stoppingToken);
                await Task.Delay(_checkInterval, stoppingToken);
            }
        }

        private async Task CheckStorageStatuses(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var storages = await context.RemoteStorages.ToListAsync(cancellationToken);

            var tasks = storages.Select(storage => StorageStatusChecker.CheckAndUpdateStorageStatusAsync(storage, context, _protector, _logger));
            await Task.WhenAll(tasks);
        }
    }

    public static class StorageStatusChecker
    {
        public static async Task CheckAndUpdateStorageStatusAsync(RemoteStorage storage, ApplicationDbContext context, IDataProtector protector, ILogger logger)
        {
            bool isOnline = false;

            try
            {
                using var client = CreateSftpClient(storage, protector);
                client.Connect();
                isOnline = client.IsConnected;
                client.Disconnect();
                logger.LogInformation("Storage {Name} is online.", storage.Name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error connecting to {Name}", storage.Name);
            }

            storage.Status = isOnline ? "Online" : "Offline";
            storage.LastChecked = DateTime.UtcNow;

            context.RemoteStorages.Update(storage);
            await context.SaveChangesAsync();
        }

        private static SftpClient CreateSftpClient(RemoteStorage storage, IDataProtector protector)
        {
            if (storage.AuthenticationMethod == "Password")
            {
                return new SftpClient(storage.Host, storage.Port, storage.Username, protector.Unprotect(storage.Password ?? string.Empty));
            }
            else
            {
                using var keyStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(protector.Unprotect(storage.SSHKey ?? string.Empty)));
                var keyFile = new PrivateKeyFile(keyStream);
                var keyFiles = new[] { keyFile };
                var authMethod = new PrivateKeyAuthenticationMethod(storage.Username, keyFiles);
                var connectionInfo = new Renci.SshNet.ConnectionInfo(storage.Host, storage.Port, storage.Username, authMethod);
                return new SftpClient(connectionInfo);
            }
        }
    }
}
