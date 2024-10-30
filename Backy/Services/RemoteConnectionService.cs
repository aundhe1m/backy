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
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<RemoteConnectionService> _logger;
        private readonly IDataProtectionProvider _dataProtectionProvider;
        private readonly Channel<Guid> _scanQueue = Channel.CreateUnbounded<Guid>();
        private Task? _processingQueueTask;
        private readonly SemaphoreSlim _queueSemaphore = new SemaphoreSlim(1, 1);

        public RemoteConnectionService(ApplicationDbContext dbContext, ILogger<RemoteConnectionService> logger, IDataProtectionProvider dataProtectionProvider)
        {
            _dbContext = dbContext;
            _logger = logger;
            _dataProtectionProvider = dataProtectionProvider;
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

                var connection = await _dbContext.RemoteConnections.FindAsync(remoteConnectionId);
                if (connection != null)
                {
                    try
                    {
                        connection.ScanningActive = true;
                        await _dbContext.SaveChangesAsync();
                        _logger.LogInformation($"Set scanning active for connection: {connection.Name}");

                        // Perform the scan
                        await PerformScan(connection);

                        connection.ScanningActive = false;
                        await _dbContext.SaveChangesAsync();
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

            _logger.LogInformation("Finished processing scan queue.");
        }

        private async Task PerformScan(RemoteConnection connection)
        {
            // Output log message
            _logger.LogInformation($"Starting scan for connection: {connection.Name}");

            // Decrypt Password or SSHKey
            var protector = _dataProtectionProvider.CreateProtector("RemoteConnectionProtector");
            string password = string.Empty;
            string sshKey = string.Empty;

            if (!string.IsNullOrEmpty(connection.Password))
            {
                password = protector.Unprotect(connection.Password);
                _logger.LogInformation("Password decrypted successfully.");
            }
            if (!string.IsNullOrEmpty(connection.SSHKey))
            {
                sshKey = protector.Unprotect(connection.SSHKey);
                _logger.LogInformation("SSH key decrypted successfully.");
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
                connection.LastChecked = DateTimeOffset.Now;
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("SFTP client connected successfully.");

                // Load filters
                var filters = await _dbContext.RemoteFilters
                    .Where(f => f.RemoteConnectionId == connection.RemoteConnectionId)
                    .ToListAsync();
                _logger.LogInformation("Loaded filters from database.");

                var excludePatterns = filters.Where(f => !f.IsInclude).Select(f => f.Pattern).ToList();

                // Perform directory listing and update RemoteFiles
                var files = ListRemoteFiles(sftpClient, connection.RemotePath);
                _logger.LogInformation($"Listed {files.Count} files from remote path.");

                // Apply filters using Microsoft.Extensions.FileSystemGlobbing
                var filteredFiles = ApplyFilters(files, connection.RemotePath, excludePatterns);
                _logger.LogInformation($"Filtered files count: {filteredFiles.Count()}");

                // Update RemoteFiles in the database
                var existingFiles = await _dbContext.RemoteFiles
                    .Where(rf => rf.RemoteConnectionId == connection.RemoteConnectionId)
                    .ToListAsync();
                _logger.LogInformation("Loaded existing remote files from database.");

                foreach (var file in filteredFiles)
                {
                    var existingFile = existingFiles.FirstOrDefault(rf => rf.FullPath == file.FullName);
                    if (existingFile == null)
                    {
                        // Add new file
                        var remoteFile = new RemoteFile
                        {
                            RemoteConnectionId = connection.RemoteConnectionId,
                            FileName = file.Name,
                            FullPath = file.FullName,
                            Size = file.Length,
                            BackupExists = false // Update as per your backup logic
                        };
                        _dbContext.RemoteFiles.Add(remoteFile);
                        _logger.LogInformation($"Added new file: {file.FullName}");
                    }
                    else
                    {
                        // Update existing file
                        existingFile.Size = file.Length;
                        existingFile.IsDeleted = false;
                        _logger.LogInformation($"Updated existing file: {file.FullName}");
                    }
                }

                // Mark files as deleted if they no longer exist
                var deletedFiles = existingFiles.Where(ef => !filteredFiles.Any(f => f.FullName == ef.FullPath));
                foreach (var deletedFile in deletedFiles)
                {
                    deletedFile.IsDeleted = true;
                    _logger.LogInformation($"Marked file as deleted: {deletedFile.FullPath}");
                }

                await _dbContext.SaveChangesAsync();
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
        private IEnumerable<ISftpFile> ApplyFilters(IEnumerable<ISftpFile> files, string rootPath, List<string> excludePatterns)
        {
            var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);

            // Include all files by default
            matcher.AddInclude("**/*");

            // Apply exclude patterns
            if (excludePatterns.Any())
            {
                matcher.AddExcludePatterns(excludePatterns);
            }

            // Build list of file paths relative to the root path and remove leading slashes
            var filePaths = files.Select(f =>
            {
                var relativePath = GetRelativePath(rootPath, f.FullName).Replace('\\', '/');
                return relativePath.TrimStart('/');
            }).ToList();

            _logger.LogInformation("Relative file paths:");
            foreach (var path in filePaths)
            {
                _logger.LogInformation($"- {path}");
            }

            // Perform matching with root directory "."
            var result = matcher.Match(".", filePaths);

            _logger.LogInformation("Matched file paths:");
            foreach (var file in result.Files)
            {
                _logger.LogInformation($"- {file.Path}");
            }

            // Get matched file paths
            var matchedFilePaths = result.Files
                .Select(f => f.Path.Replace('\\', '/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Filter the files based on matched relative paths
            var filteredFiles = files.Where(f =>
            {
                var relativePath = GetRelativePath(rootPath, f.FullName).Replace('\\', '/').TrimStart('/');
                return matchedFilePaths.Contains(relativePath);
            });

            _logger.LogInformation($"Filtered files count: {filteredFiles.Count()}");

            return filteredFiles;
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
            var now = DateTimeOffset.Now;
            var connections = await _dbContext.RemoteConnections
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

            if (!selectedDays.Contains(now.DayOfWeek))
                return false;

            var scheduledTime = TimeSpan.FromMinutes(schedule.TimeOfDayMinutes);
            var currentTime = now.TimeOfDay;

            return Math.Abs((currentTime - scheduledTime).TotalMinutes) < 1;
        }
    }
}
