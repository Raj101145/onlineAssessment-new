using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineAssessment.Web.Models
{
    [Table("testresult")]
    public class TestResult
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int TestId { get; set; }

        [Required]
        public string Username { get; set; }

        [Required]
        public int TotalQuestions { get; set; }

        [Required]
        public int CorrectAnswers { get; set; }

        [Required]
        public double Score { get; set; }

        // These properties are required by the database schema but we only use MCQ now
        [Required]
        public double McqScore { get; set; }

        [Required]
        public double CodingScore { get; set; }

        [Required]
        public DateTime SubmittedAt { get; set; } = Utilities.TimeZoneHelper.GetCurrentIstTime();

        // Test time information
        public DateTime? StartTime { get; set; }

        public DateTime? EndTime { get; set; }

        // SAP ID for unique identification
        public string? UserSapId { get; set; }

        [ForeignKey("TestId")]
        public Test Test { get; set; }
    }
}