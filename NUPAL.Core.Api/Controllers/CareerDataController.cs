using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nupal.Domain.Entities;
using NUPAL.Core.Application.DTOs;
using NUPAL.Core.Application.Interfaces;

namespace NUPAL.Core.Api.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/career-data")]
    public class CareerDataController : ControllerBase
    {
        private readonly IResumeRepository _resumeRepository;
        private readonly IJobFitRepository _jobFitRepository;
        private readonly ICacheService _cache;

        public CareerDataController(IResumeRepository resumeRepository, IJobFitRepository jobFitRepository, ICacheService cache)
        {
            _resumeRepository = resumeRepository;
            _jobFitRepository = jobFitRepository;
            _cache = cache;
        }

        [HttpPost("resume-analyses")]
        public async Task<IActionResult> CreateResumeAnalysis(
            [FromQuery] string studentEmail,
            [FromBody] CreateResumeAnalysisRequest request)
        {
            if (string.IsNullOrWhiteSpace(studentEmail))
                return BadRequest(new { detail = "studentEmail is required" });
            if (request == null || string.IsNullOrWhiteSpace(request.FileName))
                return BadRequest(new { detail = "fileName is required" });
            if (!OwnsStudentEmail(studentEmail))
                return Forbid();

            var model = new ResumeAnalysis
            {
                StudentEmail = studentEmail.Trim(),
                FileName = request.FileName.Trim(),
                Data = request.Data ?? new ResumeData(),
                AnalyzedAt = DateTime.UtcNow
            };

            await _resumeRepository.SaveAsync(model);

            // Invalidate cache
            await _cache.RemoveAsync($"resumes:{studentEmail.Trim().ToLower()}");

            return Ok(new { id = model.Id.ToString() });
        }

        [HttpGet("resume-analyses")]
        public async Task<IActionResult> ListResumeAnalyses([FromQuery] string studentEmail)
        {
            if (string.IsNullOrWhiteSpace(studentEmail))
                return BadRequest(new { detail = "studentEmail is required" });
            if (!OwnsStudentEmail(studentEmail))
                return Forbid();

            var key = $"resumes:{studentEmail.Trim().ToLower()}";
            var cached = await _cache.GetAsync<List<ResumeAnalysisResponseDto>>(key);
            if (cached is not null)
                return Ok(cached);

            var rows = await _resumeRepository.GetByStudentEmailAsync(studentEmail.Trim());
            var payload = rows.Select(x => new ResumeAnalysisResponseDto
            {
                Id = x.Id.ToString(),
                StudentEmail = x.StudentEmail,
                FileName = x.FileName,
                AnalyzedAt = x.AnalyzedAt,
                Data = x.Data
            }).ToList();

            await _cache.SetAsync(key, payload, TimeSpan.FromMinutes(15));
            return Ok(payload);
        }

        [HttpGet("resume-analyses/{id}")]
        public async Task<IActionResult> GetResumeAnalysis([FromRoute] string id, [FromQuery] string studentEmail)
        {
            if (string.IsNullOrWhiteSpace(studentEmail))
                return BadRequest(new { detail = "studentEmail is required" });
            if (!OwnsStudentEmail(studentEmail))
                return Forbid();

            var row = await _resumeRepository.GetByIdAsync(id);
            if (row == null || !EmailEquals(row.StudentEmail, studentEmail))
                return NotFound(new { detail = "Resume not found" });

            return Ok(new
            {
                Id = row.Id.ToString(),
                row.StudentEmail,
                row.FileName,
                row.AnalyzedAt,
                row.Data
            });
        }

        [HttpDelete("resume-analyses/{id}")]
        public async Task<IActionResult> DeleteResumeAnalysis([FromRoute] string id, [FromQuery] string studentEmail)
        {
            if (string.IsNullOrWhiteSpace(studentEmail))
                return BadRequest(new { detail = "studentEmail is required" });
            if (!OwnsStudentEmail(studentEmail))
                return Forbid();

            var row = await _resumeRepository.GetByIdAsync(id);
            if (row == null || !EmailEquals(row.StudentEmail, studentEmail))
                return NotFound(new { detail = "Resume not found" });

            await _resumeRepository.DeleteAsync(id);

            // Invalidate cache
            await _cache.RemoveAsync($"resumes:{studentEmail.Trim().ToLower()}");

            return NoContent();
        }

        [HttpPost("job-fit-results")]
        public async Task<IActionResult> CreateJobFitResult(
            [FromQuery] string studentEmail,
            [FromBody] CreateJobFitResultRequest request)
        {
            if (string.IsNullOrWhiteSpace(studentEmail))
                return BadRequest(new { detail = "studentEmail is required" });
            if (request == null)
                return BadRequest(new { detail = "request body is required" });
            if (!OwnsStudentEmail(studentEmail))
                return Forbid();

            var model = new JobFitResult
            {
                StudentEmail = studentEmail.Trim(),
                JobUrl = request.JobUrl ?? string.Empty,
                JobText = request.JobText ?? string.Empty,
                AnalysisJson = request.AnalysisJson ?? "{}",
                AnalyzedAt = DateTime.UtcNow
            };

            await _jobFitRepository.SaveAsync(model);

            // Invalidate cache
            await _cache.RemoveAsync($"jobfits:{studentEmail.Trim().ToLower()}");

            return Ok(new { id = model.Id.ToString() });
        }

        [HttpGet("job-fit-results")]
        public async Task<IActionResult> ListJobFitResults([FromQuery] string studentEmail)
        {
            if (string.IsNullOrWhiteSpace(studentEmail))
                return BadRequest(new { detail = "studentEmail is required" });
            if (!OwnsStudentEmail(studentEmail))
                return Forbid();

            var key = $"jobfits:{studentEmail.Trim().ToLower()}";
            var cached = await _cache.GetAsync<List<JobFitResponseDto>>(key);
            if (cached is not null)
                return Ok(cached);

            var rows = await _jobFitRepository.GetByStudentEmailAsync(studentEmail.Trim());
            var payload = rows.Select(x => new JobFitResponseDto
            {
                Id = x.Id.ToString(),
                StudentEmail = x.StudentEmail,
                JobUrl = x.JobUrl,
                JobText = x.JobText,
                AnalyzedAt = x.AnalyzedAt,
                AnalysisJson = x.AnalysisJson
            }).ToList();

            await _cache.SetAsync(key, payload, TimeSpan.FromMinutes(15));
            return Ok(payload);
        }

        [HttpGet("job-fit-results/{id}")]
        public async Task<IActionResult> GetJobFitResult([FromRoute] string id, [FromQuery] string studentEmail)
        {
            if (string.IsNullOrWhiteSpace(studentEmail))
                return BadRequest(new { detail = "studentEmail is required" });
            if (!OwnsStudentEmail(studentEmail))
                return Forbid();

            var row = await _jobFitRepository.GetByIdAsync(id);
            if (row == null || !EmailEquals(row.StudentEmail, studentEmail))
                return NotFound(new { detail = "Job fit not found" });

            return Ok(new
            {
                Id = row.Id.ToString(),
                row.StudentEmail,
                row.JobUrl,
                row.JobText,
                row.AnalyzedAt,
                row.AnalysisJson
            });
        }

        [HttpDelete("job-fit-results/{id}")]
        public async Task<IActionResult> DeleteJobFitResult([FromRoute] string id, [FromQuery] string studentEmail)
        {
            if (string.IsNullOrWhiteSpace(studentEmail))
                return BadRequest(new { detail = "studentEmail is required" });
            if (!OwnsStudentEmail(studentEmail))
                return Forbid();

            var row = await _jobFitRepository.GetByIdAsync(id);
            if (row == null || !EmailEquals(row.StudentEmail, studentEmail))
                return NotFound(new { detail = "Job fit not found" });

            await _jobFitRepository.DeleteAsync(id);

            // Invalidate cache
            await _cache.RemoveAsync($"jobfits:{studentEmail.Trim().ToLower()}");

            return NoContent();
        }

        private bool OwnsStudentEmail(string studentEmail)
        {
            var fromToken = UserEmailFromClaims();
            if (string.IsNullOrWhiteSpace(fromToken))
                return false;
            return EmailEquals(fromToken, studentEmail);
        }

        private string UserEmailFromClaims()
        {
            return User.FindFirstValue(ClaimTypes.Email)
                ?? User.FindFirstValue(ClaimTypes.Name)
                ?? User.FindFirstValue("email")
                ?? User.FindFirstValue("unique_name")
                ?? User.Identity?.Name
                ?? string.Empty;
        }

        private static bool EmailEquals(string a, string b)
        {
            return string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    public class CreateResumeAnalysisRequest
    {
        public string FileName { get; set; } = string.Empty;
        public ResumeData Data { get; set; } = new();
    }

    public class CreateJobFitResultRequest
    {
        public string JobUrl { get; set; } = string.Empty;
        public string JobText { get; set; } = string.Empty;
        public string AnalysisJson { get; set; } = "{}";
    }
}
