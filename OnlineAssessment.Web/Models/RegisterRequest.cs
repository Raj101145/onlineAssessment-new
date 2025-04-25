namespace OnlineAssessment.Web.Models
{
    public class RegisterRequest
    {
        public required string Username { get; set; }
        public required string Email { get; set; }
        public string? Password { get; set; } // Password is now optional
        public string? Role { get; set; } // Role is now optional

        // User fields - common for all users
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? MobileNumber { get; set; }
        public string? ProfilePicture { get; set; } // Will be stored as PhotoUrl in User model
        public string? KeySkills { get; set; }
        public string? Employment { get; set; } // Will be converted to enum
        public string? Education { get; set; } // Will be converted to enum
        public string? Category { get; set; }

        // Organization-specific fields - only used for creating Organization records
        // These fields will NOT be stored in the User table
        public string? OrganizationName { get; set; }
        public string? ContactPerson { get; set; }
        public string? Address { get; set; }
        public string? Website { get; set; }
        public string? Description { get; set; }
    }
}
