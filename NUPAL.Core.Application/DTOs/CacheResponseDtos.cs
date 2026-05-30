namespace NUPAL.Core.Application.DTOs;

public class RlRecommendationResponseDto
{
    public string Id { get; set; } = string.Empty;
    public int TermIndex { get; set; }
    public List<string> Courses { get; set; } = new();
    public object? Slates { get; set; }
    public object? Metrics { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? DefaultProfile { get; set; }
    public object? Profiles { get; set; }
}

public class ResumeAnalysisResponseDto
{
    public string Id { get; set; } = string.Empty;
    public string StudentEmail { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime AnalyzedAt { get; set; }
    public object? Data { get; set; }
}

public class JobFitResponseDto
{
    public string Id { get; set; } = string.Empty;
    public string StudentEmail { get; set; } = string.Empty;
    public string JobUrl { get; set; } = string.Empty;
    public string JobText { get; set; } = string.Empty;
    public DateTime AnalyzedAt { get; set; }
    public string AnalysisJson { get; set; } = string.Empty;
}
