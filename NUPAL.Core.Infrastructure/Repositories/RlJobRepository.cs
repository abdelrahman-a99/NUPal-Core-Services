using MongoDB.Driver;
using MongoDB.Bson;
using Nupal.Domain.Entities;
using NUPAL.Core.Application.Interfaces;

namespace Nupal.Core.Infrastructure.Repositories
{
    public class RlJobRepository : IRlJobRepository
    {
        private readonly IMongoCollection<RlJob> _col;

        public RlJobRepository(IMongoDatabase db)
        {
            _col = db.GetCollection<RlJob>("rl_jobs");
            
            try
            {
                // Index on StudentId and CreatedAt for fast lookups
                var indexKeys = Builders<RlJob>.IndexKeys.Descending(x => x.CreatedAt).Ascending(x => x.StudentId);
                _col.Indexes.CreateOne(new CreateIndexModel<RlJob>(indexKeys));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARNING] Failed to create indexes for RlJobRepository: {ex.Message}");
            }
        }

        public async Task CreateAsync(RlJob job)
        {
            await _col.InsertOneAsync(job);
        }

        public async Task UpdateStatusAsync(string jobId, JobStatus status, string? error = null)
        {
            var filter = Builders<RlJob>.Filter.Eq(x => x.Id, ObjectId.Parse(jobId));
            
            var update = Builders<RlJob>.Update
                .Set(x => x.Status, status);

            if (status == JobStatus.Running)
                update = update.Set(x => x.StartedAt, DateTime.UtcNow);
            
            if (status == JobStatus.Ready || status == JobStatus.Failed)
                update = update.Set(x => x.FinishedAt, DateTime.UtcNow);

            if (error != null)
                update = update.Set(x => x.Error, error);

            await _col.UpdateOneAsync(filter, update);
        }

        public async Task UpdateResultAsync(string jobId, string recommendationId)
        {
            var filter = Builders<RlJob>.Filter.Eq(x => x.Id, ObjectId.Parse(jobId));
            var update = Builders<RlJob>.Update
                .Set(x => x.ResultRecommendationId, recommendationId)
                .Set(x => x.Status, JobStatus.Ready)
                .Set(x => x.FinishedAt, DateTime.UtcNow);

            await _col.UpdateOneAsync(filter, update);
        }

        public async Task<RlJob?> GetByIdAsync(string id)
        {
            return await _col.Find(x => x.Id == ObjectId.Parse(id)).FirstOrDefaultAsync();
        }

        public async Task<RlJob?> GetLatestByStudentIdAsync(string studentId)
        {
            return await _col.Find(x => x.StudentId == studentId)
                .SortByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<RlJob?> GetInProgressForStudentAsync(string studentId, bool isSimulation)
        {
            var filter = Builders<RlJob>.Filter.Eq(x => x.StudentId, studentId)
                & Builders<RlJob>.Filter.Eq(x => x.IsSimulation, isSimulation)
                & Builders<RlJob>.Filter.In(x => x.Status, new[] { JobStatus.Queued, JobStatus.Running });

            return await _col.Find(filter)
                .SortByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<RlJob?> TryClaimNextQueuedJobAsync()
        {
            var filter = Builders<RlJob>.Filter.Eq(x => x.Status, JobStatus.Queued);
            var update = Builders<RlJob>.Update
                .Set(x => x.Status, JobStatus.Running)
                .Set(x => x.StartedAt, DateTime.UtcNow);

            var options = new FindOneAndUpdateOptions<RlJob>
            {
                Sort = Builders<RlJob>.Sort.Ascending(x => x.CreatedAt),
                ReturnDocument = ReturnDocument.After
            };

            return await _col.FindOneAndUpdateAsync(filter, update, options);
        }

        public async Task<IEnumerable<RlJob>> GetActiveJobsAsync()
        {
            var activeFilter = Builders<RlJob>.Filter.In(
                x => x.Status,
                new[] { JobStatus.Queued, JobStatus.Running });

            var active = await _col.Find(activeFilter)
                .SortByDescending(x => x.CreatedAt)
                .ToListAsync();

            var recentFilter = Builders<RlJob>.Filter.In(
                x => x.Status,
                new[] { JobStatus.Ready, JobStatus.Failed });

            var recent = await _col.Find(recentFilter)
                .SortByDescending(x => x.CreatedAt)
                .Limit(20)
                .ToListAsync();

            return active
                .Concat(recent)
                .GroupBy(j => j.Id)
                .Select(g => g.First())
                .OrderByDescending(j => j.CreatedAt)
                .ToList();
        }

        public async Task DeleteAsync(string id)
        {
            await _col.DeleteOneAsync(x => x.Id == ObjectId.Parse(id));
        }
    }
}
