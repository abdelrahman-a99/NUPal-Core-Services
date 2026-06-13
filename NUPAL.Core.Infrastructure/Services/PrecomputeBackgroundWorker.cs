using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUPAL.Core.Application.Interfaces;

namespace Nupal.Core.Infrastructure.Services
{
    public class PrecomputeBackgroundWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PrecomputeBackgroundWorker> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromDays(5);
        private readonly TimeSpan _startupDelay = TimeSpan.FromMinutes(30);

        public PrecomputeBackgroundWorker(IServiceProvider serviceProvider, ILogger<PrecomputeBackgroundWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Precompute Background Worker is starting. First sync in {Delay}.", _startupDelay);

            try
            {
                await Task.Delay(_startupDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Precompute Background Worker triggering sync at: {time}", DateTimeOffset.Now);

                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var precomputeService = scope.ServiceProvider.GetRequiredService<IPrecomputeService>();
                        var result = await precomputeService.SyncAllStudentsAsync(isSimulation: false);
                        
                        if (result.TriggeredJobs > 0)
                        {
                            _logger.LogInformation("Sync completed. Triggered {count} jobs for: {ids}", 
                                result.TriggeredJobs, string.Join(", ", result.TriggeredStudentIds));
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Error occurred executing Precompute Background Worker.");
                    }
                }

                await Task.Delay(_interval, stoppingToken);
            }

            _logger.LogInformation("Precompute Background Worker is stopping.");
        }
    }
}
