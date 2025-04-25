using Microsoft.EntityFrameworkCore;
using OnlineAssessment.Web.Models;
using OnlineAssessment.Web.Utilities;

namespace OnlineAssessment.Web.Services
{
    public class ExpiredTestCleanupService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ExpiredTestCleanupService> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1); // Check every hour

        public ExpiredTestCleanupService(
            IServiceProvider serviceProvider,
            ILogger<ExpiredTestCleanupService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Expired Test Cleanup Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CleanupExpiredTests();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while cleaning up expired tests.");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }
        }

        private async Task CleanupExpiredTests()
        {
            _logger.LogInformation("Checking for expired tests to clean up...");

            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // Get current time in IST
                var currentTimeIST = TimeZoneHelper.GetCurrentIstTime();
                _logger.LogInformation($"Current time (IST): {currentTimeIST}");

                try
                {
                    // Use raw SQL to find expired tests to avoid EF Core issues
                    var sql = @"SELECT Id, Title FROM Tests
                               WHERE IsScheduleRestricted = 1
                               AND ScheduledEndTime IS NOT NULL
                               AND ScheduledEndTime < UTC_TIMESTAMP()
                               AND (IsDeleted = 0 OR IsDeleted IS NULL)";

                    List<(int Id, string Title)> expiredTests = new List<(int Id, string Title)>();

                    using (var connection = new MySqlConnector.MySqlConnection(dbContext.Database.GetConnectionString()))
                    {
                        await connection.OpenAsync();

                        using (var command = new MySqlConnector.MySqlCommand(sql, connection))
                        {
                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                while (await reader.ReadAsync())
                                {
                                    var id = reader.GetInt32(0);
                                    var title = reader.GetString(1);
                                    expiredTests.Add((id, title));
                                }
                            }
                        }
                    }

                    _logger.LogInformation($"Found {expiredTests.Count} expired tests to clean up.");

                    // Update each expired test
                    foreach (var (id, title) in expiredTests)
                    {
                        try
                        {
                            // First delete all test bookings for this test using EF Core
                            var bookings = await dbContext.TestBookings.Where(tb => tb.TestId == id).ToListAsync();
                            if (bookings.Any())
                            {
                                _logger.LogInformation($"Found {bookings.Count} bookings to delete for test {id} ({title}).");
                                dbContext.TestBookings.RemoveRange(bookings);
                                await dbContext.SaveChangesAsync();
                                _logger.LogInformation($"Successfully deleted {bookings.Count} test bookings for test {id} ({title}).");
                            }
                            else
                            {
                                _logger.LogInformation($"No bookings found for test {id} ({title}).");
                            }

                            // Then use raw SQL to mark the test as deleted
                            var updateSql = $"UPDATE Tests SET IsDeleted = 1, DeletedAt = UTC_TIMESTAMP() WHERE Id = {id}";
                            await dbContext.Database.ExecuteSqlRawAsync(updateSql);

                            _logger.LogInformation($"Marked test {id} ({title}) as deleted.");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error cleaning up test {id}: {ex.Message}");
                        }
                    }

                    _logger.LogInformation("Expired test cleanup completed.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in cleanup process. Database schema might not be updated yet.");
                }
            }
        }
    }
}
