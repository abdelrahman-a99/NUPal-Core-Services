using NUPAL.Core.Application.DTOs;
using NUPAL.Core.Application.Interfaces;
using NUPAL.Core.Application.Utilities;
using Nupal.Domain.Entities;
using System.Text.Json;

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
                          ?? await _studentRepo.FindByEmailAsync(studentId);

            if (student == null)
                throw new KeyNotFoundException($"Student {studentId} not found");

            var eduHash = EducationHashHelper.ComputeHash(student.Education);

            if (!force)
            {
                if (!isSimulation)
                {
                    var existingRecommendation = await _recRepo.GetValidRecommendationForStudentAsync(
                        student.Account.Id, eduHash, student.Education);
                    if (existingRecommendation != null)
                    {
                        var latestReadyJob = await _jobRepo.GetLatestByStudentIdAsync(student.Account.Id);
                        Console.WriteLine($"[DEBUG] TriggerPrecompute: Student {student.Account.Id} already has recommendation for current education hash. Skipping.");
                        return latestReadyJob?.Id.ToString() ?? existingRecommendation.Id.ToString();
                    }
                }

                var inProgress = await _jobRepo.GetInProgressForStudentAsync(student.Account.Id, isSimulation);
                if (inProgress != null)
                {
                    Console.WriteLine($"[DEBUG] TriggerPrecompute: Job {inProgress.Id} already queued/running for student {student.Account.Id}. Skipping duplicate.");
                    return inProgress.Id.ToString();
                }
            }

            var job = new RlJob
            {
                StudentId = student.Account.Id,
                Status = JobStatus.Queued,
                CreatedAt = DateTime.UtcNow,
                EducationHash = eduHash,
                IsSimulation = isSimulation,
                Episodes = episodes,
                TargetTrack = targetTrack
            };

            await _jobRepo.CreateAsync(job);
            Console.WriteLine($"[DEBUG] TriggerPrecompute: Enqueued job {job.Id} for student {student.Account.Id}.");

            return job.Id.ToString();
        }

        public async Task ProcessQueuedJobAsync(string jobId, CancellationToken cancellationToken = default)
        {
            var job = await _jobRepo.GetByIdAsync(jobId);
            if (job == null)
            {
                Console.WriteLine($"[WARNING] ProcessQueuedJob: Job {jobId} not found.");
                return;
            }

            if (job.Status != JobStatus.Running)
            {
                Console.WriteLine($"[WARNING] ProcessQueuedJob: Job {jobId} is not running (status={job.Status}). Skipping.");
                return;
            }

            var student = await _studentRepo.GetByIdAsync(job.StudentId);
            if (student == null)
            {
                await _jobRepo.UpdateStatusAsync(jobId, JobStatus.Failed, $"Student {job.StudentId} not found.");
                return;
            }

            var currentHash = EducationHashHelper.ComputeHash(student.Education);
            if (!job.IsSimulation && EducationHashHelper.HashMatchesStored(currentHash, job.EducationHash, student.Education))
            {
                var existingRecommendation = await _recRepo.GetValidRecommendationForStudentAsync(
                    job.StudentId, currentHash, student.Education);
                if (existingRecommendation != null)
                {
                    Console.WriteLine($"[DEBUG] ProcessQueuedJob: Job {jobId} superseded by existing recommendation. Marking ready.");
                    await _jobRepo.UpdateResultAsync(jobId, existingRecommendation.Id.ToString());
                    return;
                }
            }

            await ProcessJobAsync(
                jobId,
                student,
                job.IsSimulation,
                job.Episodes,
                job.TargetTrack,
                job.EducationHash,
                cancellationToken);
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
            return await _recRepo.GetByIdAsync(id);
        }

        public async Task<SyncResult> SyncAllStudentsAsync(bool isSimulation = false)
        {
            var students = (await _studentRepo.GetAllAsync())
                .Where(s => string.IsNullOrWhiteSpace(s.Account.Role) || s.Account.Role.ToLower() != "admin")
                .ToList();
            var result = new SyncResult { TotalStudents = students.Count };

            foreach (var student in students)
            {
                var currentHash = EducationHashHelper.ComputeHash(student.Education);

                if (!isSimulation)
                {
                    var existingRecommendation = await _recRepo.GetValidRecommendationForStudentAsync(
                        student.Account.Id, currentHash, student.Education);
                    if (existingRecommendation != null)
                        continue;
                }

                var latestJob = await _jobRepo.GetLatestByStudentIdAsync(student.Account.Id);
                var needsJob = false;

                if (latestJob == null)
                {
                    needsJob = true;
                }
                else if (latestJob.IsSimulation != isSimulation)
                {
                    needsJob = true;
                }
                else if (!EducationHashHelper.HashMatchesStored(currentHash, latestJob.EducationHash, student.Education))
                {
                    needsJob = true;
                }
                else if (latestJob.Status == JobStatus.Failed)
                {
                    needsJob = true;
                }
                else if (latestJob.Status == JobStatus.Queued || latestJob.Status == JobStatus.Running)
                {
                    if (latestJob.CreatedAt < DateTime.UtcNow.Subtract(TimeSpan.FromHours(1)))
                    {
                        Console.WriteLine($"[DEBUG] SyncAll: Job {latestJob.Id} timed out in status {latestJob.Status}. Re-queueing.");
                        await _jobRepo.UpdateStatusAsync(latestJob.Id.ToString(), JobStatus.Failed, "Timed out waiting for RL service.");
                        needsJob = true;
                    }
                }
                else if (latestJob.Status == JobStatus.Ready && !string.IsNullOrEmpty(latestJob.ResultRecommendationId))
                {
                    var recommendation = await _recRepo.GetByIdAsync(latestJob.ResultRecommendationId);
                    if (recommendation == null)
                    {
                        Console.WriteLine($"[DEBUG] SyncAll: Job {latestJob.Id} is Ready but recommendation is missing. Re-queueing.");
                        needsJob = true;
                    }
                }

                if (!needsJob)
                    continue;

                var jobId = await TriggerPrecomputeAsync(student.Account.Id, isSimulation, episodes: null, force: false);
                result.TriggeredJobs++;
                result.TriggeredStudentIds.Add(student.Account.Id);
                Console.WriteLine($"[DEBUG] SyncAll: Enqueued job {jobId} for student {student.Account.Id}.");
            }

            return result;
        }

        private async Task ProcessJobAsync(
            string jobId,
            Student student,
            bool isSimulation,
            int? episodes,
            string? targetTrack,
            string educationHash,
            CancellationToken cancellationToken)
        {
            try
            {
                Console.WriteLine($"[DEBUG] Job {jobId}: Starting track-aware bundle processing...");
                await _jobRepo.UpdateStatusAsync(jobId, JobStatus.Running);

                var tracksToCompute = ResolveTracks(targetTrack);
                var responsesByTrack = new Dictionary<string, RlTrainingResponse>();

                foreach (var track in tracksToCompute)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    Console.WriteLine($"[DEBUG] Job {jobId}: Computing all profiles for track={track}...");
                    var request = MapToRlRequest(student, isSimulation, episodes, track);

                    Console.WriteLine($"[DEBUG] Job {jobId}: Sending RL Request: {JsonSerializer.Serialize(request)}");
                    var response = await _rlService.GetRecommendationAsync(request);
                    Console.WriteLine($"[DEBUG] Job {jobId}: Received RL Response for track={track}");

                    responsesByTrack[track] = response;
                }

                if (!responsesByTrack.Any())
                    throw new InvalidOperationException("No recommendation variants were created.");

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
                return new List<string> { NormalizeTargetTrack(requestedTrack) };

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

            var semesters = edu.Semesters ?? new List<Semester>();
            var totalCredits = edu.TotalCredits;
            var numSemesters = edu.NumSemesters;

            if (isSimulation && semesters.Count > 2)
            {
                int take = semesters.Count - 2;
                semesters = semesters.Take(take).ToList();
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

            int epCount = episodes ?? 2000;

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
    }
}
