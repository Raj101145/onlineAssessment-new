using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineAssessment.Web.Models
{
    public class Test
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Title { get; set; }

        [Required]
        public string Description { get; set; }

        [Required]
        [Range(1, 1440, ErrorMessage = "Duration must be between 1 and 1440 minutes.")]
        public int DurationMinutes { get; set; }

        [Required]
        public TestType Type { get; set; }

        [Required]
        [Column(TypeName = "datetime(6)")]
        public DateTime CreatedAt { get; set; } = Utilities.TimeZoneHelper.GetCurrentIstTime();  // Store in IST instead of UTC

        public int? CreatedBy { get; set; }  // ID of the organization that created the test

        // MaxStudents field removed as per requirement

        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Maximum attempts per student must be at least 1.")]
        public int MaxAttempts { get; set; } = 1;  // Default value of 1 attempt per student

        // New property for sharing
        public string ShareId { get; set; } = Guid.NewGuid().ToString("N");

        // Domain/Category for the test
        public string? Domain { get; set; }

        // Category ID to pull questions from (for dynamic question loading)
        public int? CategoryQuestionsId { get; set; }

        // Number of questions to pull from the category
        public int QuestionCount { get; set; } = 20;

        // Flag to indicate if a test file has been uploaded
        public bool HasUploadedFile { get; set; } = false;

        // Scheduling properties
        public DateTime? ScheduledStartTime { get; set; }
        public DateTime? ScheduledEndTime { get; set; }

        // Removed SlotNumber property in favor of time-based approach

        // Current number of users for this test
        public int CurrentUserCount { get; set; } = 0;

        // Maximum users per time slot (default 200)
        public int MaxUsersPerSlot { get; set; } = 200;

        // Flag to indicate if the test is accessible only during scheduled time
        public bool IsScheduleRestricted { get; set; } = false;

        // Soft delete properties
        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedAt { get; set; }

        // Questions are loaded dynamically from CategoryQuestions
        [NotMapped]
        public ICollection<InMemoryQuestion> Questions { get; set; } = new List<InMemoryQuestion>();

        // ✅ Navigation property for test results
        public ICollection<TestResult> TestResults { get; set; } = new HashSet<TestResult>();
    }
}
