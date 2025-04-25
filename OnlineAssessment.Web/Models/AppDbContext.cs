using Microsoft.EntityFrameworkCore;
using OnlineAssessment.Web.Models;

namespace OnlineAssessment.Web.Models
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) {}

        public DbSet<User> Users { get; set; }
        public DbSet<Test> Tests { get; set; }
        // Question and AnswerOption tables removed as they're no longer used
        // Questions are now stored in CategoryQuestions as JSON
        public DbSet<TestResult> TestResults { get; set; }
        // OrganizationTestResult table removed as it's redundant with TestResult
        public DbSet<Payment> Payments { get; set; }
        public DbSet<CategoryQuestions> CategoryQuestions { get; set; }
        public DbSet<Organization> Organizations { get; set; }
        public DbSet<TestBooking> TestBookings { get; set; }
        // SuperOrganization table has been removed, using Organization with IsSuperOrganization flag instead

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // ✅ Store UserRole Enum as a string in the database
            modelBuilder.Entity<User>()
                .Property(u => u.Role)
                .HasConversion<string>();

            base.OnModelCreating(modelBuilder);
        }
    }
}
