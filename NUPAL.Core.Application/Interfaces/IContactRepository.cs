using Nupal.Domain.Entities;

namespace NUPAL.Core.Application.Interfaces
{
    public interface IContactRepository
    {
        Task AddAsync(ContactMessage message);
        Task<IEnumerable<ContactMessage>> GetAllAsync();
    }
}
