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
    public enum ScanResult
    {
        ScanQueued,
        ScanningOngoing,
        ScanAlreadyQueued
    }
    public interface IRemoteConnectionService
    {
        Task<bool> ValidateSSHConnection(RemoteConnection connection, string password, string sshKey);
        Task<ScanResult> StartScan(Guid remoteConnectionId);
        Task StopScan(Guid remoteConnectionId);
        Task CheckSchedules(CancellationToken cancellationToken);
    }

    public class RemoteConnectionService : IRemoteConnectionService
    {
        private readonly ILogger<RemoteConnectionService> _logger;
        private readonly IDataProtectionProvider _dataProtectionProvider;
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly Channel<Guid> _scanQueue = Channel.CreateUnbounded<Guid>();
        private Task? _processingQueueTask;
        private readonly SemaphoreSlim _queueSemaphore = new SemaphoreSlim(1, 1);
        private readonly TimeZoneInfo _timeZoneInfo;
        private readonly ConnectionEventService _connectionEventService;

        public RemoteConnectionService(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        ILogger<RemoteConnectionService> logger,
        IDataProtectionProvider dataProtectionProvider,
        ITimeZoneService timeZoneService,
        ConnectionEventService connectionEventService)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dataProtectionProvider = dataProtectionProvider ?? throw new ArgumentNullException(nameof(dataProtectionProvider));
            _timeZoneInfo = timeZoneService.GetConfiguredTimeZone();
            _connectionEventService = connectionEventService ?? throw new ArgumentNullException(nameof(connectionEventService));
        }

        private enum ScanState
        {
            Queued,
            Active
        }

        private readonly Dictionary<Guid, ScanState> _scanStatus = new Dictionary<Guid, ScanState>();


        public async Task<bool> ValidateSSHConnection(RemoteConnection connection, string password, string sshKey)
        {
            try
            {
                _logger.LogDebug($"Validating SSH connection for host: {connection.Host}");

                Renci.SshNet.ConnectionInfo connectionInfo;
                if (connection.AuthenticationMethod == RemoteConnection.AuthMethod.Password)
                {
                    _logger.LogDebug("Using password authentication.");
                    connectionInfo = new Renci.SshNet.ConnectionInfo(connection.Host, connection.Port, connection.Username,
                        new PasswordAuthenticationMethod(connection.Username, password));
                }
                else if (connection.AuthenticationMethod == RemoteConnection.AuthMethod.SSHKey)
                {
                    _logger.LogDebug("Using SSH key authentication.");
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
                _logger.LogDebug($"SSH connection status for host {connection.Host}: {isConnected}");
                client.Disconnect();
                return isConnected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SSH Connection validation failed.");
                return false;
            }
        }


        public async Task<ScanResult> StartScan(Guid remoteConnectionId)
        {
            await _queueSemaphore.WaitAsync();
            try
            {
                if (_scanStatus.TryGetValue(remoteConnectionId, out var state))
                {
                    if (state == ScanState.Active)
                    {
                        _logger.LogDebug($"Scan already active for connection {remoteConnectionId}");
                        return ScanResult.ScanningOngoing;
                    }
                    else if (state == ScanState.Queued)
                    {
                        _logger.LogDebug($"Scan already queued for connection {remoteConnectionId}");
                        return ScanResult.ScanAlreadyQueued;
                    }
                }
                // Not in the dictionary yet: mark as queued and add to channel.
                _scanStatus.Add(remoteConnectionId, ScanState.Queued);
                _logger.LogDebug($"Adding connection ID {remoteConnectionId} to scan queue.");
                await _scanQueue.Writer.WriteAsync(remoteConnectionId);

                if (_processingQueueTask == null || _processingQueueTask.IsCompleted)
                {
                    _logger.LogDebug("Starting new task to process scan queue.");
                    _processingQueueTask = Task.Run(ProcessQueue);
                }
                return ScanResult.ScanQueued;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error while adding connection ID {remoteConnectionId} to scan queue.");
                throw;
            }
            finally
            {
                _queueSemaphore.Release();
            }
        }


        private async Task ProcessQueue()
        {
            _logger.LogDebug("Started processing scan queue.");

            await foreach (var remoteConnectionId in _scanQueue.Reader.ReadAllAsync())
            {
                // Mark the scan as Active.
                await _queueSemaphore.WaitAsync();
                try
                {
                    if (_scanStatus.ContainsKey(remoteConnectionId))
                    {
                        _scanStatus[remoteConnectionId] = ScanState.Active;
                    }
                }
                finally
                {
                    _queueSemaphore.Release();
                }

                try
                {
                    // Get the connection using a DbContext created from the factory
                    await using var dbContext = await _contextFactory.CreateDbContextAsync();
                    var connection = await dbContext.RemoteConnections.FindAsync(remoteConnectionId);
                    if (connection != null)
                    {
                        try
                        {
                            connection.ScanningActive = true;
                            await dbContext.SaveChangesAsync();
                            _connectionEventService.NotifyConnectionUpdated(connection.RemoteConnectionId);
                            _logger.LogDebug($"Set scanning active for connection: {connection.Name}");

                            // Perform the scan
                            await PerformScan(connection);

                            // Update connection using a fresh DbContext
                            await using var updateContext = await _contextFactory.CreateDbContextAsync();
                            connection = await updateContext.RemoteConnections.FindAsync(remoteConnectionId);
                            if (connection != null)
                            {
                                connection.ScanningActive = false;
                                await updateContext.SaveChangesAsync();
                                _connectionEventService.NotifyConnectionUpdated(connection.RemoteConnectionId);
                                _logger.LogDebug($"Completed scan for connection: {connection.Name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error scanning remote connection {connection.Name}");
                            
                            // Ensure we update the scanning status even if there's an error
                            try
                            {
                                await using var errorContext = await _contextFactory.CreateDbContextAsync();
                                connection = await errorContext.RemoteConnections.FindAsync(remoteConnectionId);
                                if (connection != null)
                                {
                                    connection.ScanningActive = false;
                                    await errorContext.SaveChangesAsync();
                                    _connectionEventService.NotifyConnectionUpdated(connection.RemoteConnectionId);
                                }
                            }
                            catch (Exception updateEx)
                            {
                                _logger.LogError(updateEx, "Error updating connection scanning status after an error");
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"Connection ID {remoteConnectionId} not found in database.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing scan for connection ID {remoteConnectionId}");
                }

                // Remove the connection from the tracking dictionary.
                await _queueSemaphore.WaitAsync();
                try
                {
                    _scanStatus.Remove(remoteConnectionId);
                }
                finally
                {
                    _queueSemaphore.Release();
                }
            }

            _logger.LogDebug("Finished processing scan queue.");
        }

        private async Task PerformScan(RemoteConnection connection)
        {
            // Create a new DbContext for this scan operation
            await using var dbContext = await _contextFactory.CreateDbContextAsync();
            
            // Output log message
            _logger.LogInformation($"Starting scan for connection: {connection.Name}");

            // Decrypt Password or SSHKey
            var protector = _dataProtectionProvider.CreateProtector("RemoteConnectionProtector");
            string password = string.Empty;
            string sshKey = string.Empty;

            if (!string.IsNullOrEmpty(connection.Password))
            {
                try
                {
                    password = protector.Unprotect(connection.Password);
                    _logger.LogDebug("Password decrypted successfully.");
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
                    _logger.LogDebug("SSH key decrypted successfully.");
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
                _logger.LogDebug("Using password authentication.");
            }
            else if (connection.AuthenticationMethod == RemoteConnection.AuthMethod.SSHKey)
            {
                var keyFile = new PrivateKeyFile(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(sshKey)));
                connectionInfo = new Renci.SshNet.ConnectionInfo(connection.Host, connection.Port, connection.Username,
                    new PrivateKeyAuthenticationMethod(connection.Username, keyFile));
                _logger.LogDebug("Using SSH key authentication.");
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
                await dbContext.SaveChangesAsync();
                _logger.LogDebug("SFTP client connected successfully.");

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
                    var existingFile = existingFiles.FirstOrDefault(rf => rf.RelativePath == relativePath);
                    if (existingFile == null)
                    {
                        var remoteFile = new RemoteFile
                        {
                            RemoteConnectionId = connection.RemoteConnectionId,
                            FileName = file.Name,
                            RelativePath = relativePath,
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
                var remotePaths = files.Select(f => GetRelativePath(connection.RemotePath, f.FullName).Replace('\\', '/').TrimStart('/')).ToHashSet();
                var deletedFiles = existingFiles.Where(ef => !remotePaths.Contains(ef.RelativePath));
                
                foreach (var deletedFile in deletedFiles)
                {
                    deletedFile.IsDeleted = true;
                    _logger.LogDebug($"Marked file as deleted: {deletedFile.RelativePath}");
                }

                await dbContext.SaveChangesAsync();
                _logger.LogDebug("Database changes saved successfully.");
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
                    _logger.LogDebug("SFTP client disconnected.");
                }
            }

            // Log completion
            _logger.LogInformation($"Scan completed for connection: {connection.Name}");
            _connectionEventService.NotifyConnectionUpdated(connection.RemoteConnectionId);
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

                _logger.LogDebug($"Found item: {item.FullName} - IsDirectory: {item.IsDirectory}");

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
            await using var dbContext = await _contextFactory.CreateDbContextAsync();

            var now = DateTimeOffset.UtcNow;
            var connections = await dbContext.RemoteConnections
                .Include(rc => rc.ScanSchedules)
                .Where(rc => rc.IsEnabled)
                .ToListAsync(cancellationToken);

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
