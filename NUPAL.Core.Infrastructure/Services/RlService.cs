using System.Net.Http.Json;
using System.Text.Json;
using NUPAL.Core.Application.DTOs;
using NUPAL.Core.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Nupal.Core.Infrastructure.Services
{
    public class RlService : IRlService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public RlService(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            // Allow override via config, but throw if missing (Fail Fast)
            _baseUrl = config["RlServiceUrl"] 
                       ?? throw new InvalidOperationException("RL Recommender Service URL is not configured. Please provide 'RlServiceUrl' in appsettings.");
            
            var timeoutSeconds = config.GetValue<double?>("ExternalServiceTimeouts:RlSeconds") ?? 10800;
            _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds); // Default: 3 hours
        }

        public async Task<RlTrainingResponse> GetRecommendationAsync(RlTrainingRequest request)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            };

            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl.TrimEnd('/')}/api/recommend", request, options);
            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"RL Service failed: {response.StatusCode} - {content}");
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[DEBUG] RL Service Response: {jsonContent}");

            var parsed = JsonSerializer.Deserialize<RlTrainingResponse>(jsonContent, options) 
                         ?? throw new InvalidOperationException("Failed to deserialize RL response");

            parsed.RawJson = jsonContent;
            return parsed;
        }
    }
}
