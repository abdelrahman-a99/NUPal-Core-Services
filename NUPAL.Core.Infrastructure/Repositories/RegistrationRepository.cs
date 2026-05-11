using MongoDB.Driver;
using Nupal.Domain.Entities;
using NUPAL.Core.Application.Interfaces;
using MongoDB.Bson;

namespace Nupal.Core.Infrastructure.Repositories
{
    public class RegistrationRepository : IRegistrationRepository
    {
        private readonly IMongoCollection<Registration> _registrations;

        public RegistrationRepository(IMongoDatabase database)
        {
            _registrations = database.GetCollection<Registration>("registrations");
        }

        public async Task<List<Registration>> GetAllAsync()
        {
            return await _registrations.Find(_ => true).SortByDescending(r => r.RegisteredAt).ToListAsync();
        }

        public async Task<Registration?> GetByIdAsync(string id)
        {
            return await _registrations.Find(r => r.Id == id).FirstOrDefaultAsync();
        }

        public async Task<List<Registration>> GetByStudentIdAsync(string studentId)
        {
            return await _registrations.Find(r => r.StudentId == studentId).SortByDescending(r => r.RegisteredAt).ToListAsync();
        }

        public async Task CreateAsync(Registration registration)
        {
            await _registrations.InsertOneAsync(registration);
        }

        public async Task UpdateAsync(Registration registration)
        {
            await _registrations.ReplaceOneAsync(r => r.Id == registration.Id, registration);
        }
    }
}
