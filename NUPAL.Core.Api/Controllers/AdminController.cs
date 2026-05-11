using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NUPAL.Core.Application.DTOs;
using NUPAL.Core.Application.Interfaces;
using System.Security.Claims;

namespace NUPAL.Core.Api.Controllers
{
    [ApiController]
    [Route("api/admin")]
    [Authorize] 
    public class AdminController : ControllerBase
    {
        private readonly IAdminService _adminService;

        public AdminController(IAdminService adminService)
        {
            _adminService = adminService;
        }

        // ── Role Guard ────────────────────────────────────────────────────────
        private bool IsAdmin() =>
            User.Claims.Any(c =>
                c.Type == ClaimTypes.Role && c.Value == "admin");

        // ── Students ──────────────────────────────────────────────────────────

        [HttpGet("students")]
        public async Task<IActionResult> GetAllStudents(
            [FromQuery] string? search = null,
            [FromQuery] double? minGpa = null,
            [FromQuery] double? maxGpa = null)
        {
            if (!IsAdmin()) return Unauthorized(new { error = "admin_key_required" });
            try
            {
                var filter = new AdminStudentFilterDto { Search = search, MinGpa = minGpa, MaxGpa = maxGpa };
                var result = await _adminService.GetAllStudentsAsync(filter);
                return Ok(result);
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        [HttpGet("students/{id}")]
        public async Task<IActionResult> GetStudentById(string id)
        {
            if (!IsAdmin()) return Unauthorized(new { error = "admin_key_required" });
            try
            {
                var result = await _adminService.GetStudentByIdAsync(id);
                if (result == null) return NotFound(new { error = "student_not_found" });
                return Ok(result);
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        // ── RL Engine ─────────────────────────────────────────────────────────

        [HttpGet("rl/jobs")]
        public async Task<IActionResult> GetRlJobs()
        {
            if (!IsAdmin()) return Unauthorized(new { error = "admin_key_required" });
            try
            {
                var result = await _adminService.GetAllRlJobsAsync();
                return Ok(result);
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        [HttpGet("rl/recommendations/{studentId}")]
        public async Task<IActionResult> GetStudentRecommendation(string studentId)
        {
            if (!IsAdmin()) return Unauthorized(new { error = "admin_key_required" });
            try
            {
                var result = await _adminService.GetStudentRecommendationAsync(studentId);
                if (result == null) return NotFound(new { error = "no_recommendation" });
                return Ok(result);
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        [HttpPost("rl/trigger/{studentId}")]
        public async Task<IActionResult> TriggerRl(string studentId, [FromQuery] bool isSimulation = false)
        {
            if (!IsAdmin()) return Unauthorized(new { error = "admin_key_required" });
            try
            {
                var jobId = await _adminService.TriggerRlJobAsync(studentId, isSimulation);
                return Accepted(new { jobId, message = "RL job queued" });
            }
            catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        [HttpPost("rl/sync-all")]
        public async Task<IActionResult> SyncAll([FromQuery] bool isSimulation = false)
        {
            if (!IsAdmin()) return Unauthorized(new { error = "admin_key_required" });
            try
            {
                var result = await _adminService.SyncAllStudentsAsync(isSimulation);
                return Ok(result);
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        [HttpDelete("rl/jobs/{jobId}")]
        public async Task<IActionResult> DeleteRlJob(string jobId)
        {
            if (!IsAdmin()) return Unauthorized(new { error = "admin_key_required" });
            try
            {
                await _adminService.DeleteRlJobAsync(jobId);
                return Ok(new { message = "Job deleted" });
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        // ── Registrations ─────────────────────────────────────────────────────

        [HttpGet("registrations")]
        public async Task<IActionResult> GetRegistrations()
        {
            if (!IsAdmin()) return Unauthorized(new { error = "admin_key_required" });
            try
            {
                var regs = await _adminService.GetAllRegistrationsAsync();
                return Ok(regs);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("registrations/{id}/approve")]
        public async Task<IActionResult> ApproveRegistration(string id, [FromBody] ApproveRegistrationDto dto)
        {
            if (!IsAdmin()) return Unauthorized(new { error = "admin_key_required" });
            try
            {
                await _adminService.ApproveRegistrationAsync(id, dto);
                return Ok(new { message = $"Registration {dto.Status} successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // ── Course Mappings ───────────────────────────────────────────────────

        [HttpGet("course-mappings")]
        public async Task<IActionResult> GetAllMappings()
        {
            if (!IsAdmin()) return Unauthorized(new { error = "admin_key_required" });
            try
            {
                var result = await _adminService.GetAllCourseMappingsAsync();
                return Ok(result);
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        [HttpPost("course-mappings")]
        public async Task<IActionResult> CreateMapping([FromBody] CourseMappingUpsertDto dto)
        {
            if (!IsAdmin()) return Unauthorized(new { error = "admin_key_required" });
            try
            {
                await _adminService.CreateCourseMappingAsync(dto);
                return Ok(new { message = "Mapping created" });
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        [HttpPut("course-mappings/{id}")]
        public async Task<IActionResult> UpdateMapping(string id, [FromBody] CourseMappingUpsertDto dto)
        {
            if (!IsAdmin()) return Unauthorized(new { error = "admin_key_required" });
            try
            {
                await _adminService.UpdateCourseMappingAsync(id, dto);
                return Ok(new { message = "Mapping updated" });
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        [HttpDelete("course-mappings")]
        public async Task<IActionResult> DeleteAllMappings()
        {
            if (!IsAdmin()) return Unauthorized(new { error = "admin_key_required" });
            try
            {
                await _adminService.DeleteAllCourseMappingsAsync();
                return Ok(new { message = "All mappings deleted" });
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        // ── System Stats ──────────────────────────────────────────────────────

        [HttpGet("stats")]
        public async Task<IActionResult> GetSystemStats()
        {
            if (!IsAdmin()) return Unauthorized(new { error = "admin_key_required" });
            try
            {
                var result = await _adminService.GetSystemStatsAsync();
                return Ok(result);
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        [HttpPut("settings/active-semester")]
        public async Task<IActionResult> UpdateActiveSemester([FromBody] string semester)
        {
            if (!IsAdmin()) return Unauthorized(new { error = "admin_key_required" });
            try
            {
                await _adminService.UpdateActiveSemesterAsync(semester);
                return Ok(new { message = "Active semester updated" });
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        // ── Scheduling Blocks ─────────────────────────────────────────────────

        [HttpGet("blocks")]
        public async Task<IActionResult> GetBlocks([FromQuery] string? level = null, [FromQuery] string? semester = null)
        {
            if (!IsAdmin()) return Unauthorized(new { error = "admin_required" });
            try
            {
                var result = await _adminService.GetAllBlocksAsync(level, semester);
                return Ok(result);
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        [HttpPost("blocks")]
        public async Task<IActionResult> CreateBlock([FromBody] BlockDto dto)
        {
            if (!IsAdmin()) return Unauthorized(new { error = "admin_required" });
            try
            {
                await _adminService.CreateBlockAsync(dto);
                return Ok(new { message = "Block created successfully" });
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        [HttpPut("blocks/{blockId}")]
        public async Task<IActionResult> UpdateBlock(string blockId, [FromQuery] string? semester, [FromBody] BlockDto dto)
        {
            if (!IsAdmin()) return Unauthorized(new { error = "admin_required" });
            try
            {
                await _adminService.UpdateBlockAsync(blockId, semester, dto);
                return Ok(new { message = "Block updated successfully" });
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        [HttpDelete("blocks/{blockId}")]
        public async Task<IActionResult> DeleteBlock(string blockId, [FromQuery] string? semester)
        {
            if (!IsAdmin()) return Unauthorized(new { error = "admin_required" });
            try
            {
                await _adminService.DeleteBlockAsync(blockId, semester);
                return Ok(new { message = "Block deleted successfully" });
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        [HttpPost("blocks/parse-pdf")]
        public async Task<IActionResult> ParseSchedulePdf(IFormFile file)
        {
            if (!IsAdmin()) return Unauthorized(new { error = "admin_required" });
            if (file == null || file.Length == 0) return BadRequest(new { error = "No file uploaded" });

            try
            {
                using var stream = file.OpenReadStream();
                var result = await _adminService.ParseSchedulePdfAsync(stream);
                return Ok(result);
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }
    }
}
