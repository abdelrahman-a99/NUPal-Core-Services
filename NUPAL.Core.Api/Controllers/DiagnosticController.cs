using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NUPAL.Core.Application.Interfaces;
using System.Security.Claims;

namespace NUPAL.Core.API.Controllers
{
    [ApiController]
    [Route("api/diagnostic")]
    [Authorize]
    public class DiagnosticController : ControllerBase
    {
        private readonly IStudentRepository _studentRepo;
        private readonly IRlRecommendationRepository _rlRepo;
        private readonly IChatConversationRepository _convoRepo;
        private readonly IChatMessageRepository _msgRepo;

        public DiagnosticController(
            IStudentRepository studentRepo,
            IRlRecommendationRepository rlRepo,
            IChatConversationRepository convoRepo,
            IChatMessageRepository msgRepo)
        {
            _studentRepo = studentRepo;
            _rlRepo = rlRepo;
            _convoRepo = convoRepo;
            _msgRepo = msgRepo;
        }

        [HttpGet("check-rl")]
        public async Task<IActionResult> CheckRlRecommendation()
        {
            var studentId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(studentId))
                return Unauthorized(new { error = "unauthorized" });

            var student = await _studentRepo.GetByIdAsync(studentId);
            if (student == null)
            {
                return Ok(new
                {
                    studentFound = false,
                    studentId,
                    message = "Student not found in database"
                });
            }

            var rlRecommendation = await _rlRepo.GetLatestByStudentIdAsync(studentId);
            
            return Ok(new
            {
                studentFound = true,
                studentId,
                studentEmail = student.Account?.Email,
                latestRecommendationId = student.LatestRecommendationId,
                rlRecommendationFound = rlRecommendation != null,
                rlData = rlRecommendation == null ? null : new
                {
                    id = rlRecommendation.Id.ToString(),
                    termIndex = rlRecommendation.TermIndex,
                    coursesCount = rlRecommendation.Courses?.Count ?? 0,
                    courses = rlRecommendation.Courses,
                    slatesCount = rlRecommendation.SlatesByTerm?.Count ?? 0,
                    targetTrack = rlRecommendation.TargetTrack,
                    objectiveProfile = rlRecommendation.ObjectiveProfile,
                    modelVersion = rlRecommendation.ModelVersion,
                    policyVersion = rlRecommendation.PolicyVersion,
                    createdAt = rlRecommendation.CreatedAt,
                    defaultProfile = rlRecommendation.DefaultProfile,
                    profilesCount = rlRecommendation.Profiles?.Count ?? 0
                }
            });
        }

        [HttpGet("check-conversations")]
        public async Task<IActionResult> CheckConversations()
        {
            var studentId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(studentId))
                return Unauthorized(new { error = "unauthorized" });

            var allConversations = new List<string>();
            var conversationDetails = new List<object>();

            return Ok(new
            {
                studentId,
                message = "Conversation listing requires GetByStudentIdAsync method to be implemented in IChatConversationRepository",
                suggestion = "Check individual conversation by ID or implement the missing repository method"
            });
        }
    }
}
