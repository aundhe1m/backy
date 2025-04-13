namespace Backy.Configuration;

public static class ConfigurationExtensions
{
    public static WebApplicationBuilder ConfigureEnvironmentVariableMapping(this WebApplicationBuilder builder)
    {
        // Map our custom environment variables to the standard .NET configuration format
        
        // Database
        MapEnvironmentVariable(builder.Configuration, "DB_CONNECTION_STRING", "ConnectionStrings:DefaultConnection");
        
        // Backy Agent
        MapEnvironmentVariable(builder.Configuration, "BACKY_AGENT_URL", "BackyAgent:Url");
        MapEnvironmentVariable(builder.Configuration, "BACKY_AGENT_TIMEOUT_SECONDS", "BackyAgent:TimeoutSeconds");
        MapEnvironmentVariable(builder.Configuration, "BACKY_AGENT_MAX_RETRIES", "BackyAgent:RetryPolicy:MaxRetries");
        MapEnvironmentVariable(builder.Configuration, "BACKY_AGENT_RETRY_DELAY_MS", "BackyAgent:RetryPolicy:DelayMilliseconds");
        
        // Logging
        MapEnvironmentVariable(builder.Configuration, "LOGGING_LEVEL_DEFAULT", "Logging:LogLevel:Default");
        MapEnvironmentVariable(builder.Configuration, "LOGGING_LEVEL_MICROSOFT", "Logging:LogLevel:Microsoft");
        
        return builder;
    }
    
    private static void MapEnvironmentVariable(IConfigurationRoot config, string envVar, string configKey)
    {
        var value = config[envVar];
        if (!string.IsNullOrEmpty(value))
        {
            // This forces the configuration system to use our environment variable value
            config[configKey] = value;
        }
    }
}
