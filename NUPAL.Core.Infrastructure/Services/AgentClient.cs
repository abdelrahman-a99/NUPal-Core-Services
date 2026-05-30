using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NUPAL.Core.Application.DTOs;
using NUPAL.Core.Application.Interfaces;

namespace Nupal.Core.Infrastructure.Services
{
    public class AgentClient : IAgentClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public AgentClient(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _baseUrl = config["AgentServiceUrl"] 
                       ?? config["Agent:BaseUrl"] 
                       ?? throw new InvalidOperationException("Agent service URL is not configured. Please provide 'AgentServiceUrl' in appsettings.");
            _httpClient.Timeout = TimeSpan.FromSeconds(300);
        }

        public async Task<AgentRouteResponseDto> RouteAsync(AgentRouteRequestDto request, CancellationToken ct = default)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = null
            };

            try
            {
                var resp = await _httpClient.PostAsJsonAsync($"{_baseUrl.TrimEnd('/')}/route", request, options, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    Console.WriteLine($"[AgentClient] Agent service failed: {(int)resp.StatusCode} - {body}. Using fallback.");
                    return BuildFallbackResponse();
                }

                var json = await resp.Content.ReadAsStringAsync(ct);
                var parsed = JsonSerializer.Deserialize<AgentRouteResponseDto>(json, options);
                return parsed ?? BuildFallbackResponse();
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is OperationCanceledException)
            {
                Console.WriteLine($"[AgentClient] Agent service unreachable: {ex.Message}. Using fallback.");
                return BuildFallbackResponse();
            }
        }

        private static AgentRouteResponseDto BuildFallbackResponse() => new()
        {
            Intent = "faq",
            Results = new List<AgentResultDto>
            {
                new AgentResultDto
                {
                    Kind = "rag",
                    Answer = "عذراً، خدمة المساعد الذكي غير متاحة حالياً. يرجى المحاولة مرة أخرى لاحقاً.",
                }
            }
        };
    }
}
