using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Backy.Agent.Services.Core
{
    /// <summary>
    /// Interface for executing system commands and returning structured results.
    /// Centralizes all external command execution in the application.
    /// </summary>
    /// <remarks>
    /// This service is responsible for:
    /// - Executing shell commands in a controlled manner
    /// - Handling command execution errors
    /// - Providing standardized output format
    /// - Supporting both synchronous and asynchronous command execution
    /// - Process management (checking if running, killing)
    /// </remarks>
    public interface ISystemCommandService
    {
        /// <summary>
        /// Executes a shell command and returns the result.
        /// </summary>
        /// <param name="command">The command to execute</param>
        /// <param name="sudo">Whether to execute the command with sudo</param>
        /// <returns>A CommandResult containing execution results</returns>
        Task<CommandResult> ExecuteCommandAsync(string command, bool sudo = false);
        
        /// <summary>
        /// Executes a shell command with output parsing and returns the result.
        /// </summary>
        /// <param name="command">The command to execute</param>
        /// <param name="sudo">Whether to execute the command with sudo</param>
        /// <returns>A CommandResult containing execution results</returns>
        Task<CommandResult> ExecuteCommandWithOutputAsync(string command, bool sudo = false);
        
        /// <summary>
        /// Checks if a process with the specified PID is running.
        /// </summary>
        /// <param name="pid">The process ID to check</param>
        /// <returns>True if the process is running, false otherwise</returns>
        Task<bool> IsProcessRunningAsync(int pid);
        
        /// <summary>
        /// Attempts to kill a process with the specified PID.
        /// </summary>
        /// <param name="pid">The process ID to kill</param>
        /// <param name="force">Whether to force kill the process (SIGKILL)</param>
        /// <returns>True if the process was killed successfully, false otherwise</returns>
        Task<bool> KillProcessAsync(int pid, bool force = false);
    }

    /// <summary>
    /// Implementation of the system command service that executes shell commands and returns structured results.
    /// </summary>
    /// <remarks>
    /// This class centralizes all external command execution in the application, providing:
    /// - Standardized error handling and logging
    /// - Command execution with timeout support
    /// - Process management capabilities
    /// - Consistent output formatting
    /// 
    /// This implementation replaces scattered command execution throughout the application,
    /// enforcing a single, well-tested approach to interacting with the system.
    /// </remarks>
    public class SystemCommandService : ISystemCommandService
    {
        private readonly ILogger<SystemCommandService> _logger;
        
        public SystemCommandService(ILogger<SystemCommandService> logger)
        {
            _logger = logger;
        }
        
        /// <inheritdoc />
        public async Task<CommandResult> ExecuteCommandAsync(string command, bool sudo = false)
        {
            var actualCommand = sudo ? $"sudo {command}" : command;
            _logger.LogDebug("Executing command: {Command}", actualCommand);

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "bash",
                        Arguments = $"-c \"{actualCommand}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string stdout = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                int exitCode = process.ExitCode;
                string output = stdout + stderr;

                _logger.LogDebug("Command exit code: {ExitCode}, Output length: {Length}", exitCode, output.Length);
                if (output.Length < 1000)
                {
                    _logger.LogTrace("Command output: {Output}", output);
                }
                
                // Clean the output before returning
                string cleanedOutput = CleanCommandOutput(output);

                return new CommandResult
                {
                    Success = exitCode == 0,
                    ExitCode = exitCode,
                    Output = cleanedOutput.Trim(),
                    Command = actualCommand,
                    StdOut = stdout.Trim(),
                    StdErr = stderr.Trim()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing command: {Command}", actualCommand);
                return new CommandResult
                {
                    Success = false,
                    ExitCode = -1,
                    Output = $"Error: {ex.Message}",
                    Command = actualCommand,
                    Error = ex
                };
            }
        }
        
        /// <inheritdoc />
        public async Task<CommandResult> ExecuteCommandWithOutputAsync(string command, bool sudo = false)
        {
            // This method provides the same functionality as ExecuteCommandAsync
            // but is maintained as a separate method for API compatibility
            return await ExecuteCommandAsync(command, sudo);
        }
        
        /// <inheritdoc />
        public async Task<bool> IsProcessRunningAsync(int pid)
        {
            try
            {
                // Check if process exists using ps command
                var result = await ExecuteCommandAsync($"ps -p {pid} -o pid=");
                return result.Success && !string.IsNullOrWhiteSpace(result.Output);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if process {PID} is running", pid);
                return false;
            }
        }
        
        /// <inheritdoc />
        public async Task<bool> KillProcessAsync(int pid, bool force = false)
        {
            try
            {
                // First check if the process exists
                if (!await IsProcessRunningAsync(pid))
                {
                    _logger.LogWarning("Process {PID} not found when attempting to kill it", pid);
                    return false;
                }
                
                // Use SIGKILL (-9) for force kill, otherwise use SIGTERM (-15, default)
                string signal = force ? "-9" : "-15";
                var result = await ExecuteCommandAsync($"kill {signal} {pid}", sudo: true);
                
                if (!result.Success)
                {
                    _logger.LogWarning("Failed to kill process {PID}: {Error}", pid, result.Output);
                    return false;
                }
                
                // Verify the process was killed
                await Task.Delay(100); // Small delay to allow process to terminate
                return !await IsProcessRunningAsync(pid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error killing process {PID}", pid);
                return false;
            }
        }
        
        /// <summary>
        /// Cleans command output by removing ANSI escape codes, control characters, and progress indicators.
        /// </summary>
        private string CleanCommandOutput(string output)
        {
            if (string.IsNullOrEmpty(output))
            {
                return string.Empty;
            }
            
            // Remove ANSI escape codes
            var ansiPattern = new Regex(@"\x1B\[[^@-~]*[@-~]");
            output = ansiPattern.Replace(output, string.Empty);
            
            // Remove carriage returns and backspace sequences that create "spinning" or progress indicators
            var progressPattern = new Regex(@"\r[^\n]");
            output = progressPattern.Replace(output, string.Empty);
            
            // Remove backspace characters and the character they would erase
            var backspacePattern = new Regex(".\b");
            while (backspacePattern.IsMatch(output))
            {
                output = backspacePattern.Replace(output, string.Empty);
            }
            
            // Fix multiple consecutive newlines
            var multipleNewlinesPattern = new Regex(@"\n{3,}");
            output = multipleNewlinesPattern.Replace(output, "\n\n");
            
            // Handle progress percentages (like "0/32" progress indicators)
            var progressPercentPattern = new Regex(@"\d+\/\d+\b\b\b\b\b     \b\b\b\b\b");
            output = progressPercentPattern.Replace(output, string.Empty);
            
            return output;
        }
    }

    /// <summary>
    /// Represents the result of executing a system command.
    /// </summary>
    public class CommandResult
    {
        /// <summary>
        /// Whether the command executed successfully (exit code 0).
        /// </summary>
        public bool Success { get; set; }
        
        /// <summary>
        /// The exit code returned by the command.
        /// </summary>
        public int ExitCode { get; set; }
        
        /// <summary>
        /// The combined standard output and standard error of the command.
        /// </summary>
        public string Output { get; set; } = string.Empty;
        
        /// <summary>
        /// The standard output of the command.
        /// </summary>
        public string StdOut { get; set; } = string.Empty;
        
        /// <summary>
        /// The standard error of the command.
        /// </summary>
        public string StdErr { get; set; } = string.Empty;
        
        /// <summary>
        /// The command that was executed.
        /// </summary>
        public string Command { get; set; } = string.Empty;
        
        /// <summary>
        /// Any exception that occurred during command execution.
        /// </summary>
        public Exception? Error { get; set; }
        
        /// <summary>
        /// Gets the output as a string array, split by newlines and with empty entries removed.
        /// </summary>
        public string[] OutputLines => Output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }
}