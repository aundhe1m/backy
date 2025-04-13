using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

namespace Backy.Configuration;

public class ConfigurationPrinter
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfigurationPrinter> _logger;
    private readonly Regex _passwordPattern = new Regex("password|secret|key", RegexOptions.IgnoreCase);

    public ConfigurationPrinter(IConfiguration configuration, ILogger<ConfigurationPrinter> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public void PrintAllConfiguration()
    {
        var configSections = new Dictionary<string, List<KeyValuePair<string, string?>>>
        {
            { "DATABASE", GetConfigurationSection("DB_") },
            { "BACKY AGENT", GetConfigurationSection("BACKY_AGENT_") },
            { "LOGGING", GetConfigurationSection("LOGGING_") }
        };

        _logger.LogInformation("------ BACKY CONFIGURATION ------");

        foreach (var section in configSections)
        {
            if (section.Value.Count > 0)
            {
                _logger.LogInformation($"--- {section.Key} ---");
                
                foreach (var item in section.Value)
                {
                    string value = MaskSensitiveData(item.Key, item.Value);
                    _logger.LogInformation($"{item.Key}: {value}");
                }
            }
        }
        
        _logger.LogInformation("--------------------------------");
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
        
        // Mask sensitive data like passwords and secrets
        if (_passwordPattern.IsMatch(key))
        {
            return "********";
        }
        
        return value;
    }
}
