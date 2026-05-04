using MongoDB.Bson;
using MongoDB.Driver;
using Nupal.Domain.Entities;
using NUPAL.Core.Application.Interfaces;

namespace Nupal.Core.Infrastructure.Repositories
{
    public class ChatMessageRepository : IChatMessageRepository
    {
        private readonly IMongoCollection<ChatMessage> _col;

        public ChatMessageRepository(IMongoDatabase db)
        {
            _col = db.GetCollection<ChatMessage>("chat_messages");
            try
            {
                var indexes = new[]
                {
                    new CreateIndexModel<ChatMessage>(
                        Builders<ChatMessage>.IndexKeys.Ascending(x => x.ConversationId).Descending(x => x.CreatedAt)),
                    new CreateIndexModel<ChatMessage>(
                        Builders<ChatMessage>.IndexKeys.Ascending(x => x.AgentTraceId)),
                    new CreateIndexModel<ChatMessage>(
                        Builders<ChatMessage>.IndexKeys.Ascending(x => x.AgentRoute).Descending(x => x.CreatedAt))
                };

                _col.Indexes.CreateMany(indexes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARNING] Failed to create indexes for ChatMessageRepository: {ex.Message}");
            }
        }

        public async Task CreateAsync(ChatMessage message)
        {
            await _col.InsertOneAsync(message);
        }

        public async Task DeleteByConversationIdAsync(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId)) return;
            await _col.DeleteManyAsync(x => x.ConversationId == conversationId);
        }

        public async Task<List<ChatMessage>> GetRecentByConversationAsync(string conversationId, int limit = 30)
        {
            if (string.IsNullOrWhiteSpace(conversationId)) return new List<ChatMessage>();
            // Stored as string, but represents an ObjectId. Keep as exact match.
            return await _col.Find(x => x.ConversationId == conversationId)
                .SortByDescending(x => x.CreatedAt)
                .Limit(limit)
                .ToListAsync();
        }
    }
}
