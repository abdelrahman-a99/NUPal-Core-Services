using Microsoft.AspNetCore.Mvc;
using NUPAL.Core.Application.DTOs;
using NUPAL.Core.Application.Interfaces;
using System.Net.Mail;

namespace NUPAL.Core.API.Controllers
{
    [ApiController]
    [Route("api/students")]
    public class StudentsController : ControllerBase
    {
        private readonly IStudentService _service;
        private readonly IConfiguration _config;

        public StudentsController(IStudentService service, IConfiguration config)
        {
            _service = service;
            _config = config;
        }

        [HttpPost("import")]
        public async Task<IActionResult> Import([FromBody] ImportStudentDto req)
        {
            try
            {
                if (req == null || req.Account == null || req.Education == null || string.IsNullOrWhiteSpace(req.Account.Email) || string.IsNullOrWhiteSpace(req.Account.Password) || string.IsNullOrWhiteSpace(req.Account.Id))
                    return BadRequest(new { error = "missing_fields" });

                await _service.UpsertStudentAsync(req);
                return Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "server_error", message = ex.Message });
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto body)
        {
            try
            {
                if (body == null)
                    return BadRequest(new { error = "missing_fields" });

                var email = body.Email?.Trim().ToLower();
                var password = body.Password ?? string.Empty;

                if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                    return BadRequest(new { error = "missing_fields" });

                // Basic email format validation
                bool validEmail;
                try { var addr = new MailAddress(email); validEmail = addr.Address == email; } catch { validEmail = false; }
                if (!validEmail)
                    return BadRequest(new { error = "invalid_email" });

                // Basic password policy (min length)
                if (password.Length < 6)
                    return BadRequest(new { error = "invalid_password_format" });

                var jwtKey = _config["Jwt:Key"] ?? throw new InvalidOperationException("Missing Jwt:Key configuration");
                var jwtIssuer = _config["Jwt:Issuer"] ?? throw new InvalidOperationException("Missing Jwt:Issuer configuration");
                var jwtAudience = _config["Jwt:Audience"] ?? throw new InvalidOperationException("Missing Jwt:Audience configuration");

                var authResponse = await _service.AuthenticateAsync(new LoginDto { Email = email, Password = password }, jwtKey, jwtIssuer, jwtAudience);

                if (authResponse == null) return Unauthorized(new { error = "incorrect_email_or_password" });

                return Ok(new { ok = true, token = authResponse.Token, student = authResponse.Student });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "server_error", message = ex.Message });
            }
        }

        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        [HttpGet("by-email/{email}")]
        public async Task<IActionResult> GetByEmail([FromRoute] string email)
        {
            try
            {
                var s = await _service.GetStudentByEmailAsync(email);
                if (s == null) return NotFound(new { error = "not_found" });
                return Ok(s);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "server_error", message = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById([FromRoute] string id)
        {
            try
            {
                var s = await _service.GetStudentByIdAsync(id);
                if (s == null) return NotFound(new { error = "not_found" });
                return Ok(s);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "server_error", message = ex.Message });
            }
        }
        [HttpGet("{id}/rl-recommendation")]
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public async Task<IActionResult> GetLatestRlRecommendation([FromRoute] string id, [FromServices] IRlRecommendationRepository rlRepo, [FromServices] ICourseMappingRepository mappingRepo)
        {
            try
            {
                var s = await _service.GetStudentByIdAsync(id);
                if (s == null) return NotFound(new { error = "student_not_found" });

                var rlRecommendation = await rlRepo.GetLatestByStudentIdAsync(id);
                if (rlRecommendation == null) return NotFound(new { error = "no_recommendation_found" });

                var mappings = await mappingRepo.GetAllAsync();

                var displayCourses = rlRecommendation.Courses.Select(c =>
                {
                    var lower = c.Trim().ToLower();
                    var mapping = mappings.FirstOrDefault(m =>
                        (m.CourseCode != null && m.CourseCode.ToLower() == lower) ||
                        m.GetAllNames().Any(n => n.ToLower() == lower)
                    );

                    if (mapping != null)
                    {
                        var name = mapping.CourseNames?.FirstOrDefault() ?? mapping.CourseCode;
                        return name;
                    }

                    return c; // fallback: return raw code as-is
                }).Distinct().ToList();

                return Ok(new
                {
                    id = rlRecommendation.Id.ToString(),
                    termIndex = rlRecommendation.TermIndex,
                    courses = displayCourses,
                    slates = rlRecommendation.SlatesByTerm,
                    metrics = rlRecommendation.Metrics,
                    createdAt = rlRecommendation.CreatedAt
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "server_error", message = ex.Message });
            }
        }
    }
}
