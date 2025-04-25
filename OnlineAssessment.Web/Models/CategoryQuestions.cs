using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace OnlineAssessment.Web.Models
{
    public class CategoryQuestions
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Category { get; set; }

        [Required]
        public string QuestionsJson { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int CreatedBy { get; set; } // Organization ID

        [NotMapped]
        public List<QuestionDto> Questions
        {
            get
            {
                if (string.IsNullOrEmpty(QuestionsJson))
                    return new List<QuestionDto>();

                try
                {
                    var options = new JsonSerializerOptions
                    {
                        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
                        MaxDepth = 64
                    };
                    return JsonSerializer.Deserialize<List<QuestionDto>>(QuestionsJson, options) ?? new List<QuestionDto>();
                }
                catch (Exception ex)
                {
                    // Log the error or handle it appropriately
                    Console.Error.WriteLine($"Error deserializing questions: {ex.Message}");
                    return new List<QuestionDto>();
                }
            }
            set => QuestionsJson = JsonSerializer.Serialize(value, new JsonSerializerOptions
            {
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
                MaxDepth = 64
            });
        }

        // Navigation property for the organization that created these questions
        [ForeignKey("CreatedBy")]
        public User User { get; set; }

        // Static list of allowed categories
        public static readonly List<string> AllowedCategories = new List<string>
        {
            "BFSI Internship",
            "Digital Marketing  Internships",
            "IT Internships",
            "Relationship Executive Internships",
            "Business Development Internships",
            "Sales Internships",
            "Portfolio Internships",
            "Web Development Internships",
            "Software Development Internships"
        };
    }
}
