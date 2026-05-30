using NUPAL.Core.Application.DTOs;
using NUPAL.Core.Application.Interfaces;
using Nupal.Domain.Entities;
using MongoDB.Bson;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace Nupal.Core.Infrastructure.Services
{
    public class PrecomputeService : IPrecomputeService
    {
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

        public async Task<string> TriggerPrecomputeAsync(string studentId, bool isSimulation = false, int? episodes = null, bool force = false)
        {
            var student = await _studentRepo.GetByIdAsync(studentId) 
                          ?? await _studentRepo.FindByEmailAsync(studentId); // Support ID or Email

            if (student == null)
                throw new KeyNotFoundException($"Student {studentId} not found");

            // Compute Hash of Education to prevent redundant training if needed
            var eduJson = JsonSerializer.Serialize(student.Education);
            // Fix: Store the "Clean" hash in the DB so SyncAll can compare apples-to-apples.
            // If we want to track sim/episodes, we should store them as separate columns in RlJob, not bake into the hash.
            var eduHash = ComputeSha256(eduJson); 

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
            _ = Task.Run(async () => await ProcessJobAsync(job.Id.ToString(), student, isSimulation, episodes));

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
                var currentHash = ComputeSha256(eduJson); 

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

        private async Task ProcessJobAsync(string jobId, Student student, bool isSimulation, int? episodes)
        {
            try
            {
                Console.WriteLine($"[DEBUG] Job {jobId}: Starting processing...");
                await _jobRepo.UpdateStatusAsync(jobId, JobStatus.Running);

                Console.WriteLine($"[DEBUG] Job {jobId}: Mapping request...");
                var request = MapToRlRequest(student, isSimulation, episodes);
                
                Console.WriteLine($"[DEBUG] Job {jobId}: Sending RL Request: {JsonSerializer.Serialize(request)}");
                var response = await _rlService.GetRecommendationAsync(request);
                Console.WriteLine($"[DEBUG] Job {jobId}: Received RL Response: {JsonSerializer.Serialize(response)}");

                Console.WriteLine($"[DEBUG] Job {jobId}: Mapping to Entity...");
                var recommendation = MapToEntity(response, student.Account.Id);
                
                Console.WriteLine($"[DEBUG] Job {jobId}: Saving Recommendation...");
                await _recRepo.CreateAsync(recommendation);
                Console.WriteLine($"[DEBUG] Job {jobId}: Saved Recommendation ID: {recommendation.Id}");

                Console.WriteLine($"[DEBUG] Job {jobId}: Updating Job Result...");
                await _jobRepo.UpdateResultAsync(jobId, recommendation.Id.ToString());
                
                Console.WriteLine($"[DEBUG] Job {jobId}: Updating Student...");
                // Update Student with latest recommendation
                student.LatestRecommendationId = recommendation.Id.ToString();
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

        private RlTrainingRequest MapToRlRequest(Student student, bool isSimulation, int? episodes)
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

            // Determine episode count: 
            // 1. Explicitly provided (testing)
            // 2. Simulation -> Default 5
            // 3. Production -> Default 5000
            int epCount = episodes ?? 2000;

            return new RlTrainingRequest
            {
                StudentId = student.Account.Id,
                Education = rlEdu,
                Episodes = epCount,
                PretrainSteps = epCount,
                MaxSemesters = 8,
                Seed = 42
            };
        }

        private RlRecommendation MapToEntity(RlTrainingResponse response, string studentId)
        {
            var lastTerm = response.Terms?.OrderBy(t => t.Term).LastOrDefault();

            Dictionary<string, object>? gradFlags = null;
            if (response.Metadata?.GradFlags != null)
            {
                // Convert JsonElement values to plain CLR types so MongoDB can serialize them
                gradFlags = response.Metadata.GradFlags
                    .ToDictionary(kvp => kvp.Key, kvp => ConvertToPlainObject(kvp.Value));
            }
            else if (response.Metadata?.TopFailedFlags is JsonElement je && je.ValueKind == JsonValueKind.Array)
            {
                gradFlags = je.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.Array && x.GetArrayLength() == 2)
                    .ToDictionary(
                        x => x[0].ToString(), 
                        x => (object)x[1].GetInt32());
            }

            return new RlRecommendation
            {
                StudentId = studentId,
                CreatedAt = DateTime.UtcNow,
                Courses = (response.RecommendedSlates != null && response.RecommendedSlates.Any()) 
                          ? response.RecommendedSlates.First() 
                          : new List<string>(),
                TermIndex = response.Terms?.FirstOrDefault()?.Term ?? 0,
                SlatesByTerm = response.Terms?.Select(t => new TermRecommendation 
                { 
                    Term = t.Term, 
                    Slate = t.Slate 
                }).ToList(),
                Metrics = new RecommendationMetrics
                {
                    CumGpa = response.Metadata?.FinalCumGpa 
                             ?? response.Metadata?.BestEpisode?.CumGpa 
                             ?? lastTerm?.CumulativeGpaSoFar 
                             ?? 0,
                    TotalCredits = response.Metadata?.FinalTotalCredits 
                                   ?? response.Metadata?.BestEpisode?.TotalCredits 
                                   ?? response.Metadata?.TotalCredits 
                                   ?? lastTerm?.TotalCreditsSoFar 
                                   ?? 0,
                    Graduated = response.Metadata?.Graduated 
                                ?? (response.Metadata?.Status == "already_finished" 
                                    || (response.Metadata?.BestEpisode?.Graduated ?? lastTerm?.GraduatedSoFar ?? false)),
                    GradFlags = gradFlags ?? new Dictionary<string, object>()
                },
                DefaultProfile = response.DefaultProfile ?? "balanced",
                Profiles = response.Profiles?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => MapProfile(kvp.Value)
                )
            };
        }

        private ProfileRecommendation MapProfile(ProfileRecommendationDto dto)
        {
            var lastTerm = dto.Terms?.OrderBy(t => t.Term).LastOrDefault();

            Dictionary<string, object>? gradFlags = null;
            if (dto.Metadata?.GradFlags != null)
            {
                // Convert JsonElement values to plain CLR types so MongoDB can serialize them
                gradFlags = dto.Metadata.GradFlags
                    .ToDictionary(kvp => kvp.Key, kvp => ConvertToPlainObject(kvp.Value));
            }
            else if (dto.Metadata?.TopFailedFlags is JsonElement je && je.ValueKind == JsonValueKind.Array)
            {
                gradFlags = je.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.Array && x.GetArrayLength() == 2)
                    .ToDictionary(
                        x => x[0].ToString(), 
                        x => (object)x[1].GetInt32());
            }

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
                Metrics = new RecommendationMetrics
                {
                    CumGpa = dto.Metadata?.FinalCumGpa 
                             ?? dto.Metadata?.BestEpisode?.CumGpa 
                             ?? lastTerm?.CumulativeGpaSoFar 
                             ?? 0,
                    TotalCredits = dto.Metadata?.FinalTotalCredits 
                                   ?? dto.Metadata?.BestEpisode?.TotalCredits 
                                   ?? dto.Metadata?.TotalCredits 
                                   ?? lastTerm?.TotalCreditsSoFar 
                                   ?? 0,
                    Graduated = dto.Metadata?.Graduated 
                                ?? (dto.Metadata?.Status == "already_finished" 
                                    || (dto.Metadata?.BestEpisode?.Graduated ?? lastTerm?.GraduatedSoFar ?? false)),
                    GradFlags = gradFlags ?? new Dictionary<string, object>()
                }
            };
        }

        /// <summary>Converts JsonElement and nested JsonElement values to plain CLR types for MongoDB compatibility.</summary>
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
