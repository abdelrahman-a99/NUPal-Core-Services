using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nupal.Domain.Entities;

namespace NUPAL.Core.Application.Utilities
{
    public static class EducationHashHelper
    {
        public const string RecommendationVariantSchemaVersion = "track-aware-bundle-v1";

        private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public static string ComputeHash(Education education)
        {
            var canonical = BuildCanonicalPayload(education);
            var json = JsonSerializer.Serialize(canonical, CanonicalJsonOptions);
            return ComputeSha256($"{RecommendationVariantSchemaVersion}|{json}");
        }

        /// <summary>
        /// Previous hash format kept for backward compatibility with existing MongoDB records.
        /// </summary>
        public static string ComputeLegacyHash(Education education)
        {
            var eduJson = JsonSerializer.Serialize(education);
            return ComputeSha256($"{RecommendationVariantSchemaVersion}|{eduJson}");
        }

        public static bool HashMatchesStored(string currentHash, string? storedHash, Education education)
        {
            if (string.IsNullOrEmpty(storedHash))
                return false;

            if (storedHash == currentHash)
                return true;

            return storedHash == ComputeLegacyHash(education);
        }

        private static object BuildCanonicalPayload(Education education)
        {
            var semesters = (education.Semesters ?? new List<Semester>())
                .OrderBy(s => s.Term, StringComparer.Ordinal)
                .Select(sem => new
                {
                    term = sem.Term,
                    optional = sem.Optional,
                    semesterCredits = sem.SemesterCredits,
                    semesterGpa = sem.SemesterGpa,
                    cumulativeGpa = sem.CumulativeGpa,
                    courses = (sem.Courses ?? new List<Course>())
                        .OrderBy(c => c.CourseId, StringComparer.Ordinal)
                        .Select(c => new
                        {
                            courseId = c.CourseId,
                            courseName = c.CourseName,
                            credit = c.Credit,
                            grade = c.Grade,
                            gpa = c.Gpa
                        })
                        .ToList()
                })
                .ToList();

            return new
            {
                totalCredits = education.TotalCredits,
                numSemesters = education.NumSemesters,
                semesters
            };
        }

        private static string ComputeSha256(string rawData)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawData));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
