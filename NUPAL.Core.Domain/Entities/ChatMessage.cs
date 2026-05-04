using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Nupal.Domain.Entities
{
    [BsonIgnoreExtraElements]
    public class ChatMessage
    {
        [BsonId]
        public ObjectId Id { get; set; }

        // ObjectId string of ChatConversation
        [BsonRepresentation(BsonType.ObjectId)]
        public string ConversationId { get; set; } = default!;

        // Reference to Student.Account.Id
        public string StudentId { get; set; } = default!;

        // "user" | "assistant" | "system"
        public string Role { get; set; } = default!;

        // "rag" | "rl" | "unknown" (used for grouping)
        public string Kind { get; set; } = "unknown";

        public string Content { get; set; } = default!;

        // Optional JSON metadata (e.g., RAG passages, RL slate, routing diagnostics).
        // For assistant messages, this stores both result-level and route-level metadata.
        // For user messages, this stores the route decision used for that turn.
        public string? MetadataJson { get; set; }

        // Query-friendly agent routing metadata copied from agent_deploy /route.
        // Existing MongoDB documents remain valid because this entity ignores extra/missing fields.
        public string? AgentTraceId { get; set; }
        public string? AgentIntent { get; set; }
        public string? AgentRoute { get; set; }
        public string? AgentUserKind { get; set; }
        public string? AgentStatus { get; set; }
        public double? RouteConfidence { get; set; }
        public string? RouteReason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
