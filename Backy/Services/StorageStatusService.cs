using Backy.Data;
using Backy.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Renci.SshNet;
using System.Threading.Tasks;

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
            _protector = provider.CreateProtector("Backy.RemoteScan");
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
            var storages = await context.RemoteScans.ToListAsync(cancellationToken);

            var tasks = storages.Select(storage => Task.Run(async () =>
            {
                using var innerScope = _scopeFactory.CreateScope();
                var innerContext = innerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await StorageStatusChecker.CheckAndUpdateStorageStatusAsync(storage, innerContext, _protector, _logger);
            }, cancellationToken));

            await Task.WhenAll(tasks);
        }
    }

    public static class StorageStatusChecker
    {
        public static async Task CheckAndUpdateStorageStatusAsync(RemoteScan storage, ApplicationDbContext context, IDataProtector protector, ILogger logger)
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
                // Expanded the logs to include more connection details
                logger.LogError(ex, "Error connecting to storage. Id: {Id}, Name: {Name}, Host: {Host}, Port: {Port}, Username: {Username}, RemotePath: {RemotePath}",
                    storage.Id, storage.Name, storage.Host, storage.Port, storage.Username, storage.RemotePath);
            }

            storage.Status = isOnline ? "Online" : "Offline";
            storage.LastChecked = DateTimeOffset.UtcNow;

            context.RemoteScans.Update(storage);
            await context.SaveChangesAsync();
        }

        private static SftpClient CreateSftpClient(RemoteScan storage, IDataProtector protector)
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