using Nupal.Domain.Entities;

namespace NUPAL.Core.Application.Interfaces
{
    public interface IRlJobRepository
    {
        Task CreateAsync(RlJob job);
        Task UpdateStatusAsync(string jobId, JobStatus status, string? error = null);
        Task UpdateResultAsync(string jobId, string recommendationId);
        Task<RlJob?> GetByIdAsync(string id);
        Task<RlJob?> GetLatestByStudentIdAsync(string studentId);
        Task<RlJob?> GetInProgressForStudentAsync(string studentId, bool isSimulation);
        Task<RlJob?> TryClaimNextQueuedJobAsync();
        Task<IEnumerable<RlJob>> GetActiveJobsAsync();
        Task DeleteAsync(string id);
    }
}
