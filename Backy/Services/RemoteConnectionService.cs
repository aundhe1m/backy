using Backy.Data;
using Backy.Models;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.Threading.Channels;
using Microsoft.Extensions.FileSystemGlobbing;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using System.Threading;

namespace Backy.Services
{
    public interface IRemoteConnectionService
    {
        Task<bool> ValidateSSHConnection(RemoteConnection connection, string password, string sshKey);
        Task StartScan(Guid remoteConnectionId);
        Task StopScan(Guid remoteConnectionId);
        Task CheckSchedules(CancellationToken cancellationToken);
    }

    public class RemoteConnectionService : IRemoteConnectionService
    {
        private readonly ILogger<RemoteConnectionService> _logger;
        private readonly IDataProtectionProvider _dataProtectionProvider;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly Channel<Guid> _scanQueue = Channel.CreateUnbounded<Guid>();
        private Task? _processingQueueTask;
        private readonly SemaphoreSlim _queueSemaphore = new SemaphoreSlim(1, 1);
        private readonly TimeZoneInfo _timeZoneInfo;

        public RemoteConnectionService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<RemoteConnectionService> logger,
        IDataProtectionProvider dataProtectionProvider,
        ITimeZoneService timeZoneService)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
            _dataProtectionProvider = dataProtectionProvider;
            _timeZoneInfo = timeZoneService.GetConfiguredTimeZone();
        }

        public async Task<bool> ValidateSSHConnection(RemoteConnection connection, string password, string sshKey)
        {
            try
            {
                _logger.LogInformation($"Validating SSH connection for host: {connection.Host}");

                Renci.SshNet.ConnectionInfo connectionInfo;
                if (connection.AuthenticationMethod == RemoteConnection.AuthMethod.Password)
                {
                    _logger.LogInformation("Using password authentication.");
                    connectionInfo = new Renci.SshNet.ConnectionInfo(connection.Host, connection.Port, connection.Username,
                        new PasswordAuthenticationMethod(connection.Username, password));
                }
                else if (connection.AuthenticationMethod == RemoteConnection.AuthMethod.SSHKey)
                {
                    _logger.LogInformation("Using SSH key authentication.");
                    var keyFile = new PrivateKeyFile(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(sshKey)));
                    connectionInfo = new Renci.SshNet.ConnectionInfo(connection.Host, connection.Port, connection.Username,
                        new PrivateKeyAuthenticationMethod(connection.Username, keyFile));
                }
                else
                {
                    throw new InvalidOperationException("Unsupported authentication method.");
                }

                using var client = new SshClient(connectionInfo);
                await Task.Run(() => client.Connect());
                var isConnected = client.IsConnected;
                _logger.LogInformation($"SSH connection status for host {connection.Host}: {isConnected}");
                client.Disconnect();
                return isConnected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SSH Connection validation failed.");
                return false;
            }
        }


        public async Task StartScan(Guid remoteConnectionId)
        {
            // Output log message
            _logger.LogInformation($"Received request to start scan for connection ID: {remoteConnectionId}");

            await _queueSemaphore.WaitAsync();
            try
            {
                _logger.LogInformation($"Adding connection ID {remoteConnectionId} to scan queue.");
                await _scanQueue.Writer.WriteAsync(remoteConnectionId);

                if (_processingQueueTask == null || _processingQueueTask.IsCompleted)
                {
                    _logger.LogInformation("Starting new task to process scan queue.");
                    _processingQueueTask = Task.Run(ProcessQueue);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error while adding connection ID {remoteConnectionId} to scan queue.");
            }
            finally
            {
                _queueSemaphore.Release();
                _logger.LogInformation($"Released semaphore for connection ID {remoteConnectionId}.");
            }
        }

        private async Task ProcessQueue()
        {
            _logger.LogInformation("Started processing scan queue.");

            await foreach (var remoteConnectionId in _scanQueue.Reader.ReadAllAsync())
            {
                _logger.LogInformation($"Processing scan for connection ID: {remoteConnectionId}");

                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var dataProtectionProvider = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();

                    var connection = await dbContext.RemoteConnections.FindAsync(remoteConnectionId);
                    if (connection != null)
                    {
                        try
                        {
                            connection.ScanningActive = true;
                            await dbContext.SaveChangesAsync();
                            _logger.LogInformation($"Set scanning active for connection: {connection.Name}");

                            // Perform the scan
                            await PerformScan(dbContext, dataProtectionProvider, connection);

                            connection.ScanningActive = false;
                            await dbContext.SaveChangesAsync();
                            _logger.LogInformation($"Completed scan for connection: {connection.Name}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error scanning remote connection {connection.Name}");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"Connection ID {remoteConnectionId} not found in database.");
                    }
                }
            }

            _logger.LogInformation("Finished processing scan queue.");
        }


        private async Task PerformScan(ApplicationDbContext dbContext, IDataProtectionProvider dataProtectionProvider, RemoteConnection connection)
        {
            // Output log message
            _logger.LogInformation($"Starting scan for connection: {connection.Name}");

            // Decrypt Password or SSHKey
            var protector = dataProtectionProvider.CreateProtector("RemoteConnectionProtector");
            string password = string.Empty;
            string sshKey = string.Empty;

            if (!string.IsNullOrEmpty(connection.Password))
            {
                try
                {
                    password = protector.Unprotect(connection.Password);
                    _logger.LogInformation("Password decrypted successfully.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to decrypt password.");
                    throw new InvalidOperationException("Invalid stored password. Please update the connection details.");
                }
            }
            if (!string.IsNullOrEmpty(connection.SSHKey))
            {
                try
                {
                    sshKey = protector.Unprotect(connection.SSHKey);
                    _logger.LogInformation("SSH key decrypted successfully.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to decrypt SSH key.");
                    throw new InvalidOperationException("Invalid stored SSH key. Please update the connection details.");
                }
            }

            Renci.SshNet.ConnectionInfo connectionInfo;
            if (connection.AuthenticationMethod == RemoteConnection.AuthMethod.Password)
            {
                connectionInfo = new Renci.SshNet.ConnectionInfo(connection.Host, connection.Port, connection.Username,
                    new PasswordAuthenticationMethod(connection.Username, password));
                _logger.LogInformation("Using password authentication.");
            }
            else if (connection.AuthenticationMethod == RemoteConnection.AuthMethod.SSHKey)
            {
                var keyFile = new PrivateKeyFile(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(sshKey)));
                connectionInfo = new Renci.SshNet.ConnectionInfo(connection.Host, connection.Port, connection.Username,
                    new PrivateKeyAuthenticationMethod(connection.Username, keyFile));
                _logger.LogInformation("Using SSH key authentication.");
            }
            else
            {
                throw new InvalidOperationException("Unsupported authentication method.");
            }

            using var sftpClient = new SftpClient(connectionInfo);
            try
            {
                sftpClient.Connect();
                connection.IsOnline = sftpClient.IsConnected;
                connection.LastChecked = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(); // Changed to dbContext
                _logger.LogInformation("SFTP client connected successfully.");

                // Load filters
                var filters = await dbContext.RemoteFilters
                    .Where(f => f.RemoteConnectionId == connection.RemoteConnectionId)
                    .ToListAsync();

                // Initialize filter counts
                foreach (var filter in filters)
                {
                    filter.FilteredFileCount = 0;
                }

                // Initialize counts
                connection.TotalFiles = 0;
                connection.BackedUpFiles = 0;
                connection.TotalSize = 0;
                connection.BackedUpSize = 0;

                // Build matchers for each filter
                var filterMatchers = filters.ToDictionary(
                    filter => filter,
                    filter =>
                    {
                        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
                        matcher.AddInclude(filter.Pattern);
                        return matcher;
                    });

                // Perform directory listing
                var files = ListRemoteFiles(sftpClient, connection.RemotePath);

                // Build list of existing files
                var existingFiles = await dbContext.RemoteFiles
                    .Where(rf => rf.RemoteConnectionId == connection.RemoteConnectionId)
                    .ToListAsync();

                // Process each file
                foreach (var file in files)
                {
                    var relativePath = GetRelativePath(connection.RemotePath, file.FullName).Replace('\\', '/').TrimStart('/');

                    bool isExcluded = false;

                    foreach (var kvp in filterMatchers)
                    {
                        var filter = kvp.Key;
                        var matcher = kvp.Value;
                        var matchResult = matcher.Match(relativePath);

                        if (matchResult.HasMatches)
                        {
                            // Increment the filter's count
                            filter.FilteredFileCount++;

                            if (!filter.IsInclude)
                            {
                                isExcluded = true;
                            }
                        }
                    }

                    // Count total files and size
                    if (!isExcluded)
                    {
                        connection.TotalFiles++;
                        connection.TotalSize += file.Length;
                    }

                    // Update or add the RemoteFile
                    var existingFile = existingFiles.FirstOrDefault(rf => rf.FullPath == file.FullName);
                    if (existingFile == null)
                    {
                        var remoteFile = new RemoteFile
                        {
                            RemoteConnectionId = connection.RemoteConnectionId,
                            FileName = file.Name,
                            FullPath = file.FullName,
                            Size = file.Length,
                            BackupExists = false, // Update as per your backup logic
                            IsExcluded = isExcluded
                        };
                        dbContext.RemoteFiles.Add(remoteFile);
                    }
                    else
                    {
                        existingFile.Size = file.Length;
                        existingFile.IsDeleted = false;
                        existingFile.IsExcluded = isExcluded;

                        // Count backed up files
                        if (existingFile.BackupExists)
                        {
                            connection.BackedUpFiles++;
                            connection.BackedUpSize += file.Length;
                        }
                    }
                }

                // Mark files as deleted if they no longer exist
                var deletedFiles = existingFiles.Where(ef => !files.Any(f => f.FullName == ef.FullPath));
                foreach (var deletedFile in deletedFiles)
                {
                    deletedFile.IsDeleted = true;
                    _logger.LogInformation($"Marked file as deleted: {deletedFile.FullPath}");
                }

                await dbContext.SaveChangesAsync(); // Changed to dbContext
                _logger.LogInformation("Database changes saved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during SFTP operation.");
            }
            finally
            {
                if (sftpClient.IsConnected)
                {
                    sftpClient.Disconnect();
                    _logger.LogInformation("SFTP client disconnected.");
                }
            }
        }


        private List<ISftpFile> ListRemoteFiles(SftpClient sftpClient, string remotePath)
        {
            var files = new List<ISftpFile>();

            var items = sftpClient.ListDirectory(remotePath);
            foreach (var item in items)
            {
                if (item.Name == "." || item.Name == "..")
                {
                    continue; // Skip logging and processing
                }

                _logger.LogInformation($"Found item: {item.FullName} - IsDirectory: {item.IsDirectory}");

                if (item.IsDirectory)
                {
                    files.AddRange(ListRemoteFiles(sftpClient, item.FullName));
                }
                else if (item.IsRegularFile)
                {
                    files.Add(item);
                }
            }
            return files;
        }


        // RemoteConnectionService.cs
        // private void ApplyFilters(IEnumerable<ISftpFile> files, string rootPath, List<RemoteFilter> filters)
        // {
        //     var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        //     matcher.AddInclude("**/*");

        //     // Initialize filter counts
        //     var filterCounts = filters.ToDictionary(f => f.Pattern, f => 0);

        //     // Build list of file paths relative to the root path
        //     var filePaths = files.Select(f =>
        //     {
        //         var relativePath = GetRelativePath(rootPath, f.FullName).Replace('\\', '/');
        //         return relativePath.TrimStart('/');
        //     }).ToList();

        //     // Apply each filter individually
        //     foreach (var filter in filters)
        //     {
        //         matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        //         matcher.AddInclude(filter.Pattern);

        //         var result = matcher.Match(".", filePaths);
        //         filterCounts[filter.Pattern] = result.Files.Count();
        //     }

        //     // Update filter counts in database
        //     foreach (var filter in filters)
        //     {
        //         filter.FilteredFileCount = filterCounts[filter.Pattern];
        //     }

        //     // Apply exclude patterns to get the final list of files
        //     matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        //     matcher.AddInclude("**/*");
        //     matcher.AddExcludePatterns(filters.Select(f => f.Pattern));
        //     var matchedFiles = matcher.Match(".", filePaths).Files.Select(f => f.Path.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);

        //     var filteredFiles = files.Where(f =>
        //     {
        //         var relativePath = GetRelativePath(rootPath, f.FullName).Replace('\\', '/').TrimStart('/');
        //         return matchedFiles.Contains(relativePath);
        //     });

        //     return filteredFiles;
        // }

        // Helper method to get relative path
        private string GetRelativePath(string rootPath, string fullPath)
        {
            // Ensure consistent path separators
            rootPath = rootPath.Replace('\\', '/').TrimEnd('/');
            fullPath = fullPath.Replace('\\', '/');

            if (fullPath.StartsWith(rootPath + "/"))
            {
                return fullPath.Substring(rootPath.Length + 1);
            }
            else if (fullPath == rootPath)
            {
                return string.Empty;
            }
            else
            {
                // Use Uri to compute relative path
                var uriRoot = new Uri(rootPath + "/");
                var uriFull = new Uri(fullPath);
                return Uri.UnescapeDataString(uriRoot.MakeRelativeUri(uriFull).ToString());
            }
        }



        public Task StopScan(Guid remoteConnectionId)
        {
            // Implement logic to stop an ongoing scan if needed
            _logger.LogInformation($"Received request to stop scan for connection ID: {remoteConnectionId}");
            // Implement cancellation logic if needed
            return Task.CompletedTask;
        }

        public async Task CheckSchedules(CancellationToken cancellationToken)
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var now = DateTimeOffset.UtcNow;
                var connections = await dbContext.RemoteConnections
                    .Include(rc => rc.ScanSchedules)
                    .Where(rc => rc.IsEnabled)
                    .ToListAsync();

                foreach (var connection in connections)
                {
                    foreach (var schedule in connection.ScanSchedules)
                    {
                        if (IsTimeToScan(schedule, now))
                        {
                            await StartScan(connection.RemoteConnectionId);
                        }
                    }
                }
            }
        }

        private bool IsTimeToScan(RemoteScanSchedule schedule, DateTimeOffset now)
        {
            var selectedDays = new List<DayOfWeek>();
            if (schedule.SelectedDayMonday) selectedDays.Add(DayOfWeek.Monday);
            if (schedule.SelectedDayTuesday) selectedDays.Add(DayOfWeek.Tuesday);
            if (schedule.SelectedDayWednesday) selectedDays.Add(DayOfWeek.Wednesday);
            if (schedule.SelectedDayThursday) selectedDays.Add(DayOfWeek.Thursday);
            if (schedule.SelectedDayFriday) selectedDays.Add(DayOfWeek.Friday);
            if (schedule.SelectedDaySaturday) selectedDays.Add(DayOfWeek.Saturday);
            if (schedule.SelectedDaySunday) selectedDays.Add(DayOfWeek.Sunday);

            // Convert current UTC time to the configured time zone
            var nowInConfiguredZone = TimeZoneInfo.ConvertTimeFromUtc(now.UtcDateTime, _timeZoneInfo);

            // Check if the day matches
            if (!selectedDays.Contains(nowInConfiguredZone.DayOfWeek))
                return false;

            var scheduledTime = schedule.ScheduledTime;
            var currentTime = nowInConfiguredZone.TimeOfDay;

            // Check if the current time matches the scheduled time
            return Math.Abs((currentTime - scheduledTime).TotalMinutes) < 1;
        }
    }
}
