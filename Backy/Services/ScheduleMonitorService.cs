using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backy.Services
{
    public class ScheduleMonitorService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<ScheduleMonitorService> _logger;

        public ScheduleMonitorService(IServiceScopeFactory serviceScopeFactory, ILogger<ScheduleMonitorService> logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ScheduleMonitorService is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var remoteConnectionService = scope.ServiceProvider.GetRequiredService<IRemoteConnectionService>();
                    await remoteConnectionService.CheckSchedules(stoppingToken);
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }

            _logger.LogInformation("ScheduleMonitorService is stopping.");
        }
    }
}
