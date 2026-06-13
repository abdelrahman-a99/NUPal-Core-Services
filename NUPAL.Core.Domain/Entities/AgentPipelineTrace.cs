using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Nupal.Domain.Entities
{
    [BsonIgnoreExtraElements]
    public class AgentPipelineTrace
    {
        [BsonId]
        public ObjectId Id { get; set; }

        public string TraceId { get; set; } = default!;

        public string? AgentTraceId { get; set; }

        public string StudentId { get; set; } = default!;

        [BsonRepresentation(BsonType.ObjectId)]
        public string ConversationId { get; set; } = default!;

        public string UserMessage { get; set; } = default!;

        public string? UserMessageId { get; set; }

        public List<string> AssistantMessageIds { get; set; } = new();

        public string Status { get; set; } = "completed";

        public string? AgentRoute { get; set; }

        public string? AgentIntent { get; set; }

        public string? AgentUserKind { get; set; }

        public string? AgentStatus { get; set; }

        public double? RouteConfidence { get; set; }

        public string? RouteReason { get; set; }

        public double? TotalDurationMs { get; set; }

        public string? AgentRequestJson { get; set; }

        public string? AgentResponseJson { get; set; }

        public string? Error { get; set; }

        public List<AgentPipelineTraceEvent> Events { get; set; } = new();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAt { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class AgentPipelineTraceEvent
    {
        public int Order { get; set; }

        public string Stage { get; set; } = default!;

        public string Status { get; set; } = "ok";

        public DateTime At { get; set; } = DateTime.UtcNow;

        public double? DurationMs { get; set; }

        public string? DataJson { get; set; }

        public string? Error { get; set; }
    }
}
