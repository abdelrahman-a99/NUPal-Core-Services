using Nupal.Domain.Entities;

namespace NUPAL.Core.Application.DTOs
{
    public class RegistrationRequestDto
    {
        public string StudentId { get; set; } = "";
        public string StudentName { get; set; } = "";
        public string StudentEmail { get; set; } = "";
        public SchedulingBlock SelectedBlock { get; set; } = new();
        public bool IsFromRecommendation { get; set; }
        public bool IsFromRl { get; set; }
        public bool IsModified { get; set; }
    }

    public class ApproveRegistrationDto
    {
        public string Status { get; set; } = "Approved"; // Approved or Rejected
        public string? AdminNote { get; set; }
    }
}
