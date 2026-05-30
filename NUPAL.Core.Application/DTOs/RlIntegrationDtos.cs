using System.Text.Json;
using System.Text.Json.Serialization;
using Nupal.Domain.Entities;

namespace NUPAL.Core.Application.DTOs
{
    // Request to RL Service
    public class RlTrainingRequest
    {
        [JsonPropertyName("student_id")]
        public string StudentId { get; set; }

        [JsonPropertyName("education")]
        public RlEducation Education { get; set; }

        [JsonPropertyName("episodes")]
        public int Episodes { get; set; }

        [JsonPropertyName("pretrain_steps")]
        public int PretrainSteps { get; set; }

        [JsonPropertyName("max_semesters")]
        public int MaxSemesters { get; set; }

        [JsonPropertyName("seed")]
        public int Seed { get; set; }

        [JsonPropertyName("profile")]
        public string? Profile { get; set; }

        [JsonPropertyName("profiles")]
        public List<string>? Profiles { get; set; }

        [JsonPropertyName("target_track")]
        public string? TargetTrack { get; set; }
    }

    public class RlEducation
    {
        [JsonPropertyName("total_credits")]
        public double TotalCredits { get; set; }

        [JsonPropertyName("num_semesters")]
        public int NumSemesters { get; set; }

        [JsonPropertyName("semesters")]
        public Dictionary<string, RlSemester> Semesters { get; set; }
    }

    public class RlSemester
    {
        [JsonPropertyName("courses")]
        public List<RlCourse> Courses { get; set; }

        [JsonPropertyName("cumulative_gpa")]
        public double CumulativeGpa { get; set; }

        [JsonPropertyName("optional")]
        public bool Optional { get; set; }

        [JsonPropertyName("semester_credits")]
        public double SemesterCredits { get; set; }

        [JsonPropertyName("semester_gpa")]
        public double SemesterGpa { get; set; }
    }

    public class RlCourse
    {
        [JsonPropertyName("course_id")]
        public string CourseId { get; set; }

        [JsonPropertyName("course_name")]
        public string CourseName { get; set; }

        [JsonPropertyName("credit")]
        public double Credit { get; set; }

        [JsonPropertyName("gpa")]
        public double Gpa { get; set; }

        [JsonPropertyName("grade")]
        public string Grade { get; set; }
    }

    // Response from RL Service
    public class RlTrainingResponse
    {
        [JsonPropertyName("recommended_slates")]
        public List<List<string>> RecommendedSlates { get; set; }

        [JsonPropertyName("terms")]
        public List<RlTermResult> Terms { get; set; }

        [JsonPropertyName("metadata")]
        public RlMetadata Metadata { get; set; }

        [JsonPropertyName("default_profile")]
        public string? DefaultProfile { get; set; }

        [JsonPropertyName("profiles")]
        public Dictionary<string, ProfileRecommendationDto>? Profiles { get; set; }
    }

    public class ProfileRecommendationDto
    {
        [JsonPropertyName("recommended_slates")]
        public List<List<string>> RecommendedSlates { get; set; }

        [JsonPropertyName("terms")]
        public List<RlTermResult> Terms { get; set; }

        [JsonPropertyName("metadata")]
        public RlMetadata Metadata { get; set; }
    }

    public class RlTermResult
    {
        [JsonPropertyName("term")]
        public int Term { get; set; }

        [JsonPropertyName("slate")]
        public List<string> Slate { get; set; }

        [JsonPropertyName("semester_gpa")]
        public double SemesterGpa { get; set; }

        [JsonPropertyName("credits_passed")]
        public double CreditsPassed { get; set; }

        [JsonPropertyName("failed_credits")]
        public double FailedCredits { get; set; }

        [JsonPropertyName("total_credits_so_far")]
        public double TotalCreditsSoFar { get; set; }

        [JsonPropertyName("cumulative_gpa_so_far")]
        public double CumulativeGpaSoFar { get; set; }

        [JsonPropertyName("graduated_so_far")]
        public bool GraduatedSoFar { get; set; }
    }

    public class RlMetadata
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("profile")]
        public string? Profile { get; set; }

        [JsonPropertyName("target_track")]
        public string? TargetTrack { get; set; }

        [JsonPropertyName("total_credits")]
        public double? TotalCredits { get; set; }

        [JsonPropertyName("best_episode")]
        public RlBestEpisode? BestEpisode { get; set; }

        [JsonPropertyName("episodes")]
        public int Episodes { get; set; }

        [JsonPropertyName("graduation_rate")]
        public double GraduationRate { get; set; }

        [JsonPropertyName("top_failed_flags")]
        public object? TopFailedFlags { get; set; }

        [JsonPropertyName("final_total_credits")]
        public double? FinalTotalCredits { get; set; }

        [JsonPropertyName("final_cum_gpa")]
        public double? FinalCumGpa { get; set; }

        [JsonPropertyName("graduated")]
        public bool? Graduated { get; set; }

        [JsonPropertyName("grad_flags")]
        public Dictionary<string, object>? GradFlags { get; set; }
    }

    public class RlBestEpisode
    {
        [JsonPropertyName("cum_gpa")]
        public double CumGpa { get; set; }

        [JsonPropertyName("total_credits")]
        public double TotalCredits { get; set; }

        [JsonPropertyName("graduated")]
        public bool Graduated { get; set; }

        [JsonPropertyName("return_value")]
        public double ReturnValue { get; set; }
    }
}
