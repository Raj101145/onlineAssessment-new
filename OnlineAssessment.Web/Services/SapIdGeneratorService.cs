using Microsoft.EntityFrameworkCore;
using OnlineAssessment.Web.Models;

namespace OnlineAssessment.Web.Services
{
    public interface ISapIdGeneratorService
    {
        Task<string> GenerateUniqueIdAsync();
    }

    public class SapIdGeneratorService : ISapIdGeneratorService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<SapIdGeneratorService> _logger;
        private const string SAP_PREFIX = "1000";
        private const int SAP_LENGTH = 10;

        public SapIdGeneratorService(AppDbContext context, ILogger<SapIdGeneratorService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<string> GenerateUniqueIdAsync()
        {
            try
            {
                // Check if SapId column exists in the database
                string highestSapId = null;
                try
                {
                    // Try to get the highest SAP ID from the database
                    highestSapId = await _context.Users
                        .Where(u => u.SapId != null && u.SapId.StartsWith(SAP_PREFIX))
                        .Select(u => u.SapId)
                        .OrderByDescending(id => id)
                        .FirstOrDefaultAsync();
                }
                catch (Exception ex)
                {
                    // If the column doesn't exist, this will throw an exception
                    _logger.LogWarning(ex, "SapId column might not exist yet. Using default starting number.");
                }

                int nextNumber;

                if (highestSapId != null)
                {
                    // Extract the numeric part and increment
                    if (int.TryParse(highestSapId.Substring(SAP_PREFIX.Length), out int currentNumber))
                    {
                        nextNumber = currentNumber + 1;
                    }
                    else
                    {
                        // If parsing fails, start from a default number
                        nextNumber = 10000;
                    }
                }
                else
                {
                    // If no existing SAP IDs, start from a default number
                    nextNumber = 10000;
                }

                // Format the new SAP ID
                string newSapId = $"{SAP_PREFIX}{nextNumber.ToString().PadLeft(SAP_LENGTH - SAP_PREFIX.Length, '0')}";

                _logger.LogInformation($"Generated new SAP ID: {newSapId}");
                return newSapId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating unique SAP ID");
                // Fallback to a random ID if there's an error
                return $"{SAP_PREFIX}{new Random().Next(10000, 99999).ToString().PadLeft(6, '0')}";
            }
        }
    }
}
