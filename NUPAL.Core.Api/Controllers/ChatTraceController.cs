using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nupal.Domain.Entities;
using NUPAL.Core.Application.DTOs;
using NUPAL.Core.Application.Interfaces;

namespace NUPAL.Core.API.Controllers
{
    [ApiController]
    [Route("api/chat")]
    [Authorize]
    public class ChatTraceController : ControllerBase
    {
        private readonly IAgentPipelineTraceRepository _traceRepo;
        private readonly IChatConversationRepository _convoRepo;

        public ChatTraceController(
            IAgentPipelineTraceRepository traceRepo,
            IChatConversationRepository convoRepo)
        {
            _traceRepo = traceRepo;
            _convoRepo = convoRepo;
        }

        [HttpGet("traces/{traceId}")]
        public async Task<IActionResult> GetTrace(string traceId)
        {
            var studentId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(studentId))
                return Unauthorized(new { error = "unauthorized" });

            var trace = await _traceRepo.GetByTraceIdAsync(traceId);
            if (trace == null)
                return NotFound(new { error = "trace_not_found" });

            if (trace.StudentId != studentId)
                return Forbid();

            return Ok(ToDto(trace));
        }

        [HttpGet("conversations/{conversationId}/traces")]
        public async Task<IActionResult> GetConversationTraces(string conversationId)
        {
            var studentId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(studentId))
                return Unauthorized(new { error = "unauthorized" });

            var convo = await _convoRepo.GetByIdAsync(conversationId);
            if (convo == null)
                return NotFound(new { error = "conversation_not_found" });

            if (convo.StudentId != studentId)
                return Forbid();

            var traces = await _traceRepo.GetRecentByConversationAsync(conversationId, 50);
            return Ok(traces.Select(ToDto).ToList());
        }

        private static AgentPipelineTraceDto ToDto(AgentPipelineTrace trace)
        {
            return new AgentPipelineTraceDto
            {
                Id = trace.Id.ToString(),
                TraceId = trace.TraceId,
                AgentTraceId = trace.AgentTraceId,
                StudentId = trace.StudentId,
                ConversationId = trace.ConversationId,
                UserMessage = trace.UserMessage,
                UserMessageId = trace.UserMessageId,
                AssistantMessageIds = trace.AssistantMessageIds,
                Status = trace.Status,
                AgentRoute = trace.AgentRoute,
                AgentIntent = trace.AgentIntent,
                AgentUserKind = trace.AgentUserKind,
                AgentStatus = trace.AgentStatus,
                RouteConfidence = trace.RouteConfidence,
                RouteReason = trace.RouteReason,
                TotalDurationMs = trace.TotalDurationMs,
                AgentRequestJson = trace.AgentRequestJson,
                AgentResponseJson = trace.AgentResponseJson,
                Error = trace.Error,
                CreatedAt = trace.CreatedAt,
                CompletedAt = trace.CompletedAt,
                Events = trace.Events.Select(e => new AgentPipelineTraceEventDto
                {
                    Order = e.Order,
                    Stage = e.Stage,
                    Status = e.Status,
                    At = e.At,
                    DurationMs = e.DurationMs,
                    DataJson = e.DataJson,
                    Error = e.Error
                }).ToList()
            };
        }
    }
}
