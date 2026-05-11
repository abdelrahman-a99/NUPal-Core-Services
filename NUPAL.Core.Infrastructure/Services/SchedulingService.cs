using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nupal.Domain.Entities;
using NUPAL.Core.Application.DTOs;
using NUPAL.Core.Application.Interfaces;
using NUPAL.Core.Infrastructure.Services.Scheduling;

namespace NUPAL.Core.Infrastructure.Services
{

    public class SchedulingService : ISchedulingService
    {
        private readonly IBlockRepository _repo;
        private readonly ILogger<SchedulingService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        private readonly SemaphoreSlim _cacheLock = new(1, 1);
        private Dictionary<string, List<SchedulingBlock>> _cache = new(StringComparer.OrdinalIgnoreCase);
        private List<CourseMapping> _mappingsCache = [];
        private bool _cacheLoaded;

        public SchedulingService(IBlockRepository repo, ILogger<SchedulingService> logger, IServiceScopeFactory scopeFactory)
        {
            _repo = repo;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }
        public async Task<IEnumerable<RawBlockDto>> GetBlocksAsync(string? level = null)
        {
            var rawBlocks = await GetCachedAsync(level);
            
            using var scope = _scopeFactory.CreateScope();
            var settingsRepo = scope.ServiceProvider.GetRequiredService<ISystemSettingsRepository>();
            var settings = await settingsRepo.GetSettingsAsync();
            var activeSemester = settings.ActiveSemester;

            return rawBlocks
                .Where(b => string.IsNullOrEmpty(b.Semester) || b.Semester == activeSemester)
                .ToList();
        }

        public async Task<IEnumerable<BlockDto>> GetMappedBlocksAsync(string? level = null)
        {
            var rawBlocks = await GetBlocksAsync(level);
            return rawBlocks.Select(b => SchedulingBlockMapper.RawToFrontend(b, _mappingsCache));
        }
        public async Task<RawBlockDto?> GetBlockAsync(string blockId)
        {
            await EnsureCacheAsync();
            var entity = _cache.Values
                .SelectMany(list => list)
                .FirstOrDefault(b => b.BlockId.Equals(blockId.Trim(), StringComparison.OrdinalIgnoreCase));

            return entity is null ? null : SchedulingBlockMapper.ToRawDto(entity);
        }

        public async Task<BlockDto?> GetMappedBlockAsync(string blockId)
        {
            var raw = await GetBlockAsync(blockId);
            if (raw == null) return null;
            await EnsureCacheAsync(); // Ensure mappings are loaded
            return SchedulingBlockMapper.RawToFrontend(raw, _mappingsCache);
        }

        public async Task<IEnumerable<string>> GetCourseNamesAsync(string? level = null)
        {
            var blocks = await GetBlocksAsync(level);
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "" };

            return blocks
                .SelectMany(b => b.Courses)
                .Select(c => (c.CourseName ?? "").Trim())
                .Where(n => !excluded.Contains(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n);
        }

        public async Task<IEnumerable<string>> GetInstructorsAsync(string? level = null)
        {
            var blocks = await GetBlocksAsync(level);
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "", "unknown", "tba", "tbd", "n/a" };

            return blocks
                .SelectMany(b => b.Courses)
                .Select(c => (c.Instructor ?? "").Trim())
                .Where(i => !string.IsNullOrEmpty(i) && !excluded.Contains(i))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(i => i);
        }

        public async Task<IEnumerable<RecommendationResultDto>> RecommendAsync(RecommendationRequestDto request)
        {
            var prefs = request.Preferences;
            var desired = request.DesiredCourseNames ?? [];
            int topN = request.TopN > 0 ? request.TopN : 5;

            using var scope = _scopeFactory.CreateScope();
            var settingsRepo = scope.ServiceProvider.GetRequiredService<ISystemSettingsRepository>();
            var settings = await settingsRepo.GetSettingsAsync();
            var activeSemester = settings.ActiveSemester;

            var levelBlocks = (await GetBlocksAsync(prefs.Level))
                .Where(b => string.IsNullOrEmpty(b.Semester) || b.Semester == activeSemester)
                .ToList();

            if (levelBlocks.Count == 0)
            {
                _logger.LogWarning("No blocks found for level '{Level}' in semester '{Semester}'", prefs.Level, activeSemester);
                return [];
            }

            await EnsureCacheAsync(); // Ensure mappings cache is loaded

            var normalizationService = scope.ServiceProvider.GetRequiredService<ICourseNormalizationService>();

            var normalizedDesired = await normalizationService.NormalizeToCodesAsync(desired);
            var distinctDesiredCodes = normalizedDesired
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var originalDesiredCount = distinctDesiredCodes.Count;
            var expandedDesired = new List<string>(distinctDesiredCodes);
            foreach (var code in distinctDesiredCodes)
            {
                var mapping = _mappingsCache.FirstOrDefault(m => (m.CourseCode ?? "").Equals(code, StringComparison.OrdinalIgnoreCase));
                if (mapping != null)
                {
                    expandedDesired.AddRange(mapping.GetAllNames());
                }
            }
            var distinctExpandedDesired = expandedDesired.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var allFeatures = levelBlocks.Select(b => SchedulingBlockMapper.ExtractFeatures(b, _mappingsCache)).ToList();
            var vocab = SchedulingRecommender.BuildVocab(allFeatures);
            var (pv, wv) = SchedulingRecommender.VectorisePrefs(prefs, distinctExpandedDesired, vocab, request.MatchCoursesOnly);
            var scored = new List<Scheduling.Models.ScoredBlock>();

            for (int i = 0; i < levelBlocks.Count; i++)
            {
                var f = allFeatures[i];
                if (!SchedulingRecommender.PassesHardConstraints(f, prefs, request.MatchCoursesOnly)) continue;

                scored.Add(SchedulingRecommender.ScoreBlock(
                    levelBlocks[i], f, distinctExpandedDesired, pv, wv, vocab, prefs, request.MatchCoursesOnly, originalDesiredCount));
            }

            scored = [.. scored.OrderByDescending(s => s.FinalScore)];
            var vecMap = new Dictionary<string, double[]>(levelBlocks.Count);
            for (int i = 0; i < levelBlocks.Count; i++)
                vecMap[allFeatures[i].BlockId] = SchedulingRecommender.VectoriseBlock(allFeatures[i], vocab);
            var top = request.MatchCoursesOnly
                ? scored.Take(topN).ToList()
                : SchedulingRecommender.DiversityFilter(scored, vecMap, topN);
            return top.Select(s => SchedulingRecommender.BuildResultDto(s, prefs, _mappingsCache));
        }

        public async Task<int> SeedBlocksAsync(IEnumerable<RawBlockDto> blocks)
        {
            var entities = blocks.Select(dto => new SchedulingBlock
            {
                BlockId  = dto.BlockId,
                Semester = dto.Semester,
                Major    = dto.Major,
                Level    = dto.Level,
                Courses  = dto.Courses.Select(c => new SchedulingBlockCourse
                {
                    CourseName = c.CourseName,
                    Section    = c.Section,
                    Type       = c.Type,
                    Instructor = c.Instructor,
                    Day        = c.Day,
                    StartTime  = c.StartTime,
                    EndTime    = c.EndTime,
                    Room       = c.Room,
                }).ToList(),
            }).ToList();

            int count = await _repo.UpsertManyAsync(entities);

            await InvalidateCacheAsync();

            _logger.LogInformation("Seeded {Count} scheduling blocks into MongoDB", count);
            return count;
        }

        public async Task<string> GetActiveSemesterAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var settingsRepo = scope.ServiceProvider.GetRequiredService<ISystemSettingsRepository>();
            var settings = await settingsRepo.GetSettingsAsync();
            return settings.ActiveSemester;
        }

        public async Task<CategorizedInstructorsDto> GetCategorizedInstructorsAsync(IEnumerable<string> courseNames, string? level = null)
        {
            var blocks = await GetBlocksAsync(level);
            var courses = new HashSet<string>(courseNames, StringComparer.OrdinalIgnoreCase);
            
            var doctors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var b in blocks)
            {
                var frontendBlock = SchedulingBlockMapper.RawToFrontend(b, _mappingsCache);
                foreach (var c in frontendBlock.Courses)
                {
                    if (courses.Contains(c.CourseName) || courses.Contains(c.CourseId))
                    {
                        if (!string.IsNullOrWhiteSpace(c.Instructor) && !c.Instructor.Equals("TBA", StringComparison.OrdinalIgnoreCase))
                        {
                            if (c.InstructorType == "Doctor") doctors.Add(c.Instructor);
                            else if (c.InstructorType == "TA") tas.Add(c.Instructor);
                        }
                    }
                }
            }

            return new CategorizedInstructorsDto
            {
                Doctors = doctors.OrderBy(x => x).ToList(),
                TAs = tas.OrderBy(x => x).ToList()
            };
        }


        private async Task EnsureCacheAsync()
        {
            if (_cacheLoaded) return;

            await _cacheLock.WaitAsync();
            try
            {
                if (_cacheLoaded) return;

                using var scope = _scopeFactory.CreateScope();
                var mappingRepo = scope.ServiceProvider.GetRequiredService<ICourseMappingRepository>();

                var all = await _repo.GetAllAsync();
                _mappingsCache = await mappingRepo.GetAllAsync();
                _cache = all
                    .GroupBy(b => b.Level, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);


                _cache[""] = all;
                _cacheLoaded = true;

                _logger.LogInformation(
                    "Loaded {Count} scheduling blocks from MongoDB into cache", all.Count);
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        private async Task<List<RawBlockDto>> GetCachedAsync(string? level = null)
        {
            await EnsureCacheAsync();

            if (string.IsNullOrWhiteSpace(level) || level.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                return _cache.TryGetValue("", out var all)
                    ? all.Select(SchedulingBlockMapper.ToRawDto).ToList()
                    : [];

            return _cache.TryGetValue(level.Trim(), out var filtered)
                ? filtered.Select(SchedulingBlockMapper.ToRawDto).ToList()
                : [];
        }

        public async Task InvalidateCacheAsync()
        {
            await _cacheLock.WaitAsync();
            try { _cache.Clear(); _cacheLoaded = false; }
            finally { _cacheLock.Release(); }
        }

        public async Task RegisterScheduleAsync(RegistrationRequestDto request)
        {
            using var scope = _scopeFactory.CreateScope();
            var regRepo = scope.ServiceProvider.GetRequiredService<IRegistrationRepository>();

            // Check if student already has a pending or approved registration
            // For simplicity, we check all registrations. In a real app, we'd filter by semester.
            var existing = await regRepo.GetAllAsync();
            var alreadyRegistered = existing.Any(r => 
                r.StudentId == request.StudentId && 
                (r.Status == "Pending" || r.Status == "Approved"));

            if (alreadyRegistered)
            {
                throw new InvalidOperationException("You already have a pending or approved registration.");
            }

            var registration = new Registration
            {
                StudentId = request.StudentId,
                StudentName = request.StudentName,
                StudentEmail = request.StudentEmail,
                SelectedBlock = request.SelectedBlock,
                Status = "Pending",
                RegisteredAt = DateTime.UtcNow,
                IsFromRecommendation = request.IsFromRecommendation,
                IsFromRl = request.IsFromRl,
                IsModified = request.IsModified
            };

            await regRepo.CreateAsync(registration);
        }

        public async Task<Registration?> GetRegistrationByStudentIdAsync(string studentId)
        {
            using var scope = _scopeFactory.CreateScope();
            var regRepo = scope.ServiceProvider.GetRequiredService<IRegistrationRepository>();

            var all = await regRepo.GetAllAsync();

            // Only surface an active (Pending or Approved) registration.
            // Rejected registrations should not block the student from re-submitting,
            // and should not show stale schedule data on the student's schedule page.
            return all
                .Where(r => r.StudentId == studentId &&
                            (r.Status == "Pending" || r.Status == "Approved"))
                .OrderByDescending(r => r.RegisteredAt)
                .FirstOrDefault();
        }


        public async Task<Registration?> GetLatestRegistrationByStudentIdAsync(string studentId)
        {
            using var scope = _scopeFactory.CreateScope();
            var regRepo = scope.ServiceProvider.GetRequiredService<IRegistrationRepository>();

            var all = await regRepo.GetAllAsync();
            return all
                .Where(r => r.StudentId == studentId)
                .OrderByDescending(r => r.RegisteredAt)
                .FirstOrDefault();
        }
    }
}
