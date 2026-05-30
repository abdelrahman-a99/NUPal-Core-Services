using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Nupal.Domain.Entities
{
    public class RlRecommendation
    {
        [BsonId]
        public ObjectId Id { get; set; }

        public string StudentId { get; set; } // Reference to Account.Id
        public DateTime CreatedAt { get; set; }

        public int TermIndex { get; set; } // The "next term number", e.g. 1 for first semester

        public List<string> Courses { get; set; } = new();

        public List<TermRecommendation>? SlatesByTerm { get; set; }

        public RecommendationMetrics Metrics { get; set; }

        public RecommendationArtifacts? Artifacts { get; set; }

        public string? ModelVersion { get; set; }
        public string? PolicyVersion { get; set; }

        public string? DefaultProfile { get; set; }
        public Dictionary<string, ProfileRecommendation>? Profiles { get; set; }
    }

    public class ProfileRecommendation
    {
        public List<string> Courses { get; set; } = new();
        public List<TermRecommendation>? SlatesByTerm { get; set; }
        public RecommendationMetrics Metrics { get; set; }
    }

    public class TermRecommendation
    {
        public int Term { get; set; }
        public List<string> Slate { get; set; } = new();
    }

    public class RecommendationMetrics
    {
        public double CumGpa { get; set; }
        public double TotalCredits { get; set; }
        public bool Graduated { get; set; }
        public Dictionary<string, object>? GradFlags { get; set; }
    }

    public class RecommendationArtifacts
    {
        public string? JsonUrl { get; set; }
    }
}
