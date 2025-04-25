using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineAssessment.Web.Models
{
    [Table("organizations")]
    public class Organization
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Name { get; set; }

        [Required]
        public string Email { get; set; }

        [Required]
        public string ContactPerson { get; set; }

        public string? PhoneNumber { get; set; }

        public string? Address { get; set; }

        public string? Website { get; set; }

        public string? Description { get; set; }

        public string? LogoUrl { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastLoginAt { get; set; }

        // Foreign key to User table (nullable now that we're moving away from User table)
        public int? UserId { get; set; }

        // SAP ID for unique identification
        public string? SapId { get; set; }

        // Authentication fields
        [Required]
        public string Username { get; set; } // Same as Name by default
        [Required]
        public string PasswordHash { get; set; }
        public string Role { get; set; } = "Organization"; // Default role
        public bool IsSuperOrganization { get; set; } = false; // Flag for super organization

        // Reset password token
        public string? ResetPasswordToken { get; set; }
        public DateTime? ResetPasswordTokenExpiry { get; set; }

        // OTP related fields (for future use if needed)
        public string? OtpCode { get; set; }
        public DateTime? OtpExpiry { get; set; }
        public bool IsOtpVerified { get; set; } = false;

        // Navigation properties for related entities
        public ICollection<Test>? Tests { get; set; }
        public ICollection<CategoryQuestions>? CategoryQuestions { get; set; }
    }
}
