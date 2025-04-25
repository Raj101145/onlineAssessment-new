using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineAssessment.Web.Models;
using System;
using System.IO;
using System.Threading.Tasks;

namespace OnlineAssessment.Web
{
    public class CleanupOrganizationUsers
    {
        public static async Task Main(string[] args)
        {
            // Create a service collection
            var services = new ServiceCollection();
            
            // Add the DbContext
            services.AddDbContext<AppDbContext>(options =>
                options.UseMySql(
                    "server=localhost;database=onlineassessmentdb;user=root;password=admin123", 
                    ServerVersion.AutoDetect("server=localhost;database=onlineassessmentdb;user=root;password=admin123")));
            
            // Build the service provider
            var serviceProvider = services.BuildServiceProvider();
            
            // Get the DbContext
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            Console.WriteLine("Cleaning up organization users...");
            
            try
            {
                // Execute the SQL directly
                var sql = @"
                    UPDATE Users 
                    SET FirstName = NULL, 
                        LastName = NULL, 
                        MobileNumber = NULL, 
                        PhotoUrl = NULL, 
                        KeySkills = NULL, 
                        Employment = NULL, 
                        Education = NULL, 
                        Category = NULL 
                    WHERE Role = 0;  -- 0 is the enum value for Organization role
                ";
                
                var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(sql);
                
                Console.WriteLine($"Successfully updated {rowsAffected} organization users.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
