using Microsoft.EntityFrameworkCore;
using OnlineAssessment.Web.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace OnlineAssessment.Web
{
    public class UpdateOrganizationUsers
    {
        private readonly AppDbContext _context;

        public UpdateOrganizationUsers(AppDbContext context)
        {
            _context = context;
        }

        public async Task UpdateExistingOrganizationUsers()
        {
            // Get all organization users
            var organizationUsers = await _context.Users
                .Where(u => u.Role == UserRole.Organization)
                .ToListAsync();

            foreach (var user in organizationUsers)
            {
                // Clear organization-specific fields from User table
                user.FirstName = null;
                user.LastName = null;
                user.MobileNumber = null;
                user.PhotoUrl = null;
                user.KeySkills = null;
                user.Employment = null;
                user.Education = null;
                user.Category = null;
            }

            await _context.SaveChangesAsync();
            Console.WriteLine($"Updated {organizationUsers.Count} organization users.");
        }
    }
}
