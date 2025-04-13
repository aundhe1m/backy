namespace Backy.Configuration;

public static class ConfigurationExtensions
{
    public static WebApplicationBuilder ConfigureEnvironmentVariableMapping(this WebApplicationBuilder builder)
    {
        // Make a temporary dictionary to store environment variable values
        var envVarValues = new Dictionary<string, string?>();
        
        // Extract environment variable values first
        envVarValues["BACKY_AGENT_URL"] = builder.Configuration["BACKY_AGENT_URL"];
        envVarValues["BACKY_AGENT_TIMEOUT_SECONDS"] = builder.Configuration["BACKY_AGENT_TIMEOUT_SECONDS"];
        envVarValues["BACKY_AGENT_MAX_RETRIES"] = builder.Configuration["BACKY_AGENT_MAX_RETRIES"];
        envVarValues["BACKY_AGENT_RETRY_DELAY_MS"] = builder.Configuration["BACKY_AGENT_RETRY_DELAY_MS"];
        envVarValues["DB_CONNECTION_STRING"] = builder.Configuration["DB_CONNECTION_STRING"];
        envVarValues["PORT"] = builder.Configuration["PORT"];
        envVarValues["LOGGING_LEVEL_DEFAULT"] = builder.Configuration["LOGGING_LEVEL_DEFAULT"];
        envVarValues["LOGGING_LEVEL_MICROSOFT"] = builder.Configuration["LOGGING_LEVEL_MICROSOFT"];
        
        // Force clear any existing values to ensure environment variables take precedence
        // This will reset any values from appsettings.json that might conflict with environment variables
        if (!string.IsNullOrEmpty(envVarValues["BACKY_AGENT_URL"]))
        {
            builder.Configuration["BackyAgent:Url"] = null;
            builder.Configuration["BACKY_AGENT_URL"] = null;
        }
        
        // Map our custom environment variables to the standard .NET configuration format
        // Database
        MapEnvironmentVariable(builder.Configuration, "DB_CONNECTION_STRING", "ConnectionStrings:DefaultConnection", envVarValues["DB_CONNECTION_STRING"]);
        
        // Backy Agent
        MapEnvironmentVariable(builder.Configuration, "BACKY_AGENT_URL", "BackyAgent:Url", envVarValues["BACKY_AGENT_URL"]);
        MapEnvironmentVariable(builder.Configuration, "BACKY_AGENT_TIMEOUT_SECONDS", "BackyAgent:TimeoutSeconds", envVarValues["BACKY_AGENT_TIMEOUT_SECONDS"]);
        MapEnvironmentVariable(builder.Configuration, "BACKY_AGENT_MAX_RETRIES", "BackyAgent:MaxRetryAttempts", envVarValues["BACKY_AGENT_MAX_RETRIES"]);
        MapEnvironmentVariable(builder.Configuration, "BACKY_AGENT_RETRY_DELAY_MS", "BackyAgent:RetryPolicy:DelayMilliseconds", envVarValues["BACKY_AGENT_RETRY_DELAY_MS"]);
        
        // Application Hosting
        MapEnvironmentVariable(builder.Configuration, "PORT", "Kestrel:Endpoints:Http:Url", envVarValues["PORT"]);
        
        // Logging
        MapEnvironmentVariable(builder.Configuration, "LOGGING_LEVEL_DEFAULT", "Logging:LogLevel:Default", envVarValues["LOGGING_LEVEL_DEFAULT"]);
        MapEnvironmentVariable(builder.Configuration, "LOGGING_LEVEL_MICROSOFT", "Logging:LogLevel:Microsoft", envVarValues["LOGGING_LEVEL_MICROSOFT"]);
        
        return builder;
    }
    
    private static void MapEnvironmentVariable(IConfigurationRoot config, string envVar, string configKey, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            // Force the configuration system to use our environment variable value
            // by explicitly setting it in the configuration
            config[configKey] = value;
            
            // Also preserve the original environment variable name for backward compatibility
            if (configKey != envVar)
            {
                config[envVar] = value;
            }
        }
    }
}
