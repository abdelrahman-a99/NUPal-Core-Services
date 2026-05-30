namespace NUPAL.Core.Application.DTOs
{
    // ── Student Admin DTOs ────────────────────────────────────────────────────

    public class AdminStudentSummaryDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public double TotalCredits { get; set; }
        public int NumSemesters { get; set; }
        public int TotalCourses { get; set; }
        public double CumulativeGpa { get; set; }
        public double LatestSemesterGpa { get; set; }
        public string LatestTerm { get; set; } = string.Empty;
        public string? LatestRecommendationId { get; set; }
    }

    public class AdminStudentDetailDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? LatestRecommendationId { get; set; }
        public AdminEducationDto Education { get; set; } = new();
    }

    public class AdminEducationDto
    {
        public double TotalCredits { get; set; }
        public int NumSemesters { get; set; }
        public List<AdminSemesterDto> Semesters { get; set; } = new();
    }

    public class AdminSemesterDto
    {
        public string Term { get; set; } = string.Empty;
        public bool Optional { get; set; }
        public double SemesterCredits { get; set; }
        public double SemesterGpa { get; set; }
        public double CumulativeGpa { get; set; }
        public List<AdminCourseDto> Courses { get; set; } = new();
    }

    public class AdminCourseDto
    {
        public string CourseId { get; set; } = string.Empty;
        public string CourseName { get; set; } = string.Empty;
        public double Credit { get; set; }
        public string Grade { get; set; } = string.Empty;
        public double? Gpa { get; set; }
    }

    // ── RL Engine DTOs ────────────────────────────────────────────────────────

    public class AdminRlJobDto
    {
        public string Id { get; set; } = string.Empty;
        public string StudentId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public bool IsSimulation { get; set; }
        public string? ResultRecommendationId { get; set; }
        public string? Error { get; set; }
        public string? EducationHash { get; set; }
    }

    public class AdminRlRecommendationDto
    {
        public string Id { get; set; } = string.Empty;
        public string StudentId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int TermIndex { get; set; }
        public List<string> Courses { get; set; } = new();
        public object? SlatesByTerm { get; set; }
        public object? Metrics { get; set; }
        public string? ModelVersion { get; set; }
        public string? PolicyVersion { get; set; }
        public string? DefaultProfile { get; set; }
        public object? Profiles { get; set; }
    }

    // ── System Stats DTOs ─────────────────────────────────────────────────────

    public class AdminSystemStatsDto
    {
        public AdminStudentStatsDto Students { get; set; } = new();
        public AdminRlStatsDto RlJobs { get; set; } = new();
        public AdminCountDto CourseMappings { get; set; } = new();
        public AdminCountDto SchedulingBlocks { get; set; } = new();
        public string ActiveSemester { get; set; } = string.Empty;
        public List<string> AvailableSemesters { get; set; } = new();
    }

    public class AdminStudentStatsDto
    {
        public int Total { get; set; }
        public double AverageGpa { get; set; }
        public int StudentsWithSchedules { get; set; }
        public Dictionary<string, int> LevelDistribution { get; set; } = new();
    }

    public class AdminRlStatsDto
    {
        public int Total { get; set; }
        public Dictionary<string, int> ByStatus { get; set; } = new();
    }

    public class AdminCountDto
    {
        public int Total { get; set; }
        public Dictionary<string, int>? CategoryDistribution { get; set; }
    }

    // ── Course Mapping DTOs ───────────────────────────────────────────────────

    public class CourseMappingUpsertDto
    {
        public string CourseCode { get; set; } = string.Empty;
        public List<string> CourseNames { get; set; } = new();
        public int Credits { get; set; }
        public string Category { get; set; } = string.Empty;
    }

    // ── Student Filter DTOs ───────────────────────────────────────────────────

    public class AdminStudentFilterDto
    {
        public string? Search { get; set; }
        public double? MinGpa { get; set; }
        public double? MaxGpa { get; set; }
    }
}
