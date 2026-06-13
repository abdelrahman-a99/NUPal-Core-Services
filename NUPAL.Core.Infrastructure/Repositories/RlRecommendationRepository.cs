using MongoDB.Driver;
using MongoDB.Bson;
using Nupal.Domain.Entities;
using NUPAL.Core.Application.Interfaces;
using NUPAL.Core.Application.Utilities;

namespace Nupal.Core.Infrastructure.Repositories
{
    public class RlRecommendationRepository : IRlRecommendationRepository
    {
        private readonly IMongoCollection<RlRecommendation> _col;

        public RlRecommendationRepository(IMongoDatabase db)
        {
            _col = db.GetCollection<RlRecommendation>("rl_recommendations");
            
            try
            {
                // Index on StudentId + recommendation variant
                var indexKeys = Builders<RlRecommendation>.IndexKeys
                    .Ascending(x => x.StudentId)
                    .Ascending(x => x.TargetTrack)
                    .Ascending(x => x.ObjectiveProfile)
                    .Descending(x => x.CreatedAt);
                _col.Indexes.CreateOne(new CreateIndexModel<RlRecommendation>(indexKeys));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARNING] Failed to create indexes for RlRecommendationRepository: {ex.Message}");
            }
        }

        public async Task CreateAsync(RlRecommendation recommendation)
        {
            await _col.InsertOneAsync(recommendation);
        }

        public async Task<RlRecommendation?> GetByIdAsync(string id)
        {
            return await _col.Find(x => x.Id == ObjectId.Parse(id)).FirstOrDefaultAsync();
        }

        public async Task<RlRecommendation?> GetLatestByStudentIdAsync(string studentId, string? targetTrack = null, string? objectiveProfile = null)
        {
            var filter = Builders<RlRecommendation>.Filter.Eq(x => x.StudentId, studentId);

            if (!string.IsNullOrWhiteSpace(targetTrack))
            {
                filter &= Builders<RlRecommendation>.Filter.Eq(x => x.TargetTrack, targetTrack.Trim().ToLowerInvariant());
            }

            if (!string.IsNullOrWhiteSpace(objectiveProfile))
            {
                filter &= Builders<RlRecommendation>.Filter.Eq(x => x.ObjectiveProfile, objectiveProfile.Trim().ToLowerInvariant());
            }

            return await _col.Find(filter)
                .SortByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<RlRecommendation?> GetByStudentAndEducationHashAsync(string studentId, string educationHash)
        {
            return await _col.Find(x => x.StudentId == studentId && x.EducationHash == educationHash)
                .SortByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<RlRecommendation?> GetValidRecommendationForStudentAsync(string studentId, string currentHash, Education education)
        {
            var byCurrentHash = await GetByStudentAndEducationHashAsync(studentId, currentHash);
            if (byCurrentHash != null)
                return byCurrentHash;

            var legacyHash = EducationHashHelper.ComputeLegacyHash(education);
            if (legacyHash != currentHash)
            {
                var byLegacyHash = await GetByStudentAndEducationHashAsync(studentId, legacyHash);
                if (byLegacyHash != null)
                    return byLegacyHash;
            }

            var latest = await GetLatestByStudentIdAsync(studentId);
            if (latest != null &&
                EducationHashHelper.HashMatchesStored(currentHash, latest.EducationHash, education))
            {
                return latest;
            }

            return null;
        }
    }
}
