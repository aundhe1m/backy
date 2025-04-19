// filepath: /home/aundhe1m/backy/Backy.Agent/Services/PoolOperationCleanupService.cs
using Backy.Agent.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Backy.Agent.Services;

/// <summary>
/// Background service to clean up old pool operation statuses
/// </summary>
public class PoolOperationCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PoolOperationCleanupService> _logger;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(1);
    private readonly TimeSpan _operationRetentionPeriod = TimeSpan.FromDays(1);
    
    public PoolOperationCleanupService(
        IServiceProvider serviceProvider,
        ILogger<PoolOperationCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Pool operation cleanup service started");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Delay first, then clean up
                await Task.Delay(_cleanupInterval, stoppingToken);
                
                _logger.LogDebug("Starting pool operation cleanup");
                
                // Get the IPoolOperationManager from the service provider
                using (var scope = _serviceProvider.CreateScope())
                {
                    var poolOperationManager = scope.ServiceProvider.GetRequiredService<IPoolOperationManager>();
                    await poolOperationManager.CleanupOldOperationsAsync(_operationRetentionPeriod);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation, no need to log error
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during pool operation cleanup");
                
                // Wait before trying again
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
        
        _logger.LogInformation("Pool operation cleanup service stopped");
    }
}