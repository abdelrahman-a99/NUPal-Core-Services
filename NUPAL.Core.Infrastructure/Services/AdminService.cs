using Nupal.Domain.Entities;
using NUPAL.Core.Application.DTOs;
using NUPAL.Core.Application.Interfaces;
using NUPAL.Core.Infrastructure.Services.Scheduling;

namespace Nupal.Core.Infrastructure.Services
{
    public class AdminService : IAdminService
    {
        private readonly IStudentRepository _studentRepo;
        private readonly IRlJobRepository _rlJobRepo;
        private readonly IRlRecommendationRepository _rlRecRepo;
        private readonly IPrecomputeService _precomputeService;
        private readonly ICourseMappingRepository _mappingRepo;
        private readonly IBlockRepository _blockRepo;
        private readonly IAiService _aiService;
        private readonly ISchedulingService _schedulingService;
        private readonly ISystemSettingsRepository _settingsRepo;
        private readonly IRegistrationRepository _registrationRepo;

        public AdminService(
            IStudentRepository studentRepo,
            IRlJobRepository rlJobRepo,
            IRlRecommendationRepository rlRecRepo,
            IPrecomputeService precomputeService,
            ICourseMappingRepository mappingRepo,
            IBlockRepository blockRepo,
            IAiService aiService,
            ISchedulingService schedulingService,
            ISystemSettingsRepository settingsRepo,
            IRegistrationRepository registrationRepo)
        {
            _studentRepo = studentRepo;
            _rlJobRepo = rlJobRepo;
            _rlRecRepo = rlRecRepo;
            _precomputeService = precomputeService;
            _mappingRepo = mappingRepo;
            _blockRepo = blockRepo;
            _aiService = aiService;
            _schedulingService = schedulingService;
            _settingsRepo = settingsRepo;
            _registrationRepo = registrationRepo;
        }


        public async Task<List<AdminStudentSummaryDto>> GetAllStudentsAsync(AdminStudentFilterDto filter)
        {
            var students = await _studentRepo.GetAllAsync();

            var query = students
                .Where(s => string.IsNullOrWhiteSpace(s.Account.Role) || s.Account.Role.ToLower() != "admin")
                .Select(MapToSummary)
                .AsEnumerable();

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var q = filter.Search.ToLower();
                query = query.Where(s =>
                    s.Name.ToLower().Contains(q) ||
                    s.Email.ToLower().Contains(q));
            }

            if (filter.MinGpa.HasValue)
                query = query.Where(s => s.CumulativeGpa >= filter.MinGpa.Value);

            if (filter.MaxGpa.HasValue)
                query = query.Where(s => s.CumulativeGpa <= filter.MaxGpa.Value);

            return query.ToList();
        }

        public async Task<AdminStudentDetailDto?> GetStudentByIdAsync(string id)
        {
            var student = await _studentRepo.GetByIdAsync(id);
            return student == null ? null : MapToDetail(student);
        }

        // ── RL Engine ─────────────────────────────────────────────────────────

        public async Task<List<AdminRlJobDto>> GetAllRlJobsAsync()
        {
            var jobs = await _rlJobRepo.GetActiveJobsAsync();
            return jobs.Select(MapToRlJobDto).ToList();
        }

        public async Task<AdminRlRecommendationDto?> GetStudentRecommendationAsync(string studentId)
        {
            var rec = await _rlRecRepo.GetLatestByStudentIdAsync(studentId);
            return rec == null ? null : MapToRlRecommendationDto(rec);
        }

        public async Task<string> TriggerRlJobAsync(string studentId, bool isSimulation)
        {
            return await _precomputeService.TriggerPrecomputeAsync(studentId, isSimulation);
        }

        public async Task<SyncResult> SyncAllStudentsAsync(bool isSimulation)
        {
            return await _precomputeService.SyncAllStudentsAsync(isSimulation);
        }

        public async Task DeleteRlJobAsync(string jobId)
        {
            await _rlJobRepo.DeleteAsync(jobId);
        }

        // ── Course Mappings ───────────────────────────────────────────────────

        public async Task<List<CourseMapping>> GetAllCourseMappingsAsync()
        {
            var mappings = await _mappingRepo.GetAllAsync();
            foreach (var mapping in mappings)
            {
                if (mapping.CourseNames == null || mapping.CourseNames.Count == 0)
                {
                    mapping.CourseNames = mapping.GetAllNames().ToList();
                }
            }
            return mappings;
        }

        public async Task CreateCourseMappingAsync(CourseMappingUpsertDto dto)
        {
            var mapping = new CourseMapping
            {
                CourseCode = dto.CourseCode,
                CourseNames = dto.CourseNames,
                Credits = dto.Credits,
                Category = dto.Category
            };
            await _mappingRepo.AddAsync(mapping);
        }

        public async Task UpdateCourseMappingAsync(string id, CourseMappingUpsertDto dto)
        {
            var mapping = new CourseMapping
            {
                Id = id,
                CourseCode = dto.CourseCode,
                CourseNames = dto.CourseNames,
                Credits = dto.Credits,
                Category = dto.Category
            };
            await _mappingRepo.UpdateAsync(mapping);
        }

        public async Task DeleteAllCourseMappingsAsync()
        {
            await _mappingRepo.DeleteAllAsync();
        }

        // ── System Stats ──────────────────────────────────────────────────────

        public async Task<AdminSystemStatsDto> GetSystemStatsAsync()
        {
            var students = (await _studentRepo.GetAllAsync())
                .Where(s => string.IsNullOrWhiteSpace(s.Account.Role) || s.Account.Role.ToLower() != "admin")
                .ToList();
            var jobs = (await _rlJobRepo.GetActiveJobsAsync()).ToList();
            var mappings = await _mappingRepo.GetAllAsync();
            var blocks = await _blockRepo.GetAllAsync();

            var avgGpa = students.Count > 0
                ? students.Average(s => s.Education?.Semesters?.LastOrDefault()?.CumulativeGpa ?? 0)
                : 0;

            var studentsWithSchedules = students.Count(s => !string.IsNullOrEmpty(s.LatestRecommendationId));

            var levelDistribution = students
                .Select(s => s.Education?.TotalCredits ?? 0)
                .GroupBy(credits => credits switch
                {
                    >= 106 => "Senior (106-135)",
                    >= 71 => "Junior (71-105)",
                    >= 36 => "Sophomore (36-70)",
                    _ => "Freshman (0-35)"
                })
                .ToDictionary(g => g.Key, g => g.Count());

            var jobStatusCounts = jobs
                .GroupBy(j => j.Status.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            var categoryDistribution = mappings
                .GroupBy(m => string.IsNullOrEmpty(m.Category) ? "Uncategorized" : m.Category)
                .ToDictionary(g => g.Key, g => g.Count());

            var availableSemesters = blocks
                .Select(b => string.IsNullOrEmpty(b.Semester) ? "Fall 2025" : b.Semester)
                .Distinct()
                .OrderBy(s => s)
                .ToList();

            var settings = await _settingsRepo.GetSettingsAsync();

            return new AdminSystemStatsDto
            {
                Students = new AdminStudentStatsDto
                {
                    Total = students.Count,
                    AverageGpa = Math.Round(avgGpa, 2),
                    StudentsWithSchedules = studentsWithSchedules,
                    LevelDistribution = levelDistribution
                },
                RlJobs = new AdminRlStatsDto
                {
                    Total = jobs.Count,
                    ByStatus = jobStatusCounts
                },
                CourseMappings = new AdminCountDto 
                { 
                    Total = mappings.Count,
                    CategoryDistribution = categoryDistribution
                },
                SchedulingBlocks = new AdminCountDto { Total = blocks.Count },
                ActiveSemester = settings.ActiveSemester,
                AvailableSemesters = availableSemesters
            };
        }

        public async Task UpdateActiveSemesterAsync(string semester)
        {
            var settings = await _settingsRepo.GetSettingsAsync();
            settings.ActiveSemester = semester;
            await _settingsRepo.UpdateSettingsAsync(settings);
            await _schedulingService.InvalidateCacheAsync(); // Clear cache since active semester changed
        }

        // ── Scheduling Blocks ─────────────────────────────────────────────────

        public async Task<List<BlockDto>> GetAllBlocksAsync(string? level = null, string? semester = null)
        {
            var entities = await _blockRepo.GetAllAsync(level, semester);
            var mappings = await _mappingRepo.GetAllAsync();
            
            return entities.Select(e => 
                SchedulingBlockMapper.RawToFrontend(SchedulingBlockMapper.ToRawDto(e), mappings)
            ).ToList();
        }

        public async Task CreateBlockAsync(BlockDto dto)
        {
            // Map DTO to Entity
            var block = new SchedulingBlock
            {
                BlockId = dto.BlockId,
                Semester = dto.Semester,
                Major = dto.Major,
                Level = dto.BlockId.Contains("-") ? dto.BlockId.Split('-')[1].Substring(0, 2) : "ALL",
                Courses = dto.Courses.Select(c => new SchedulingBlockCourse
                {
                    CourseName = c.CourseName,
                    Instructor = c.Instructor,
                    Day = c.Day,
                    StartTime = c.Start,
                    EndTime = c.End,
                    Section = c.Section,
                    Room = c.Room,
                    Type = c.Subtype?.StartsWith("L") == true ? "L" : "T"
                }).ToList()
            };

            await _blockRepo.CreateAsync(block);
            await _schedulingService.InvalidateCacheAsync();
        }

        public async Task UpdateBlockAsync(string blockId, string? semester, BlockDto dto)
        {
            // If semester is provided, we use it to find the specific block to replace.
            // If the user changed the semester in the UI, 'dto.Semester' will be the NEW one,
            // while 'semester' (from query param) will be the OLD one.
            
            var existing = await _blockRepo.GetByBlockIdAsync(blockId, semester);
            if (existing == null)
            {
                // Fallback to create if update target not found or just use the new one
                await CreateBlockAsync(dto);
                return;
            }

            var block = new SchedulingBlock
            {
                Id = existing.Id, // Keep the same Mongo ID
                BlockId = blockId,
                Semester = dto.Semester,
                Major = dto.Major,
                Level = blockId.Contains("-") ? blockId.Split('-')[1].Substring(0, 2) : "ALL",
                Courses = dto.Courses.Select(c => new SchedulingBlockCourse
                {
                    CourseName = c.CourseName,
                    Instructor = c.Instructor,
                    Day = c.Day,
                    StartTime = c.Start,
                    EndTime = c.End,
                    Section = c.Section,
                    Room = c.Room,
                    Type = c.Subtype?.StartsWith("L") == true ? "L" : "T"
                }).ToList()
            };

            await _blockRepo.UpdateAsync(block);
            await _schedulingService.InvalidateCacheAsync();
        }

        public async Task DeleteBlockAsync(string blockId, string? semester)
        {
            await _blockRepo.DeleteByBlockIdAsync(blockId, semester);
            await _schedulingService.InvalidateCacheAsync();
        }

        public async Task<BlockDto> ParseSchedulePdfAsync(Stream pdfStream)
        {
            // Reset stream position to ensure full file is sent
            if (pdfStream.CanSeek) pdfStream.Position = 0;

            // Call AI Service to parse structured data from PDF (Python handles extraction)
            var parsedBlock = await _aiService.ParseSchedulePdfAsync(pdfStream);
            
            // 3. Normalize day names if needed (UI already does some, but better here too)
            if (parsedBlock.Courses != null)
            {
                foreach (var c in parsedBlock.Courses)
                {
                    c.Day = NormalizeDay(c.Day);
                }
            }

            return parsedBlock;
        }

        public async Task<List<Registration>> GetAllRegistrationsAsync()
        {
            return await _registrationRepo.GetAllAsync();
        }

        public async Task ApproveRegistrationAsync(string registrationId, ApproveRegistrationDto dto)
        {
            var reg = await _registrationRepo.GetByIdAsync(registrationId);
            if (reg == null) return;

            reg.Status = dto.Status;
            reg.AdminNote = dto.AdminNote;
            reg.ProcessedAt = DateTime.UtcNow;

            await _registrationRepo.UpdateAsync(reg);
        }

        private static string NormalizeDay(string day)
        {
            if (string.IsNullOrEmpty(day)) return "Monday";
            var d = day.Trim().ToLower();
            if (d.StartsWith("sun")) return "Sunday";
            if (d.StartsWith("mon")) return "Monday";
            if (d.StartsWith("tue")) return "Tuesday";
            if (d.StartsWith("wed")) return "Wednesday";
            if (d.StartsWith("thu")) return "Thursday";
            if (d.StartsWith("fri")) return "Friday";
            if (d.StartsWith("sat")) return "Saturday";
            return day;
        }

        // ── Private Mappers ───────────────────────────────────────────────────

        private static AdminStudentSummaryDto MapToSummary(Student s) => new()
        {
            Id = s.Account.Id,
            Name = s.Account.Name,
            Email = s.Account.Email,
            TotalCredits = s.Education?.TotalCredits ?? 0,
            NumSemesters = s.Education?.NumSemesters ?? 0,
            TotalCourses = s.Education?.Semesters?.Sum(sem => sem.Courses?.Count ?? 0) ?? 0,
            CumulativeGpa = s.Education?.Semesters?.LastOrDefault()?.CumulativeGpa ?? 0,
            LatestSemesterGpa = s.Education?.Semesters?.LastOrDefault()?.SemesterGpa ?? 0,
            LatestTerm = s.Education?.Semesters?.LastOrDefault()?.Term ?? "N/A",
            LatestRecommendationId = s.LatestRecommendationId
        };

        private static AdminStudentDetailDto MapToDetail(Student s) => new()
        {
            Id = s.Account.Id,
            Name = s.Account.Name,
            Email = s.Account.Email,
            LatestRecommendationId = s.LatestRecommendationId,
            Education = new AdminEducationDto
            {
                TotalCredits = s.Education.TotalCredits,
                NumSemesters = s.Education.NumSemesters,
                Semesters = s.Education.Semesters.Select(sem => new AdminSemesterDto
                {
                    Term = sem.Term,
                    Optional = sem.Optional,
                    SemesterCredits = sem.SemesterCredits,
                    SemesterGpa = sem.SemesterGpa,
                    CumulativeGpa = sem.CumulativeGpa,
                    Courses = sem.Courses.Select(c => new AdminCourseDto
                    {
                        CourseId = c.CourseId,
                        CourseName = c.CourseName,
                        Credit = c.Credit,
                        Grade = c.Grade,
                        Gpa = c.Gpa
                    }).ToList()
                }).ToList()
            }
        };

        private static AdminRlJobDto MapToRlJobDto(RlJob j) => new()
        {
            Id = j.Id.ToString(),
            StudentId = j.StudentId,
            Status = j.Status.ToString(),
            CreatedAt = j.CreatedAt,
            StartedAt = j.StartedAt,
            FinishedAt = j.FinishedAt,
            IsSimulation = j.IsSimulation,
            ResultRecommendationId = j.ResultRecommendationId,
            Error = j.Error,
            EducationHash = j.EducationHash
        };

        private static AdminRlRecommendationDto MapToRlRecommendationDto(RlRecommendation rec) => new()
        {
            Id = rec.Id.ToString(),
            StudentId = rec.StudentId,
            CreatedAt = rec.CreatedAt,
            TermIndex = rec.TermIndex,
            Courses = rec.Courses,
            SlatesByTerm = rec.SlatesByTerm,
            Metrics = rec.Metrics,
            ModelVersion = rec.ModelVersion,
            PolicyVersion = rec.PolicyVersion,
            DefaultProfile = rec.DefaultProfile,
            Profiles = rec.Profiles
        };
    }
}
