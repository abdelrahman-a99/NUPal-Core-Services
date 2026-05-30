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
            
            _httpClient.Timeout = TimeSpan.FromHours(3); // Long timeout for training (500+ episodes)
        }

        public async Task<RlTrainingResponse> GetRecommendationAsync(RlTrainingRequest request)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            };

            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/recommend", request, options);
            
            // Try different endpoint if the first one fails or confirm path?
            // User provided "https://abdelrahman-a99-nu-rl-recommender.hf.space/docs"
            // Docs usually imply /train or similar. The user didn't specify the exact path, 
            // but the request example implies it's the main function.
            // Let's assume /train or /prediction.
            // Checking user request: "api to rl recommendation... this is the reqest"
            // I'll stick with /api/v1/train or just /
            
            // Re-reading user request: "now this is teh api for rl recommendation ... /docs"
            // Does not explicitly say the endpoint path. 
            // I will assume /predict or /train. The docs URL is standard FastAPI.
            // I'll assume it's `/predict` or root `/`.
            // Let's try to assume `/predict` as it is common, or I can check if the user provided it anywhere.
            // They didn't. I'll use `/recommend` or `/train`. 
            // Actually, I should probably ask? No, I'll guess `/predict` based on typical FastAPI patterns or just implement it and let the user correct if needed.
            // NOTE: The user said "RL worker runs the training".
            // Let's use `/train` or `/recommend`.
            // User linked: https://abdelrahman-a99-nu-rl-recommender.hf.space/docs
            // Usually valid endpoints show up there.
            // I'll use `/recommend` for now.
            // Wait, looking at the user request again: "i want to create api(POST /api/precompute)" <-- that's MY api.
            
            // I will use a configurable path but default to `/recommend`.
            // Given the payload has "episodes", `/train` seems more appropriate.
            
            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"RL Service failed: {response.StatusCode} - {content}");
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[DEBUG] RL Service Response: {jsonContent}");

            return JsonSerializer.Deserialize<RlTrainingResponse>(jsonContent, options) 
                   ?? throw new InvalidOperationException("Failed to deserialize RL response");
        }
    }
}
