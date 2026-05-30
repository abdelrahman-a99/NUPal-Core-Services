using Microsoft.AspNetCore.Mvc;
using NUPAL.Core.Application.Interfaces;

namespace Nupal.Core.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PrecomputeController : ControllerBase
    {
        private readonly IPrecomputeService _precomputeService;

        public PrecomputeController(IPrecomputeService precomputeService)
        {
            _precomputeService = precomputeService;
        }

        [HttpPost]
        public async Task<IActionResult> Trigger([FromBody] PrecomputeRequest request)
        {
            if (string.IsNullOrEmpty(request.StudentId))
            {
                return BadRequest("StudentId is required.");
            }

            try
            {
<<<<<<< HEAD
                var jobId = await _precomputeService.TriggerPrecomputeAsync(request.StudentId, request.IsSimulation, request.Episodes, request.Force);
=======
                var jobId = await _precomputeService.TriggerPrecomputeAsync(request.StudentId, request.IsSimulation, request.Episodes, request.TargetTrack);
>>>>>>> 52b989ddd4a62aca554d0ad28d13d347ca994be6
                return Accepted(new { JobId = jobId, Message = "Precompute job queued." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("recommendations/{id}")]
        public async Task<IActionResult> GetRecommendation(string id)
        {
            var rec = await _precomputeService.GetRecommendationAsync(id);
            if (rec == null) return NotFound();
            return Ok(rec);
        }

        [HttpGet("jobs")]
        public async Task<IActionResult> GetActiveJobs()
        {
            var jobs = await _precomputeService.GetJobStatusAsync();
            return Ok(jobs);
        }
        [HttpPost("sync-all")]
        public async Task<IActionResult> SyncAll([FromQuery] bool isSimulation = false)
        {
            var result = await _precomputeService.SyncAllStudentsAsync(isSimulation);
            return Ok(result);
        }
    }

    public class PrecomputeRequest
    {
        public string? StudentId { get; set; }
        public bool IsSimulation { get; set; }
        public int? Episodes { get; set; } // Optional, default handled in service
        public bool Force { get; set; }
    }
}
