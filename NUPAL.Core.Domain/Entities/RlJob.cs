using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Nupal.Domain.Entities
{
    public class RlJob
    {
        [BsonId]
        public ObjectId Id { get; set; }

        public string StudentId { get; set; } // Reference to Account.Id
        
        [BsonRepresentation(BsonType.String)]
        public JobStatus Status { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }

        public string EducationHash { get; set; } // To detect changes
        public bool IsSimulation { get; set; } // Track if job was a simulation run
        public int? Episodes { get; set; }
        public string? TargetTrack { get; set; }
        public string? Error { get; set; }
        
        public string? ResultRecommendationId { get; set; } // Reference to RlRecommendation ID
    }

    public enum JobStatus
    {
        Queued,
        Running,
        Ready,
        Failed
    }
}
