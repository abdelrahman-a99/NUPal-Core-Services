using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUPAL.Core.Application.Interfaces;

namespace Nupal.Core.Infrastructure.Services
{
    /// <summary>
    /// Processes RL precompute jobs one at a time from the MongoDB queue.
    /// </summary>
    public class RlPrecomputeQueueWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<RlPrecomputeQueueWorker> _logger;
        private static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(5);

        public RlPrecomputeQueueWorker(IServiceProvider serviceProvider, ILogger<RlPrecomputeQueueWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("RL precompute queue worker started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var jobRepo = scope.ServiceProvider.GetRequiredService<IRlJobRepository>();
                    var precomputeService = scope.ServiceProvider.GetRequiredService<IPrecomputeService>();

                    var job = await jobRepo.TryClaimNextQueuedJobAsync();
                    if (job == null)
                    {
                        await Task.Delay(IdlePollInterval, stoppingToken);
                        continue;
                    }

                    _logger.LogInformation(
                        "Processing RL job {JobId} for student {StudentId}.",
                        job.Id,
                        job.StudentId);

                    await precomputeService.ProcessQueuedJobAsync(job.Id.ToString(), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "RL precompute queue worker encountered an error.");
                    await Task.Delay(IdlePollInterval, stoppingToken);
                }
            }

            _logger.LogInformation("RL precompute queue worker stopped.");
        }
    }
}
