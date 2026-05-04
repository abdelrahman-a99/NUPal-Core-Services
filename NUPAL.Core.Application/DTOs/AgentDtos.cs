using System.Text.Json;
using System.Text.Json.Serialization;
using Nupal.Domain.Entities;

namespace NUPAL.Core.Application.DTOs
{
    public class AgentHistoryMessageDto
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "user"; // user/assistant

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "unknown"; // rag/rl/unknown

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    public class AgentRouteRequestDto
    {
        [JsonPropertyName("student_id")]
        public string StudentId { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("history")]
        public List<AgentHistoryMessageDto> History { get; set; } = new();

        [JsonPropertyName("rl_recommendation")]
        public AgentRlRecommendationDto? RlRecommendation { get; set; }
    }

    public class AgentRlRecommendationDto
    {
        [JsonPropertyName("term_index")]
        public int? TermIndex { get; set; }

        [JsonPropertyName("courses")]
        public List<string> Courses { get; set; } = new();

        [JsonPropertyName("slates_by_term")]
        public List<TermRecommendation>? SlatesByTerm { get; set; }

        [JsonPropertyName("metrics")]
        public RecommendationMetrics? Metrics { get; set; }

        [JsonPropertyName("model_version")]
        public string? ModelVersion { get; set; }

        [JsonPropertyName("policy_version")]
        public string? PolicyVersion { get; set; }
    }

    public class AgentRouteResponseDto
    {
        // Legacy field kept for backward compatibility with older agent/backend versions.
        // New code should prefer Route/UserKind/Status below.
        [JsonPropertyName("intent")]
        public string Intent { get; set; } = "faq"; // legacy: faq/recommendation/mixed

        [JsonPropertyName("route")]
        public string Route { get; set; } = string.Empty; // rag_only/rl_only/mixed_rag_rl/general_chat/unsupported

        [JsonPropertyName("user_kind")]
        public string UserKind { get; set; } = string.Empty; // rag/rl/mixed/general/unsupported

        [JsonPropertyName("status")]
        public string Status { get; set; } = "ok"; // ok/partial/degraded/clarification_needed/unsupported

        [JsonPropertyName("trace_id")]
        public string? TraceId { get; set; }

        [JsonPropertyName("router")]
        public JsonElement? Router { get; set; }

        [JsonPropertyName("results")]
        public List<AgentResultDto> Results { get; set; } = new();
    }

    public class AgentResultDto
    {
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "rag"; // rag/rl

        [JsonPropertyName("answer")]
        public string Answer { get; set; } = string.Empty;

        // arbitrary JSON object as string
        [JsonPropertyName("metadata")]
        public object? Metadata { get; set; }
    }
}
