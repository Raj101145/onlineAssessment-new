using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineAssessment.Web.Models
{
    public class TestBooking
    {
        [Key]
        public int Id { get; set; }

        public int TestId { get; set; }

        public int UserId { get; set; }

        public DateTime BookedAt { get; set; } = Utilities.TimeZoneHelper.GetCurrentIstTime();

        // Custom time selection fields
        public DateTime BookingDate { get; set; }

        public DateTime StartTime { get; set; }

        public DateTime EndTime { get; set; }

        // Slot number (1-4) for fixed time slots
        public int SlotNumber { get; set; }

        // User SAP ID for tracking who booked the test
        public string? UserSapId { get; set; }

        [ForeignKey("TestId")]
        public virtual Test Test { get; set; }

        [ForeignKey("UserId")]
        public virtual User User { get; set; }

        // Non-database property to indicate whether the test can be started
        [NotMapped]
        public bool CanStartTest { get; set; }
    }
}
