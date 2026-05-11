using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NUPAL.Core.Application.DTOs;
using NUPAL.Core.Application.Interfaces;
using System.Security.Claims;

namespace NUPAL.Core.Api.Controllers
{
    [ApiController]
    [Route("api/scheduling")]
    public class SchedulingController : ControllerBase
    {
        private readonly ISchedulingService _schedulingService;
        private readonly ILogger<SchedulingController> _logger;

        public SchedulingController(
            ISchedulingService schedulingService,
            ILogger<SchedulingController> logger)
        {
            _schedulingService = schedulingService;
            _logger            = logger;
        }


        [HttpGet("active-semester")]
        [AllowAnonymous]
        public async Task<IActionResult> GetActiveSemester()
        {
            try
            {
                var semester = await _schedulingService.GetActiveSemesterAsync();
                return Ok(new { semester });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching active semester");
                return StatusCode(500, new { message = "Error fetching active semester" });
            }
        }

        [HttpGet("blocks")]
        [AllowAnonymous]
        public async Task<IActionResult> GetBlocks([FromQuery] string? level = null)
        {
            try
            {
                var blocks = await _schedulingService.GetMappedBlocksAsync(level);
                return Ok(blocks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching scheduling blocks");
                return StatusCode(500, new { message = "Error fetching blocks" });
            }
        }

        [HttpGet("blocks/{blockId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetBlock(string blockId)
        {
            try
            {
                var block = await _schedulingService.GetMappedBlockAsync(blockId);
                if (block == null)
                    return NotFound(new { message = $"Block '{blockId}' not found" });

                return Ok(block);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching block {BlockId}", blockId);
                return StatusCode(500, new { message = "Error fetching block" });
            }
        }


        [HttpGet("courses")]
        [AllowAnonymous]
        public async Task<IActionResult> GetCourseNames([FromQuery] string? level = null)
        {
            try
            {
                var names = await _schedulingService.GetCourseNamesAsync(level);
                return Ok(names);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching course names");
                return StatusCode(500, new { message = "Error fetching course names" });
            }
        }


        [HttpGet("instructors")]
        [AllowAnonymous]
        public async Task<IActionResult> GetInstructors([FromQuery] string? level = null)
        {
            try
            {
                var instructors = await _schedulingService.GetInstructorsAsync(level);
                return Ok(instructors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching instructors");
                return StatusCode(500, new { message = "Error fetching instructors" });
            }
        }

        [HttpPost("instructors/categorized")]
        [AllowAnonymous]
        public async Task<IActionResult> GetCategorizedInstructors([FromBody] List<string> courseNames, [FromQuery] string? level = null)
        {
            try
            {
                if (courseNames == null || courseNames.Count == 0)
                {
                    return Ok(new CategorizedInstructorsDto());
                }

                var result = await _schedulingService.GetCategorizedInstructorsAsync(courseNames, level);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching categorized instructors");
                return StatusCode(500, new { message = "Error fetching categorized instructors" });
            }
        }


        [HttpPost("recommend")]
        [AllowAnonymous]
        public async Task<IActionResult> Recommend([FromBody] RecommendationRequestDto request)
        {
            try
            {
                if (request?.Preferences == null)
                    return BadRequest(new { message = "Preferences are required" });

                var results = await _schedulingService.RecommendAsync(request);
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running scheduling recommender");
                return StatusCode(500, new { message = "Error running recommender" });
            }
        }


        [HttpPost("seed")]
        [AllowAnonymous]
        public async Task<IActionResult> SeedBlocks([FromBody] BlockSeedRequestDto request)
        {
            try
            {
                if (request?.Blocks == null || request.Blocks.Count == 0)
                    return BadRequest(new { message = "No blocks provided" });

                var count = await _schedulingService.SeedBlocksAsync(request.Blocks);
                _logger.LogInformation("Scheduling seed: {Count} blocks upserted", count);

                return Ok(new
                {
                    message = $"Successfully seeded {count} block(s) into MongoDB.",
                    count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error seeding scheduling blocks");
                return StatusCode(500, new { message = "Error seeding blocks" });
            }
        }

        [HttpPost("register")]
        [Authorize]
        public async Task<IActionResult> Register([FromBody] RegistrationRequestDto request)
        {
            try
            {
                if (request == null) return BadRequest(new { message = "Request body is required" });
                await _schedulingService.RegisterScheduleAsync(request);
                return Ok(new { message = "Schedule registration submitted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering schedule");
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpGet("my-registration")]
        [Authorize]
        public async Task<IActionResult> GetMyRegistration()
        {
            try
            {
                var studentId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(studentId)) return Unauthorized();
                
                var reg = await _schedulingService.GetRegistrationByStudentIdAsync(studentId);
                return Ok(reg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching student registration");
                return StatusCode(500, new { message = "Error fetching registration" });
            }
        }

        [HttpGet("latest-registration")]
        [Authorize]
        public async Task<IActionResult> GetLatestRegistration()
        {
            try
            {
                var studentId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(studentId)) return Unauthorized();
                
                var reg = await _schedulingService.GetLatestRegistrationByStudentIdAsync(studentId);
                return Ok(reg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching latest student registration");
                return StatusCode(500, new { message = "Error fetching latest registration" });
            }
        }
    }
}
