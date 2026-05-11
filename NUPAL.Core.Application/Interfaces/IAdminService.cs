using NUPAL.Core.Application.DTOs;
using Nupal.Domain.Entities;

namespace NUPAL.Core.Application.Interfaces
{
    public interface IAdminService
    {
        // Students
        Task<List<AdminStudentSummaryDto>> GetAllStudentsAsync(AdminStudentFilterDto filter);
        Task<AdminStudentDetailDto?> GetStudentByIdAsync(string id);

        // RL Engine
        Task<List<AdminRlJobDto>> GetAllRlJobsAsync();
        Task<AdminRlRecommendationDto?> GetStudentRecommendationAsync(string studentId);
        Task<string> TriggerRlJobAsync(string studentId, bool isSimulation);
        Task<SyncResult> SyncAllStudentsAsync(bool isSimulation);
        Task DeleteRlJobAsync(string jobId);

        // Course Mappings
        Task<List<CourseMapping>> GetAllCourseMappingsAsync();
        Task CreateCourseMappingAsync(CourseMappingUpsertDto dto);
        Task UpdateCourseMappingAsync(string id, CourseMappingUpsertDto dto);
        Task DeleteAllCourseMappingsAsync();

        // System Stats
        Task<AdminSystemStatsDto> GetSystemStatsAsync();
        Task UpdateActiveSemesterAsync(string semester);

        // Scheduling Blocks
        Task<List<BlockDto>> GetAllBlocksAsync(string? level = null, string? semester = null);
        Task CreateBlockAsync(BlockDto dto);
        Task UpdateBlockAsync(string blockId, string? semester, BlockDto dto);
        Task DeleteBlockAsync(string blockId, string? semester);
        Task<BlockDto> ParseSchedulePdfAsync(Stream pdfStream);

        // Registrations
        Task<List<Registration>> GetAllRegistrationsAsync();
        Task ApproveRegistrationAsync(string registrationId, ApproveRegistrationDto dto);
    }
}
