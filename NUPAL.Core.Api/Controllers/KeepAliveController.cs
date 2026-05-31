using Microsoft.AspNetCore.Mvc;

namespace NUPAL.Core.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class KeepAliveController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<KeepAliveController> _logger;

    public KeepAliveController(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<KeepAliveController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("ping-ai")]
    public async Task<IActionResult> PingAIServices()
    {
        var rlServiceUrl = _configuration["RlServiceUrl"];
        var agentServiceUrl = _configuration["AgentServiceUrl"];
        var ragServiceUrl = _configuration["RagServiceUrl"]
                            ?? _configuration["RAG_BASE_URL"]
                            ?? _configuration["RagService:Url"];
        var careerServiceUrl = _configuration["CareerServices:Url"];

        var results = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(rlServiceUrl))
        {
            results.Add("RLService", await SafePing(rlServiceUrl));
        }

        if (!string.IsNullOrEmpty(agentServiceUrl))
        {
            results.Add("AgentService", await SafePing(agentServiceUrl));
        }

        if (!string.IsNullOrEmpty(ragServiceUrl))
        {
            results.Add("RAGService", await SafePing(ragServiceUrl));
        }

        if (!string.IsNullOrEmpty(careerServiceUrl))
        {
            results.Add("CareerServices", await SafePing(careerServiceUrl));
        }

        return Ok(new
        {
            Message = "Ping attempts completed",
            Timestamp = DateTime.UtcNow,
            Results = results
        });
    }

    private async Task<string> SafePing(string url)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();

            client.Timeout = TimeSpan.FromSeconds(
                _configuration.GetValue<double?>("ExternalServiceTimeouts:KeepAliveSeconds") ?? 300
            );

            // We just want to trigger a wake up, so a simple GET is enough.
            // Even if it returns 404 or 401, the server is "hit" and wakes up.
            var response = await client.GetAsync(url);
            return $"Status: {response.StatusCode}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ping AI service at {Url}", url);
            return $"Error: {ex.Message}";
        }
    }
}
