using NUPAL.Core.Application.DTOs;

namespace NUPAL.Core.Application.Interfaces
{
    public interface ISchedulingService
    {
       
        Task<IEnumerable<RawBlockDto>> GetBlocksAsync(string? level = null);
        Task<IEnumerable<BlockDto>> GetMappedBlocksAsync(string? level = null);

        Task<RawBlockDto?> GetBlockAsync(string blockId);
        Task<BlockDto?> GetMappedBlockAsync(string blockId);
        Task<IEnumerable<string>> GetCourseNamesAsync(string? level = null);

        Task<IEnumerable<string>> GetInstructorsAsync(string? level = null);
        Task<CategorizedInstructorsDto> GetCategorizedInstructorsAsync(IEnumerable<string> courseNames, string? level = null);
        Task<IEnumerable<RecommendationResultDto>> RecommendAsync(RecommendationRequestDto request);

        Task<int> SeedBlocksAsync(IEnumerable<RawBlockDto> blocks);
        Task<string> GetActiveSemesterAsync();
        Task InvalidateCacheAsync();
        Task RegisterScheduleAsync(RegistrationRequestDto request);
        Task<StudentScheduleDto> GetStudentScheduleAsync(string studentId);
        Task<Nupal.Domain.Entities.Registration?> GetRegistrationByStudentIdAsync(string studentId);
        Task<Nupal.Domain.Entities.Registration?> GetLatestRegistrationByStudentIdAsync(string studentId);
        Task InvalidateStudentScheduleCacheAsync(string studentId);
    }
}
