using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Backy.Agent.Services;

public interface ISystemCommandService
{
    Task<CommandResult> ExecuteCommandAsync(string command, bool sudo = false);
}

public class SystemCommandService : ISystemCommandService
{
    private readonly ILogger<SystemCommandService> _logger;

    public SystemCommandService(ILogger<SystemCommandService> logger)
    {
        _logger = logger;
    }

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

            _logger.LogDebug("Command exit code: {ExitCode}, Output: {Output}", exitCode, output);
            
            // Clean the output before returning
            string cleanedOutput = CleanCommandOutput(output);

            return new CommandResult
            {
                Success = exitCode == 0,
                ExitCode = exitCode,
                Output = cleanedOutput.Trim(),
                Command = actualCommand
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

public class CommandResult
{
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
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