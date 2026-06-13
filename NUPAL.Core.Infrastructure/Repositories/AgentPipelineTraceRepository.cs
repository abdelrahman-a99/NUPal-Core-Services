using MongoDB.Driver;
using Nupal.Domain.Entities;
using NUPAL.Core.Application.Interfaces;

namespace Nupal.Core.Infrastructure.Repositories
{
    public class AgentPipelineTraceRepository : IAgentPipelineTraceRepository
    {
        private readonly IMongoCollection<AgentPipelineTrace> _col;

        public AgentPipelineTraceRepository(IMongoDatabase db)
        {
            _col = db.GetCollection<AgentPipelineTrace>("agent_pipeline_traces");

            try
            {
                var indexes = new[]
                {
                    new CreateIndexModel<AgentPipelineTrace>(
                        Builders<AgentPipelineTrace>.IndexKeys.Ascending(x => x.TraceId),
                        new CreateIndexOptions { Unique = true }),

                    new CreateIndexModel<AgentPipelineTrace>(
                        Builders<AgentPipelineTrace>.IndexKeys.Ascending(x => x.AgentTraceId)),

                    new CreateIndexModel<AgentPipelineTrace>(
                        Builders<AgentPipelineTrace>.IndexKeys
                            .Ascending(x => x.ConversationId)
                            .Descending(x => x.CreatedAt)),

                    new CreateIndexModel<AgentPipelineTrace>(
                        Builders<AgentPipelineTrace>.IndexKeys
                            .Ascending(x => x.StudentId)
                            .Descending(x => x.CreatedAt)),

                    new CreateIndexModel<AgentPipelineTrace>(
                        Builders<AgentPipelineTrace>.IndexKeys
                            .Ascending(x => x.AgentRoute)
                            .Descending(x => x.CreatedAt)),

                    new CreateIndexModel<AgentPipelineTrace>(
                        Builders<AgentPipelineTrace>.IndexKeys
                            .Ascending(x => x.AgentStatus)
                            .Descending(x => x.CreatedAt))
                };

                _col.Indexes.CreateMany(indexes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARNING] Failed to create indexes for AgentPipelineTraceRepository: {ex.Message}");
            }
        }

        public async Task CreateAsync(AgentPipelineTrace trace)
        {
            await _col.InsertOneAsync(trace);
        }

        public async Task<AgentPipelineTrace?> GetByTraceIdAsync(string traceId)
        {
            if (string.IsNullOrWhiteSpace(traceId)) return null;

            var filter = Builders<AgentPipelineTrace>.Filter.Or(
                Builders<AgentPipelineTrace>.Filter.Eq(x => x.TraceId, traceId),
                Builders<AgentPipelineTrace>.Filter.Eq(x => x.AgentTraceId, traceId)
            );

            return await _col.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<List<AgentPipelineTrace>> GetRecentByConversationAsync(string conversationId, int limit = 30)
        {
            if (string.IsNullOrWhiteSpace(conversationId)) return new List<AgentPipelineTrace>();

            return await _col.Find(x => x.ConversationId == conversationId)
                .SortByDescending(x => x.CreatedAt)
                .Limit(limit)
                .ToListAsync();
        }

        public async Task<List<AgentPipelineTrace>> GetRecentByStudentAsync(string studentId, int limit = 50)
        {
            if (string.IsNullOrWhiteSpace(studentId)) return new List<AgentPipelineTrace>();

            return await _col.Find(x => x.StudentId == studentId)
                .SortByDescending(x => x.CreatedAt)
                .Limit(limit)
                .ToListAsync();
        }
    }
}