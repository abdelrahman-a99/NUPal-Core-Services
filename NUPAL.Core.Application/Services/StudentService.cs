using NUPAL.Core.Application.DTOs;
using NUPAL.Core.Application.Interfaces;
using Nupal.Domain.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using System.Linq;

namespace NUPAL.Core.Application.Services
{
    public class StudentService : IStudentService
    {
        private readonly IStudentRepository _repo;
        private readonly IPrecomputeService _precomputeService;
        private readonly ICacheService _cache;

        private static readonly TimeSpan StudentTtl = TimeSpan.FromMinutes(30);

        public StudentService(IStudentRepository repo, IPrecomputeService precomputeService, ICacheService cache)
        {
            _repo = repo;
            _precomputeService = precomputeService;
            _cache = cache;
        }

        public async Task UpsertStudentAsync(ImportStudentDto dto)
        {
            var semesters = dto.Education.Semesters?
                .OrderBy(kv => kv.Key)
                .Select(kv => new Semester
                {
                    Term = kv.Key,
                    Optional = kv.Value.Optional,
                    Courses = kv.Value.Courses?
                        .OrderBy(c => c.CourseId)
                        .Select(c => new Course
                        {
                            CourseId = c.CourseId,
                            CourseName = c.CourseName,
                            Credit = c.Credit,
                            Grade = c.Grade,
                            Gpa = c.Gpa
                        }).ToList() ?? new List<Course>(),
                    SemesterCredits = kv.Value.SemesterCredits,
                    SemesterGpa = kv.Value.SemesterGpa,
                    CumulativeGpa = kv.Value.CumulativeGpa
                }).ToList() ?? new List<Semester>();

            var student = new Student
            {
                Account = new Account
                {
                    Id = dto.Account.Id,
                    Email = dto.Account.Email.ToLower(),
                    Name = dto.Account.Name,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Account.Password, workFactor: 10),
                    Role = string.IsNullOrWhiteSpace(dto.Account.Role) ? "student" : dto.Account.Role.ToLower()
                },
                Education = new Education
                {
                    TotalCredits = dto.Education.TotalCredits,
                    NumSemesters = dto.Education.NumSemesters,
                    Semesters = semesters
                }
            };

            await _repo.UpsertAsync(student);

            await _cache.RemoveAsync($"student:id:{student.Account.Id}");
            await _cache.RemoveAsync($"student:email:{student.Account.Email}");
            await _cache.RemoveAsync($"rl-rec:{student.Account.Id}");
            try 
            {
                await _precomputeService.TriggerPrecomputeAsync(student.Account.Id, isSimulation: false);
            }
            catch (Exception ex)
            {
                // We log but don't fail the import if precompute failing (resilience)
                Console.WriteLine($"[WARNING] Student {student.Account.Id} imported, but failed to trigger automatic precompute: {ex.Message}");
            }
        }

        public async Task<StudentDto> GetStudentByEmailAsync(string email)
        {
            var key = $"student:email:{email.ToLower()}";
            return await _cache.GetOrSetAsync(
                key,
                async () =>
                {
                    var s = await _repo.FindByEmailAsync(email.ToLower());
                    return s == null ? null! : MapToDto(s);
                },
                StudentTtl);
        }

        public async Task<StudentDto> GetStudentByIdAsync(string id)
        {
            var key = $"student:id:{id}";
            return await _cache.GetOrSetAsync(
                key,
                async () =>
                {
                    var s = await _repo.GetByIdAsync(id);
                    return s == null ? null! : MapToDto(s);
                },
                StudentTtl);
        }

        public async Task<AuthResponseDto> AuthenticateAsync(LoginDto loginDto, string jwtKey, string jwtIssuer, string jwtAudience)
        {
            var s = await _repo.FindByEmailAsync(loginDto.Email.ToLower());
            if (s == null || !BCrypt.Net.BCrypt.Verify(loginDto.Password, s.Account.PasswordHash))
            {
                return null;
            }

            // Role directly from database
            var role = string.IsNullOrWhiteSpace(s.Account.Role) ? "student" : s.Account.Role.ToLower();

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(jwtKey);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, s.Account.Id),
                    new Claim(ClaimTypes.Email, s.Account.Email),
                    new Claim(ClaimTypes.Name, s.Account.Name),
                    new Claim(ClaimTypes.Role, role)
                }),
                Expires = DateTime.UtcNow.AddDays(7),
                Issuer = jwtIssuer,
                Audience = jwtAudience,
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            var tokenString = tokenHandler.WriteToken(token);
            var studentDto = MapToDto(s);

            // Warm the cache on login — next dashboard load is instant
            await _cache.SetAsync($"student:id:{s.Account.Id}",    studentDto, StudentTtl);
            await _cache.SetAsync($"student:email:{s.Account.Email}", studentDto, StudentTtl);

            return new AuthResponseDto
            {
                Token = tokenString,
                Student = studentDto
            };
        }

        private StudentDto MapToDto(Student s)
        {
            return new StudentDto
            {
                Id = s.Account.Id,
                Account = new AccountDto
                {
                    Id = s.Account.Id,
                    Email = s.Account.Email,
                    Name = s.Account.Name
                },
                Education = new EducationDto
                {
                    TotalCredits = s.Education.TotalCredits,
                    NumSemesters = s.Education.NumSemesters,
                    Semesters = s.Education.Semesters.Select(sem => new SemesterDto
                    {
                        Term = sem.Term,
                        Optional = sem.Optional,
                        Courses = sem.Courses.Select(c => new CourseDto
                        {
                            CourseId = c.CourseId,
                            CourseName = c.CourseName,
                            Credit = c.Credit,
                            Grade = c.Grade,
                            Gpa = c.Gpa
                        }).ToList(),
                        SemesterCredits = sem.SemesterCredits,
                        SemesterGpa = sem.SemesterGpa,
                        CumulativeGpa = sem.CumulativeGpa
                    }).ToList()
                }
            };
        }
    }
}
