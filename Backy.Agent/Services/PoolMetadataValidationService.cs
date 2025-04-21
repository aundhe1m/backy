using Backy.Agent.Models;

namespace Backy.Agent.Services;

/// <summary>
/// Background service that validates pool metadata at application startup
/// </summary>
public class PoolMetadataValidationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PoolMetadataValidationService> _logger;
    
    public PoolMetadataValidationService(
        IServiceProvider serviceProvider,
        ILogger<PoolMetadataValidationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Allow a small delay for services to initialize fully
            await Task.Delay(2000, stoppingToken);
            
            _logger.LogDebug("Starting pool metadata validation...");
            
            // Create a scope to resolve scoped services
            using var scope = _serviceProvider.CreateScope();
            var poolService = scope.ServiceProvider.GetRequiredService<IPoolService>();
            
            var result = await poolService.ValidateAndUpdatePoolMetadataAsync();
            
            if (result.Success)
            {
                if (result.FixedEntries > 0)
                {
                    _logger.LogDebug("Pool metadata validation complete. Fixed {FixedEntries} entries.", result.FixedEntries);
                }
                else
                {
                    _logger.LogDebug("Pool metadata validation complete. No issues found.");
                }
            }
            else
            {
                _logger.LogWarning("Pool metadata validation issue: {Message}", result.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during pool metadata validation at startup");
        }
    }
}