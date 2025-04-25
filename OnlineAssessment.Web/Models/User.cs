namespace OnlineAssessment.Web.Models
{
    public enum UserRole
    {
        Organization,
        Candidate
    }

    public enum EducationLevel
    {
        HighSchool,    // 10th
        SeniorSecondary,  // 12th
        Graduate,
        PostGraduate,
        Doctorate
    }

    public enum EmploymentStatus
    {
        Student,
        Employed,
        SelfEmployed,
        Unemployed,
        Fresher
    }

    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; set; }
        public UserRole Role { get; set; }  // ✅ Enum instead of string

        public string? PhotoUrl { get; set; }  // Profile photo URL

        // New fields
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? MobileNumber { get; set; }
        public string? KeySkills { get; set; }
        public EmploymentStatus? Employment { get; set; }
        public EducationLevel? Education { get; set; }
        public string? Category { get; set; }

        // SAP ID for unique identification
        public string? SapId { get; set; }

        // OTP related fields
        public string? OtpCode { get; set; }
        public DateTime? OtpExpiry { get; set; }
        public bool IsOtpVerified { get; set; } = false;

        // Payment is no longer required, but we keep this property for database compatibility
        public bool HasPaid { get; set; } = true;
    }
}
