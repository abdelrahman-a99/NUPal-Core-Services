using Nupal.Domain.Entities;

namespace NUPAL.Core.Application.Interfaces
{
    public interface IAgentPipelineTraceRepository
    {
        Task CreateAsync(AgentPipelineTrace trace);
        Task<AgentPipelineTrace?> GetByTraceIdAsync(string traceId);
        Task<List<AgentPipelineTrace>> GetRecentByConversationAsync(string conversationId, int limit = 30);
        Task<List<AgentPipelineTrace>> GetRecentByStudentAsync(string studentId, int limit = 50);
    }
}