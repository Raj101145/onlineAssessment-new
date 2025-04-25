using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineAssessment.Web.Models;
using System.Security.Claims;
using System;

namespace OnlineAssessment.Web.Controllers
{
    [Authorize(Roles = "Organization")]
    public class AdminController : Controller
    {
        private readonly AppDbContext _context;

        public AdminController(AppDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        [Route("admin/update-organization-users")]
        [AllowAnonymous] // Only for initial setup, remove this attribute after running once
        public async Task<IActionResult> UpdateOrganizationUsers()
        {
            try
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
                return Ok($"Updated {organizationUsers.Count} organization users.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }

        [HttpGet]
        [Route("admin/cleanup-organization-users-sql")]
        [AllowAnonymous] // Only for initial setup, remove this attribute after running once
        public async Task<IActionResult> CleanupOrganizationUsersSql()
        {
            try
            {
                // Execute SQL directly to update organization users
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

                var rowsAffected = await _context.Database.ExecuteSqlRawAsync(sql);

                return Ok($"Successfully updated {rowsAffected} organization users using direct SQL.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error executing SQL: {ex.Message}");
            }
        }

        [HttpGet]
        [Route("admin/update-organization-table")]
        [AllowAnonymous] // Only for initial setup, remove this attribute after running once
        public async Task<IActionResult> UpdateOrganizationTable()
        {
            try
            {
                // Add authentication fields to Organization table
                // MySQL doesn't support IF NOT EXISTS for columns, so we need to check if columns exist first
                try
                {
                    // Try to add Username column
                    await _context.Database.ExecuteSqlRawAsync("ALTER TABLE organizations ADD COLUMN Username VARCHAR(255) NOT NULL DEFAULT '';");
                }
                catch { /* Column might already exist */ }

                try
                {
                    // Try to add PasswordHash column
                    await _context.Database.ExecuteSqlRawAsync("ALTER TABLE organizations ADD COLUMN PasswordHash VARCHAR(255) NOT NULL DEFAULT '';");
                }
                catch { /* Column might already exist */ }

                try
                {
                    // Try to add Role column
                    await _context.Database.ExecuteSqlRawAsync("ALTER TABLE organizations ADD COLUMN Role VARCHAR(50) NOT NULL DEFAULT 'Organization';");
                }
                catch { /* Column might already exist */ }

                try
                {
                    // Try to add OtpCode column
                    await _context.Database.ExecuteSqlRawAsync("ALTER TABLE organizations ADD COLUMN OtpCode VARCHAR(50) NULL;");
                }
                catch { /* Column might already exist */ }

                try
                {
                    // Try to add OtpExpiry column
                    await _context.Database.ExecuteSqlRawAsync("ALTER TABLE organizations ADD COLUMN OtpExpiry DATETIME NULL;");
                }
                catch { /* Column might already exist */ }

                try
                {
                    // Try to add IsOtpVerified column
                    await _context.Database.ExecuteSqlRawAsync("ALTER TABLE organizations ADD COLUMN IsOtpVerified TINYINT(1) NOT NULL DEFAULT 0;");
                }
                catch { /* Column might already exist */ }

                // All columns have been added (or already exist)

                // Copy data from Users table to Organization table for existing organizations
                var copyDataSQL = @"
                    UPDATE organizations o
                    JOIN users u ON o.UserId = u.Id
                    SET o.Username = u.Username,
                        o.PasswordHash = u.PasswordHash,
                        o.Role = 'Organization';
                ";

                var rowsAffected = await _context.Database.ExecuteSqlRawAsync(copyDataSQL);

                // Add indexes for faster lookups
                try
                {
                    // Check if email index exists
                    var emailIndexExists = await _context.Database.ExecuteSqlRawAsync(
                        "SELECT 1 FROM information_schema.statistics WHERE table_schema = DATABASE() AND table_name = 'organizations' AND index_name = 'idx_organization_email';");

                    if (emailIndexExists == 0) // Index doesn't exist
                    {
                        // Try to add email index
                        await _context.Database.ExecuteSqlRawAsync("ALTER TABLE organizations ADD INDEX idx_organization_email (Email);");
                    }
                }
                catch (Exception indexEx)
                {
                    // Log the error but continue
                    Console.WriteLine($"Error creating email index: {indexEx.Message}");
                }

                try
                {
                    // Check if username index exists
                    var usernameIndexExists = await _context.Database.ExecuteSqlRawAsync(
                        "SELECT 1 FROM information_schema.statistics WHERE table_schema = DATABASE() AND table_name = 'organizations' AND index_name = 'idx_organization_username';");

                    if (usernameIndexExists == 0) // Index doesn't exist
                    {
                        // Try to add username index
                        await _context.Database.ExecuteSqlRawAsync("ALTER TABLE organizations ADD INDEX idx_organization_username (Username);");
                    }
                }
                catch (Exception indexEx)
                {
                    // Log the error but continue
                    Console.WriteLine($"Error creating username index: {indexEx.Message}");
                }

                return Ok($"Successfully updated Organization table structure and copied data from {rowsAffected} User records.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error updating Organization table: {ex.Message}");
            }
        }

        [HttpGet]
        [Route("admin/make-userid-nullable")]
        [AllowAnonymous] // Only for initial setup, remove this attribute after running once
        public async Task<IActionResult> MakeUserIdNullable()
        {
            try
            {
                // First, check if there are any foreign keys on the UserId column
                var checkForeignKeysSQL = @"
                    SELECT CONSTRAINT_NAME
                    FROM information_schema.TABLE_CONSTRAINTS
                    WHERE CONSTRAINT_TYPE = 'FOREIGN KEY'
                    AND TABLE_NAME = 'organizations'
                    AND CONSTRAINT_SCHEMA = DATABASE();
                ";

                // Execute the SQL to get foreign key constraints
                var foreignKeys = new List<string>();
                using (var command = _context.Database.GetDbConnection().CreateCommand())
                {
                    command.CommandText = checkForeignKeysSQL;
                    _context.Database.OpenConnection();
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            foreignKeys.Add(reader.GetString(0));
                        }
                    }
                }

                // Drop any foreign key constraints
                foreach (var foreignKey in foreignKeys)
                {
                    try
                    {
                        await _context.Database.ExecuteSqlRawAsync($"ALTER TABLE organizations DROP FOREIGN KEY `{foreignKey}`;");
                    }
                    catch (Exception ex)
                    {
                        // Log the error but continue
                        Console.WriteLine($"Error dropping foreign key {foreignKey}: {ex.Message}");
                    }
                }

                // Now modify the UserId column to be nullable
                try
                {
                    await _context.Database.ExecuteSqlRawAsync("ALTER TABLE organizations MODIFY COLUMN UserId INT NULL;");
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"Error making UserId nullable: {ex.Message}");
                }

                return Ok($"Successfully made UserId column nullable in Organization table.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error updating Organization table: {ex.Message}");
            }
        }
    }
}
