using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

namespace Backy.Configuration;

public class ConfigurationPrinter
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfigurationPrinter> _logger;
    private readonly Regex _sensitiveDataPattern = new Regex("password|secret|key|pwd|apikey", RegexOptions.IgnoreCase);
    private readonly HashSet<string> _printedKeys = new HashSet<string>();
    
    // Class to batch log entries to avoid extra newlines
    private class BatchLogger
    {
        private readonly ILogger _logger;
        private readonly List<string> _lines = new List<string>();
        
        public BatchLogger(ILogger logger)
        {
            _logger = logger;
        }
        
        public void AddLine(string line)
        {
            _lines.Add(line);
        }
        
        public void AddHeaderLine(string line)
        {
            if (_lines.Count > 0)
            {
                Flush(); // Flush any pending lines before adding a header
            }
            _lines.Add(line);
        }
        
        public void Flush()
        {
            if (_lines.Count == 0) return;
            
            _logger.LogInformation(string.Join("\n", _lines));
            _lines.Clear();
        }
    }

    public ConfigurationPrinter(IConfiguration configuration, ILogger<ConfigurationPrinter> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public void PrintAllConfiguration()
    {
        var batchLogger = new BatchLogger(_logger);
        
        // First display the web application port information
        PrintWebAppPortInfo(batchLogger);
        
        // Define the configuration sections to print in order
        var sectionsToPrint = new List<(string Title, string Prefix)>
        {
            ("DATABASE CONNECTION", "DB_CONNECTION"),
            ("BACKY AGENT", "BACKY_AGENT"),
            ("LOGGING", "LOGGING")
        };

        // Print each section
        foreach (var section in sectionsToPrint)
        {
            var entries = GetConfigurationSection(section.Prefix);
            
            if (entries.Count > 0)
            {
                batchLogger.AddHeaderLine($"{section.Title} CONFIG:");
                
                foreach (var item in entries)
                {
                    if (_printedKeys.Contains(item.Key))
                        continue;
                    
                    string value = MaskSensitiveData(item.Key, item.Value);
                    string keyDisplay = item.Key.Replace(section.Prefix + "_", "");
                    batchLogger.AddLine($"- {keyDisplay}: {value}");
                    _printedKeys.Add(item.Key);
                }
            }
        }
        
        batchLogger.AddLine("");
        batchLogger.Flush(); // Make sure all remaining lines are logged
    }

    private void PrintWebAppPortInfo(BatchLogger batchLogger)
    {
        batchLogger.AddHeaderLine("WEB CONFIG:");
        
        // Try to get port information from different possible sources
        string? port = null;
        string portSource = "default from docker-compose";
        
        // Check for Docker port mapping first
        port = _configuration["PORT"];
        if (!string.IsNullOrEmpty(port))
        {
            portSource = "Docker environment";
        }
        // Check Kestrel configuration
        else if (!string.IsNullOrEmpty(_configuration["Kestrel:Endpoints:Http:Url"]))
        {
            var kestrelUrl = _configuration["Kestrel:Endpoints:Http:Url"];
            if (!string.IsNullOrEmpty(kestrelUrl))
            {
                port = ExtractPortFromUrl(kestrelUrl);
            }
            portSource = "Kestrel configuration";
        }
        // Check ASP.NET Core URLs
        else if (!string.IsNullOrEmpty(_configuration["ASPNETCORE_URLS"]))
        {
            var aspNetCoreUrls = _configuration["ASPNETCORE_URLS"];
            if (!string.IsNullOrEmpty(aspNetCoreUrls))
            {
                port = ExtractPortFromUrl(aspNetCoreUrls);
            }
            portSource = "ASP.NET Core URLs";
        }
        // Use default port from docker-compose
        else
        {
            port = "5015";
        }

        batchLogger.AddLine($"- PORT: {port} ({portSource})");
        
        // Also display the environment
        var env = _configuration["ASPNETCORE_ENVIRONMENT"] ?? "Production";
        batchLogger.AddLine($"- ENVIRONMENT: {env}");
    }
    
    private string ExtractPortFromUrl(string url)
    {
        // Extract port from URLs like "http://*:5000" or "http://+:80"
        var match = Regex.Match(url, @":(\d+)");
        return match.Success ? match.Groups[1].Value : url;
    }

    private List<KeyValuePair<string, string?>> GetConfigurationSection(string prefix)
    {
        return _configuration.AsEnumerable()
            .Where(c => c.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Key)
            .ToList();
    }

    private string MaskSensitiveData(string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) return "[not set]";
        
        // Check for connection strings which need to be masked
        if (key.Contains("CONNECTION", StringComparison.OrdinalIgnoreCase) || 
            key.Contains("CONN_STR", StringComparison.OrdinalIgnoreCase))
        {
            // Format appropriately for connection strings
            return MaskConnectionString(value);
        }
        
        // Mask sensitive data like passwords, keys, and secrets
        if (_sensitiveDataPattern.IsMatch(key))
        {
            return "********";
        }
        
        return value;
    }
    
    private string MaskConnectionString(string connectionString)
    {
        // Handle common connection string formats
        var masked = connectionString;
        
        // PostgreSQL connection string pattern: "Host=...;Database=...;Username=...;Password=...;"
        masked = Regex.Replace(
            masked, 
            @"Password=([^;]*)", 
            "Password=********", 
            RegexOptions.IgnoreCase
        );
        
        // SQL Server connection string pattern: "Server=...;Database=...;User ID=...;Password=...;"
        masked = Regex.Replace(
            masked, 
            @"Password=([^;]*)", 
            "Password=********", 
            RegexOptions.IgnoreCase
        );
        
        // General approach for any "secret=value" pattern in the connection string
        masked = Regex.Replace(
            masked,
            @"(pwd|password|secret|apikey|key)=([^;]*)",
            "$1=********",
            RegexOptions.IgnoreCase
        );
        
        return masked;
    }
}
