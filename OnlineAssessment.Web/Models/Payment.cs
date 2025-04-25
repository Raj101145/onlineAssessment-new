using System.ComponentModel.DataAnnotations;

namespace OnlineAssessment.Web.Models
{
    public class Payment
    {
        public int Id { get; set; }
        
        [Required]
        public int UserId { get; set; }
        
        [Required]
        public decimal Amount { get; set; }
        
        [Required]
        public string Currency { get; set; } = "INR";
        
        [Required]
        public string Status { get; set; } = "Pending";
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime? PaidAt { get; set; }
        
        // Navigation property
        public User User { get; set; }
    }
}
