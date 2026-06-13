using Nupal.Domain.Entities;

namespace NUPAL.Core.Application.Interfaces
{
    public interface IPrecomputeService
    {
        Task<string> TriggerPrecomputeAsync(string studentId, bool isSimulation = false, int? episodes = null, string? targetTrack = null, bool force = false);
        Task<object> GetJobStatusAsync();
        Task<RlRecommendation?> GetRecommendationAsync(string id);
        Task<SyncResult> SyncAllStudentsAsync(bool isSimulation = false);
        Task ProcessQueuedJobAsync(string jobId, CancellationToken cancellationToken = default);
    }

    public class SyncResult
    {
        public int TotalStudents { get; set; }
        public int TriggeredJobs { get; set; }
        public List<string> TriggeredStudentIds { get; set; } = new();
    }
}
