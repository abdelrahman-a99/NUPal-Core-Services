using Nupal.Domain.Entities;

namespace NUPAL.Core.Application.Interfaces
{
    public interface IRegistrationRepository
    {
        Task<List<Registration>> GetAllAsync();
        Task<Registration?> GetByIdAsync(string id);
        Task<List<Registration>> GetByStudentIdAsync(string studentId);
        Task CreateAsync(Registration registration);
        Task UpdateAsync(Registration registration);
    }
}
