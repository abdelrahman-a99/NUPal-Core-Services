using NUPAL.Core.Application.DTOs;
using NUPAL.Core.Application.Interfaces;
using Nupal.Domain.Entities;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace Nupal.Core.Infrastructure.Services
{
    public class PrecomputeService : IPrecomputeService
    {
        private static readonly string[] SupportedTracks = new[] { "general", "big_data", "media" };
        private static readonly List<string> SupportedObjectiveProfiles = new()
        {
            "balanced",
            "fastest_graduation",
            "gpa_safe",
            "math_heavy",
            "programming_heavy"
        };
        private const string DefaultTargetTrack = "general";
        private const string DefaultObjectiveProfile = "balanced";
        private const string RecommendationVariantSchemaVersion = "track-aware-bundle-v1";

        private readonly IStudentRepository _studentRepo;
        private readonly IRlJobRepository _jobRepo;
        private readonly IRlRecommendationRepository _recRepo;
        private readonly IRlService _rlService;

        public PrecomputeService(
            IStudentRepository studentRepo,
            IRlJobRepository jobRepo,
            IRlRecommendationRepository recRepo,
            IRlService rlService)
        {
            _studentRepo = studentRepo;
            _jobRepo = jobRepo;
            _recRepo = recRepo;
            _rlService = rlService;
        }

        public async Task<string> TriggerPrecomputeAsync(string studentId, bool isSimulation = false, int? episodes = null, string? targetTrack = null, bool force = false)
        {
            var student = await _studentRepo.GetByIdAsync(studentId)
                          ?? await _studentRepo.FindByEmailAsync(studentId); // Support ID or Email

            if (student == null)
                throw new KeyNotFoundException($"Student {studentId} not found");

            // Compute Hash of Education to prevent redundant training if needed
            var eduJson = JsonSerializer.Serialize(student.Education);
            // Fix: Store the "Clean" hash in the DB so SyncAll can compare apples-to-apples.
            // If we want to track sim/episodes, we should store them as separate columns in RlJob, not bake into the hash.
            var eduHash = ComputeSha256($"{RecommendationVariantSchemaVersion}|{eduJson}");

            if (!force)
            {
                var latestJob = await _jobRepo.GetLatestByStudentIdAsync(student.Account.Id);
                if (latestJob != null && latestJob.EducationHash == eduHash && latestJob.IsSimulation == isSimulation)
                {
                    // 1. If it is already in progress, check if it's fresh (created in the last 1 hour)
                    if (latestJob.Status == JobStatus.Queued || latestJob.Status == JobStatus.Running)
                    {
                        if (latestJob.CreatedAt > DateTime.UtcNow.AddHours(-1))
                        {
                            Console.WriteLine($"[DEBUG] TriggerPrecompute: Job {latestJob.Id} is already in progress ({latestJob.Status}) for student {student.Account.Id}. Skipping duplicate run...");
                            return latestJob.Id.ToString();
                        }
                    }
                    // 2. If it is ready, make sure the recommendation exists
                    else if (latestJob.Status == JobStatus.Ready && !string.IsNullOrEmpty(latestJob.ResultRecommendationId))
                    {
                        var recommendation = await _recRepo.GetByIdAsync(latestJob.ResultRecommendationId);
                        if (recommendation != null)
                        {
                            Console.WriteLine($"[DEBUG] TriggerPrecompute: Student {student.Account.Id} already has a ready recommendation matching this education hash. Skipping redundant run...");
                            return latestJob.Id.ToString();
                        }
                    }
                }
            }

            // Create Job
            var job = new RlJob
            {
                StudentId = student.Account.Id,
                Status = JobStatus.Queued,
                CreatedAt = DateTime.UtcNow,
                EducationHash = eduHash,
                IsSimulation = isSimulation
            };

            await _jobRepo.CreateAsync(job);

            // Trigger Background Task
            _ = Task.Run(async () => await ProcessJobAsync(job.Id.ToString(), student, isSimulation, episodes, targetTrack, eduHash));

            return job.Id.ToString();
        }

        public async Task<object> GetJobStatusAsync()
        {
            var jobs = await _jobRepo.GetActiveJobsAsync();
            return jobs.Select(j => new
            {
                JobId = j.Id.ToString(),
                j.StudentId,
                Status = j.Status.ToString(),
                CreatedAt = j.CreatedAt,
                StartedAt = j.StartedAt,
                FinishedAt = j.FinishedAt,
                ResultRecommendationId = j.ResultRecommendationId,
                j.Error
            });
        }

        public async Task<RlRecommendation?> GetRecommendationAsync(string id)
        {
            // Assuming we can add GetByIdAsync to IRlRecommendationRepository or use the collection directly if needed
            // For now, I'll rely on the repository interface update or a direct find if I can view the repo.
            // Let's first check the repo interface in the next step.
            return await _recRepo.GetByIdAsync(id);
        }

        public async Task<SyncResult> SyncAllStudentsAsync(bool isSimulation = false)
        {
            var students = (await _studentRepo.GetAllAsync())
                .Where(s => string.IsNullOrWhiteSpace(s.Account.Role) || s.Account.Role.ToLower() != "admin")
                .ToList();
            var result = new SyncResult { TotalStudents = students.Count() };

            foreach (var student in students)
            {
                // Logic:
                // 1. Calculate current hash.
                // 2. Check if latest job matches this hash and is Finished (Ready).
                // 3. If not, trigger.

                var eduJson = JsonSerializer.Serialize(student.Education);
                // Hash is always "production" (raw) hash to allow comparison
                var currentHash = ComputeSha256($"{RecommendationVariantSchemaVersion}|{eduJson}");

                var latestJob = await _jobRepo.GetLatestByStudentIdAsync(student.Account.Id);

                bool needsJob = false;

                if (latestJob == null)
                {
                    needsJob = true;
                }
                else
                {
                    // 1. Check if hash or mode changed
                    if (latestJob.EducationHash != currentHash ||
                        latestJob.Status == JobStatus.Failed ||
                        latestJob.IsSimulation != isSimulation)
                    {
                        needsJob = true;
                    }
                    else if (latestJob.Status == JobStatus.Queued || latestJob.Status == JobStatus.Running)
                    {
                        // If job has been stuck in Queued/Running for more than 1 hour, retry it.
                        if (latestJob.CreatedAt < DateTime.UtcNow.Subtract(TimeSpan.FromHours(1)))
                        {
                            Console.WriteLine($"[DEBUG] SyncAll: Job {latestJob.Id} has been stuck in status {latestJob.Status} since {latestJob.CreatedAt}. Re-triggering...");
                            needsJob = true;
                        }
                    }
                    else if (latestJob.Status == JobStatus.Ready && !string.IsNullOrEmpty(latestJob.ResultRecommendationId))
                    {
                        // 2. Even if job says "Ready", check if the recommendation document still exists in the DB
                        var recommendation = await _recRepo.GetByIdAsync(latestJob.ResultRecommendationId);
                        if (recommendation == null)
                        {
                            Console.WriteLine($"[DEBUG] SyncAll: Job {latestJob.Id} is Ready but Recommendation {latestJob.ResultRecommendationId} is missing. Re-triggering...");
                            needsJob = true;
                        }
                    }
                }

                if (needsJob)
                {
                     // Trigger job with requested mode (simulation or production)
                     // Await the trigger to prevent slamming the RL service and database with concurrent requests
                    await TriggerPrecomputeAsync(student.Account.Id, isSimulation, episodes: null, force: true);

                    // Optional: Add a small delay if the RL service is fragile
                    await Task.Delay(500);

                    result.TriggeredJobs++;
                    result.TriggeredStudentIds.Add(student.Account.Id);
                }
            }

            return result;
        }

        private async Task ProcessJobAsync(string jobId, Student student, bool isSimulation, int? episodes, string? targetTrack, string educationHash)
        {
            try
            {
                Console.WriteLine($"[DEBUG] Job {jobId}: Starting track-aware bundle processing...");
                await _jobRepo.UpdateStatusAsync(jobId, JobStatus.Running);

                var tracksToCompute = ResolveTracks(targetTrack);
                var responsesByTrack = new Dictionary<string, RlTrainingResponse>();

                foreach (var track in tracksToCompute)
                {
                    Console.WriteLine($"[DEBUG] Job {jobId}: Computing all profiles for track={track}...");
                    var request = MapToRlRequest(student, isSimulation, episodes, track);

                    Console.WriteLine($"[DEBUG] Job {jobId}: Sending RL Request: {JsonSerializer.Serialize(request)}");
                    var response = await _rlService.GetRecommendationAsync(request);
                    Console.WriteLine($"[DEBUG] Job {jobId}: Received RL Response for track={track}");

                    responsesByTrack[track] = response;
                }

                if (!responsesByTrack.Any())
                {
                    throw new InvalidOperationException("No recommendation variants were created.");
                }

                var defaultTrack = responsesByTrack.ContainsKey(DefaultTargetTrack)
                    ? DefaultTargetTrack
                    : responsesByTrack.Keys.First();

                var recommendation = MapToBundleEntity(
                    responsesByTrack,
                    student.Account.Id,
                    defaultTrack,
                    DefaultObjectiveProfile,
                    jobId,
                    educationHash);

                await _recRepo.CreateAsync(recommendation);
                Console.WriteLine($"[DEBUG] Job {jobId}: Saved bundled recommendation ID: {recommendation.Id}");

                var recommendationId = recommendation.Id.ToString();

                await _jobRepo.UpdateResultAsync(jobId, recommendationId);

                student.LatestRecommendationId = recommendationId;
                await _studentRepo.UpsertAsync(student);

                Console.WriteLine($"[DEBUG] Job {jobId}: Finished successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] Job {jobId}: FAILED with error: {ex}");
                try
                {
                    await _jobRepo.UpdateStatusAsync(jobId, JobStatus.Failed, ex.Message);
                }
                catch (Exception finalEx)
                {
                    Console.WriteLine($"[CRITICAL] Job {jobId}: Failed to update status to Failed. Error: {finalEx}");
                }
            }
        }

        private static List<string> ResolveTracks(string? requestedTrack)
        {
            if (!string.IsNullOrWhiteSpace(requestedTrack))
            {
                return new List<string> { NormalizeTargetTrack(requestedTrack) };
            }

            // First production strategy: compute balanced once per track, not all profile×track combinations.
            return SupportedTracks.ToList();
        }

        private static string NormalizeTargetTrack(string? targetTrack)
        {
            var raw = (targetTrack ?? DefaultTargetTrack).Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
            return raw switch
            {
                "bigdata" or "big_data" or "big_data_track" => "big_data",
                "media" or "media_informatics" or "media_track" => "media",
                "general" or "general_track" => "general",
                _ => "general"
            };
        }

        private RlTrainingRequest MapToRlRequest(Student student, bool isSimulation, int? episodes, string targetTrack)
        {
            var edu = student.Education;

            // Simulation Logic: Truncate to N-2 semesters if simulation is requested
            var semesters = edu.Semesters ?? new List<Semester>();
            var totalCredits = edu.TotalCredits;
            var numSemesters = edu.NumSemesters;

            if (isSimulation && semesters.Count > 2)
            {
                // Simulate being 2 semesters back
                int take = semesters.Count - 2;
                semesters = semesters.Take(take).ToList();
                // Recalculate credits (approximate)
                totalCredits = semesters.Sum(s => s.SemesterCredits);
                numSemesters = semesters.Count;
            }

            var rlEdu = new RlEducation
            {
                TotalCredits = totalCredits,
                NumSemesters = numSemesters,
                Semesters = new Dictionary<string, RlSemester>()
            };

            foreach (var sem in semesters)
            {
                rlEdu.Semesters[sem.Term] = new RlSemester
                {
                    CumulativeGpa = sem.CumulativeGpa,
                    SemesterGpa = sem.SemesterGpa,
                    SemesterCredits = sem.SemesterCredits,
                    Optional = sem.Optional,
                    Courses = sem.Courses.Select(c => new RlCourse
                    {
                        CourseId = c.CourseId,
                        CourseName = c.CourseName,
                        Credit = c.Credit,
                        Grade = c.Grade,
                        Gpa = c.Gpa ?? 0
                    }).ToList()
                };
            }

            // Temporary Hugging Face-safe default.
            // Reset this to 2000 after adding a real backend queue for RL precompute.
            int epCount = episodes ?? 500;

            return new RlTrainingRequest
            {
                StudentId = student.Account.Id,
                Education = rlEdu,
                Episodes = epCount,
                PretrainSteps = epCount,
                MaxSemesters = 8,
                Seed = 42,
                Profile = DefaultObjectiveProfile,
                Profiles = SupportedObjectiveProfiles,
                TargetTrack = targetTrack
            };
        }

        private RlRecommendation MapToBundleEntity(
            Dictionary<string, RlTrainingResponse> responsesByTrack,
            string studentId,
            string defaultTrack,
            string defaultProfile,
            string jobId,
            string educationHash)
        {
            var normalizedDefaultTrack = NormalizeTargetTrack(defaultTrack);
            var defaultResponse = responsesByTrack[normalizedDefaultTrack];
            var defaultTrackRecommendation = MapTrack(defaultResponse, normalizedDefaultTrack);

            return new RlRecommendation
            {
                StudentId = studentId,
                CreatedAt = DateTime.UtcNow,
                ParentJobId = jobId,
                EducationHash = educationHash,

                TargetTrack = normalizedDefaultTrack,
                ObjectiveProfile = defaultProfile,
                Courses = defaultTrackRecommendation.Courses,
                TermIndex = defaultTrackRecommendation.SlatesByTerm?.FirstOrDefault()?.Term ?? 0,
                SlatesByTerm = defaultTrackRecommendation.SlatesByTerm,
                Metrics = defaultTrackRecommendation.Metrics,
                DefaultProfile = defaultResponse.DefaultProfile ?? defaultProfile,
                Profiles = defaultTrackRecommendation.Profiles,

                RawResponsesByTrack = responsesByTrack.ToDictionary(
                    kvp => NormalizeTargetTrack(kvp.Key),
                    kvp => kvp.Value.RawJson ?? JsonSerializer.Serialize(kvp.Value)),

                Tracks = responsesByTrack.ToDictionary(
                    kvp => NormalizeTargetTrack(kvp.Key),
                    kvp => MapTrack(kvp.Value, NormalizeTargetTrack(kvp.Key)))
            };
        }

        private TrackRecommendation MapTrack(RlTrainingResponse response, string targetTrack)
        {
            return new TrackRecommendation
            {
                TargetTrack = NormalizeTargetTrack(response.Metadata?.TargetTrack ?? targetTrack),
                DefaultProfile = response.DefaultProfile ?? DefaultObjectiveProfile,
                Courses = (response.RecommendedSlates != null && response.RecommendedSlates.Any())
                          ? response.RecommendedSlates.First()
                          : new List<string>(),
                SlatesByTerm = response.Terms?.Select(t => new TermRecommendation
                {
                    Term = t.Term,
                    Slate = t.Slate
                }).ToList(),
                Metrics = MapMetrics(response.Metadata, response.Terms),
                Profiles = response.Profiles?.ToDictionary(
                    kvp => kvp.Key.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_"),
                    kvp => MapProfile(kvp.Value)
                )
            };
        }

        private ProfileRecommendation MapProfile(ProfileRecommendationDto dto)
        {
            return new ProfileRecommendation
            {
                Courses = (dto.RecommendedSlates != null && dto.RecommendedSlates.Any())
                          ? dto.RecommendedSlates.First()
                          : new List<string>(),
                SlatesByTerm = dto.Terms?.Select(t => new TermRecommendation
                {
                    Term = t.Term,
                    Slate = t.Slate
                }).ToList(),
                Metrics = MapMetrics(dto.Metadata, dto.Terms)
            };
        }

        private RecommendationMetrics MapMetrics(RlMetadata? metadata, List<RlTermResult>? terms)
        {
            var lastTerm = terms?.OrderBy(t => t.Term).LastOrDefault();

            Dictionary<string, object>? gradFlags = null;
            if (metadata?.GradFlags != null)
            {
                gradFlags = metadata.GradFlags
                    .ToDictionary(kvp => kvp.Key, kvp => ConvertToPlainObject(kvp.Value));
            }
            else if (metadata?.TopFailedFlags is JsonElement je && je.ValueKind == JsonValueKind.Array)
            {
                gradFlags = je.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.Array && x.GetArrayLength() == 2)
                    .ToDictionary(
                        x => x[0].ToString(),
                        x => (object)x[1].GetInt32());
            }

            return new RecommendationMetrics
            {
                CumGpa = metadata?.FinalCumGpa
                         ?? metadata?.BestEpisode?.CumGpa
                         ?? lastTerm?.CumulativeGpaSoFar
                         ?? 0,
                TotalCredits = metadata?.FinalTotalCredits
                               ?? metadata?.BestEpisode?.TotalCredits
                               ?? metadata?.TotalCredits
                               ?? lastTerm?.TotalCreditsSoFar
                               ?? 0,
                Graduated = metadata?.Graduated
                            ?? (metadata?.Status == "already_finished"
                                || (metadata?.BestEpisode?.Graduated ?? lastTerm?.GraduatedSoFar ?? false)),
                GradFlags = gradFlags ?? new Dictionary<string, object>()
            };
        }

        private static object ConvertToPlainObject(object value)
        {
            if (value is JsonElement je)
            {
                return je.ValueKind switch
                {
                    JsonValueKind.True    => true,
                    JsonValueKind.False   => false,
                    JsonValueKind.Null    => (object)"null",
                    JsonValueKind.Number  => je.TryGetInt64(out var l)  ? (object)l
                                           : je.TryGetDouble(out var d) ? d : je.GetRawText(),
                    JsonValueKind.String  => je.GetString() ?? "",
                    JsonValueKind.Array   => je.EnumerateArray()
                                               .Select(e => ConvertToPlainObject(e))
                                               .ToList<object>(),
                    JsonValueKind.Object  => je.EnumerateObject()
                                               .ToDictionary(p => p.Name, p => ConvertToPlainObject(p.Value)),
                    _                    => je.GetRawText()
                };
            }

            return value;
        }

        private static string ComputeSha256(string rawData)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                StringBuilder builder = new StringBuilder();

                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }

                return builder.ToString();
            }
        }
    }
}
