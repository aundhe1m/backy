using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Backy;

public class BackyAgentHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<BackyAgentHealthCheck> _logger;
    
    public BackyAgentHealthCheck(IConfiguration configuration, ILogger<BackyAgentHealthCheck> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }
    
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var agentUrl = _configuration["BackyAgent:Url"];
        
        if (string.IsNullOrEmpty(agentUrl))
        {
            return HealthCheckResult.Degraded("BackyAgent URL not configured");
        }

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            
            // Just ping the URL to see if it's reachable
            // In a real implementation, you'd call a health endpoint on the agent
            var response = await httpClient.GetAsync($"{agentUrl}/health", cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Healthy("BackyAgent is reachable");
            }
            
            return HealthCheckResult.Degraded($"BackyAgent returned status code {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health check failed when connecting to BackyAgent");
            return HealthCheckResult.Unhealthy("Unable to connect to BackyAgent", ex);
        }
    }
}
