using System.Text.Json.Serialization;

namespace NUPAL.Core.Application.DTOs
{
    public class AgentPipelineTraceEventDto
    {
        [JsonPropertyName("order")]
        public int Order { get; set; }

        [JsonPropertyName("stage")]
        public string Stage { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = "ok";

        [JsonPropertyName("at")]
        public DateTime At { get; set; }

        [JsonPropertyName("duration_ms")]
        public double? DurationMs { get; set; }

        [JsonPropertyName("data_json")]
        public string? DataJson { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    public class AgentPipelineTraceDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("trace_id")]
        public string TraceId { get; set; } = string.Empty;

        [JsonPropertyName("agent_trace_id")]
        public string? AgentTraceId { get; set; }

        [JsonPropertyName("student_id")]
        public string StudentId { get; set; } = string.Empty;

        [JsonPropertyName("conversation_id")]
        public string ConversationId { get; set; } = string.Empty;

        [JsonPropertyName("user_message")]
        public string UserMessage { get; set; } = string.Empty;

        [JsonPropertyName("user_message_id")]
        public string? UserMessageId { get; set; }

        [JsonPropertyName("assistant_message_ids")]
        public List<string> AssistantMessageIds { get; set; } = new();

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("agent_route")]
        public string? AgentRoute { get; set; }

        [JsonPropertyName("agent_intent")]
        public string? AgentIntent { get; set; }

        [JsonPropertyName("agent_user_kind")]
        public string? AgentUserKind { get; set; }

        [JsonPropertyName("agent_status")]
        public string? AgentStatus { get; set; }

        [JsonPropertyName("route_confidence")]
        public double? RouteConfidence { get; set; }

        [JsonPropertyName("route_reason")]
        public string? RouteReason { get; set; }

        [JsonPropertyName("total_duration_ms")]
        public double? TotalDurationMs { get; set; }

        [JsonPropertyName("agent_request_json")]
        public string? AgentRequestJson { get; set; }

        [JsonPropertyName("agent_response_json")]
        public string? AgentResponseJson { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("events")]
        public List<AgentPipelineTraceEventDto> Events { get; set; } = new();

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("completed_at")]
        public DateTime? CompletedAt { get; set; }
    }
}
