using MongoDB.Driver;
using Nupal.Domain.Entities;
using NUPAL.Core.Application.Interfaces;

namespace Nupal.Core.Infrastructure.Repositories
{
    public class StudentRepository : IStudentRepository
    {
        private readonly IMongoCollection<Student> _col;

        static StudentRepository()
        {
            if (!MongoDB.Bson.Serialization.BsonClassMap.IsClassMapRegistered(typeof(Student)))
            {
                MongoDB.Bson.Serialization.BsonClassMap.RegisterClassMap<Student>(cm =>
                {
                    cm.AutoMap();
                    cm.SetIgnoreExtraElements(true);
                });
            }
        }

        public StudentRepository(IMongoDatabase db)
        {
            _col = db.GetCollection<Student>("students");
            try
            {
                var idxs = new[]
                {
                    new CreateIndexModel<Student>(Builders<Student>.IndexKeys.Ascending("Account.Email"), new CreateIndexOptions { Unique = true }),
                    new CreateIndexModel<Student>(Builders<Student>.IndexKeys.Ascending("Account.Id"), new CreateIndexOptions { Unique = true })
                };
                _col.Indexes.CreateMany(idxs);
            }
            catch (Exception ex)
            {
                // Log and continue - index creation might fail due to transient connectivity/DNS issues
                Console.WriteLine($"[WARNING] Failed to create indexes for StudentRepository: {ex.Message}");
            }
        }

        public async Task UpsertAsync(Student s)
        {
            var filter = Builders<Student>.Filter.Or(
                Builders<Student>.Filter.Eq(x => x.Account.Id, s.Account.Id),
                Builders<Student>.Filter.Eq(x => x.Account.Email, s.Account.Email)
            );
            var update = Builders<Student>.Update
                .Set(x => x.Account.Email, s.Account.Email)
                .Set(x => x.Account.Name, s.Account.Name)
                .Set(x => x.Account.PasswordHash, s.Account.PasswordHash)
                .Set(x => x.Account.Role, s.Account.Role)
                .Set(x => x.Education.TotalCredits, s.Education.TotalCredits)
                .Set(x => x.Education.NumSemesters, s.Education.NumSemesters)
                .Set(x => x.Education.Semesters, s.Education.Semesters)
                .Set(x => x.LatestRecommendationId, s.LatestRecommendationId)
                .SetOnInsert(x => x.Account.Id, s.Account.Id);

            await _col.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });
        }

        public async Task<Student> FindByEmailAsync(string email)
        {
            return await _col.Find(x => x.Account.Email == email).FirstOrDefaultAsync();
        }

        public async Task<Student> GetByIdAsync(string id)
        {
            // Note: Assuming 'id' here corresponds to Account.Id (string) and not the BsonId
            return await _col.Find(x => x.Account.Id == id).FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<Student>> GetAllAsync()
        {
            return await _col.Find(_ => true).ToListAsync();
        }
    }
}
