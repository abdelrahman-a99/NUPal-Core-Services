using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Nupal.Domain.Entities
{
    public class Registration
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }
        
        [BsonElement("student_id")]
        public string StudentId { get; set; } = "";
        
        [BsonElement("student_name")]
        public string StudentName { get; set; } = "";
        
        [BsonElement("student_email")]
        public string StudentEmail { get; set; } = "";
        
        [BsonElement("selected_block")]
        public SchedulingBlock SelectedBlock { get; set; } = new();
        
        [BsonElement("status")]
        public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected
        
        [BsonElement("registered_at")]
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
        
        // Metadata to know if they followed recommendations
        [BsonElement("is_from_recommendation")]
        public bool IsFromRecommendation { get; set; }
        
        [BsonElement("is_from_rl")]
        public bool IsFromRl { get; set; }
        
        [BsonElement("is_modified")]
        public bool IsModified { get; set; }
        
        [BsonElement("admin_note")]
        public string? AdminNote { get; set; }
        
        [BsonElement("processed_at")]
        public DateTime? ProcessedAt { get; set; }
    }
}
