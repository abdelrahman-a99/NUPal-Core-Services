using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace NUPAL.Core.Api.BackgroundServices;

public class AIServiceKeepAliveWorker : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AIServiceKeepAliveWorker> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _pingTimeout;

    public AIServiceKeepAliveWorker(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<AIServiceKeepAliveWorker> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;

        _interval = TimeSpan.FromMinutes(
            _configuration.GetValue<double?>("ExternalServiceTimeouts:KeepAliveIntervalMinutes") ?? 10
        );

        _pingTimeout = TimeSpan.FromSeconds(
            _configuration.GetValue<double?>("ExternalServiceTimeouts:KeepAliveSeconds") ?? 300
        );
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TryLog(() => _logger.LogInformation("AI Service Keep-Alive Worker is starting."));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var rlServiceUrl = _configuration["RlServiceUrl"];
                var agentServiceUrl = _configuration["AgentServiceUrl"];
                var ragServiceUrl = _configuration["RagServiceUrl"]
                                    ?? _configuration["RAG_BASE_URL"]
                                    ?? _configuration["RagService:Url"];
                var careerServiceUrl = _configuration["CareerServices:Url"];

                if (!string.IsNullOrEmpty(rlServiceUrl))
                    await PingService(rlServiceUrl, "RL Service", stoppingToken);

                if (!string.IsNullOrEmpty(agentServiceUrl))
                    await PingService(agentServiceUrl, "Agent Service", stoppingToken);

                if (!string.IsNullOrEmpty(ragServiceUrl))
                    await PingService(ragServiceUrl, "RAG Service", stoppingToken);

                if (!string.IsNullOrEmpty(careerServiceUrl))
                    await PingService(careerServiceUrl, "Career Services", stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown — exit gracefully
                break;
            }
            catch (Exception ex)
            {
                TryLog(() => _logger.LogError(ex, "Error occurred while pinging AI services."));
            }

            if (stoppingToken.IsCancellationRequested) break;

            TryLog(() => _logger.LogInformation("Waiting for {Interval} before next ping.", _interval));

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown during delay — exit gracefully
                break;
            }
        }

        TryLog(() => _logger.LogInformation("AI Service Keep-Alive Worker is stopping."));
    }

    private async Task PingService(string url, string serviceName, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = _pingTimeout;

            TryLog(() => _logger.LogInformation("Pinging {ServiceName} at {Url}...", serviceName, url));
            var response = await client.GetAsync(url, ct);
            TryLog(() => _logger.LogInformation("{ServiceName} responded with {StatusCode}", serviceName, response.StatusCode));
        }
        catch (OperationCanceledException)
        {
            // Shutdown during ping — ignore
        }
        catch (Exception ex)
        {
            TryLog(() => _logger.LogWarning(ex, "Connectivity issue with {ServiceName} at {Url}. Background worker will continue.", serviceName, url));
        }
    }

    /// <summary>
    /// Safely attempts to log, ignoring failures if the logger is disposed during shutdown.
    /// </summary>
    private static void TryLog(Action logAction)
    {
        try { logAction(); }
        catch { /* Logger may be disposed during shutdown — ignore */ }
    }
}
