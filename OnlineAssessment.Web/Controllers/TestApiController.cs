using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineAssessment.Web.Models;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;

namespace OnlineAssessment.Web.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class TestApiController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<TestController> _logger;

        public TestApiController(AppDbContext context, ILogger<TestController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // Create a new test
        [HttpPost("create")]
        [Authorize(Roles = "Organization")]
        public async Task<IActionResult> CreateTest([FromBody] Models.TestCreationDto testDto)
        {
            try
            {
                _logger.LogInformation($"Received test creation request: Title={testDto.Title}, Type={testDto.Type}, Duration={testDto.DurationMinutes}");

                if (string.IsNullOrWhiteSpace(testDto.Title))
                    return BadRequest(new { message = "Test title is required" });

                if (testDto.DurationMinutes <= 0 || testDto.DurationMinutes > 1440)
                    return BadRequest(new { message = "Duration must be between 1 and 1440 minutes" });

                // Get the current user's ID and role
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

                // Organizations can always create tests
                if (userRole == "Organization" && userId != null)
                {
                    // No subscription check needed

                    // Slot checking removed - now handled by booking system
                }

                // Create the test
                var test = new Test
                {
                    Title = testDto.Title,
                    Description = testDto.Description ?? $"Test created on {Utilities.TimeZoneHelper.GetCurrentIstTime():yyyy-MM-dd HH:mm:ss}",
                    DurationMinutes = testDto.DurationMinutes,
                    Type = testDto.Type,
                    CreatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime(),
                    CreatedBy = userRole == "Organization" && userId != null ? int.Parse(userId) : null,
                    MaxAttempts = testDto.MaxAttempts,
                    Domain = testDto.Domain,
                    HasUploadedFile = true, // Always set to true so tests are immediately visible

                    // Scheduling properties - will be set during booking
                    ScheduledStartTime = null,
                    ScheduledEndTime = null,
                    IsScheduleRestricted = testDto.IsScheduleRestricted,
                    CurrentUserCount = 0,
                    MaxUsersPerSlot = 200 // Default to 200 users per time slot
                };

                _context.Tests.Add(test);
                await _context.SaveChangesAsync();

                // Handle questions based on source
                if (testDto.UseCategory)
                {
                    // Validate minimum question count
                    if (testDto.QuestionCount < 60)
                    {
                        return BadRequest(new { message = "At least 60 questions are required for the test" });
                    }

                    // Use questions from category bank
                    _logger.LogInformation($"Using {testDto.QuestionCount} questions from category {testDto.Domain}");

                    // Get questions from category bank
                    if (userId == null)
                    {
                        return BadRequest(new { message = "User ID not found" });
                    }

                    var organizationId = int.Parse(userId);
                    var categoryQuestions = await _context.CategoryQuestions
                        .FirstOrDefaultAsync(cq => cq.Category == testDto.Domain && cq.CreatedBy == organizationId);

                    if (categoryQuestions == null)
                    {
                        return BadRequest(new { message = $"No questions found for category {testDto.Domain}" });
                    }

                    // Deserialize questions and validate that the category has at least 60 questions
                    var options = new JsonSerializerOptions {
                        ReferenceHandler = ReferenceHandler.Preserve,
                        MaxDepth = 64
                    };
                    var allQuestions = System.Text.Json.JsonSerializer.Deserialize<List<QuestionDto>>(categoryQuestions.QuestionsJson, options);
                    if (allQuestions == null)
                    {
                        return BadRequest(new { message = "No questions found in the selected category. At least 60 questions are required." });
                    }

                    if (allQuestions.Count < 60)
                    {
                        return BadRequest(new { message = $"The selected category has only {allQuestions.Count} questions. At least 60 questions are required." });
                    }

                    // Set the CategoryQuestionsId on the test
                    test.CategoryQuestionsId = categoryQuestions.Id;
                    test.QuestionCount = testDto.QuestionCount;
                    test.HasUploadedFile = true; // Mark as having questions

                    // Save the test with the CategoryQuestionsId
                    await _context.SaveChangesAsync();

                    // We don't need to create individual Question records anymore
                    _logger.LogInformation($"Test created with CategoryQuestionsId: {test.CategoryQuestionsId}");
                }
                // Direct question upload has been removed in favor of using category questions only
                else
                {
                    return BadRequest(new { message = "No category selected for questions. Please select a category." });
                }

                return Ok(new {
                    message = "Test created successfully",
                    testId = test.Id,
                    testTitle = test.Title,
                    redirectUrl = $"/Test/Index?testCreated={test.Id}"
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Error creating test: " + ex.Message });
            }
        }

        // Retrieve all tests
        [HttpGet("all")]
        public async Task<IActionResult> GetAllTests()
        {
            var tests = await _context.Tests.ToListAsync();

            // Filter out deleted tests if the IsDeleted column exists
            try
            {
                tests = tests.Where(t => !t.IsDeleted).ToList();
            }
            catch
            {
                // IsDeleted property might not exist yet, ignore the error
            }
            return Ok(tests);
        }

        // Simple test endpoint
        [HttpGet("Ping")]
        [AllowAnonymous]
        public IActionResult Ping()
        {
            return Ok(new { success = true, message = "Pong" });
        }

        // Update user attempts for a test
        [HttpPost("UpdateAttempts")]
        [Authorize(Roles = "Organization")]
        public async Task<IActionResult> UpdateUserAttempts([FromBody] UpdateAttemptsRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.Username) || request.TestId <= 0)
                {
                    return BadRequest(new { success = false, message = "Username and TestId are required" });
                }

                // Get the current user's ID and role
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userId == null)
                {
                    return Unauthorized(new { success = false, message = "Unauthorized" });
                }

                var organizationId = int.Parse(userId);

                // Find the test
                var test = await _context.Tests
                    .FirstOrDefaultAsync(t => t.Id == request.TestId && t.CreatedBy == organizationId);

                if (test == null)
                {
                    return NotFound(new { success = false, message = "Test not found or you don't have permission to modify it" });
                }

                // Find the test results for this user and test
                var testResults = await _context.TestResults
                    .Where(r => r.TestId == request.TestId && r.Username == request.Username)
                    .OrderByDescending(r => r.SubmittedAt)
                    .ToListAsync();

                if (testResults == null || !testResults.Any())
                {
                    return NotFound(new { success = false, message = "Test result not found for this user" });
                }

                // Calculate the total attempts from test results
                int totalAttempts = testResults.Count;

                // If we're adding attempts, we don't need to do anything
                // If we're removing attempts, we need to delete the most recent test result
                if (request.Change < 0 && testResults.Any())
                {
                    // Remove the most recent test result
                    _context.TestResults.Remove(testResults.First());
                    totalAttempts--;

                    // Save changes
                    await _context.SaveChangesAsync();
                }

                return Ok(new {
                    success = true,
                    message = $"Updated attempts for {request.Username} on test {test.Title}",
                    newAttempts = totalAttempts
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user attempts");
                return StatusCode(500, new { success = false, message = "Error updating user attempts: " + ex.Message });
            }
        }

        // Cleanup orphaned test bookings
        [HttpGet]
        [Route("Test/CleanupBookings")]
        [Authorize(Roles = "Admin,Organization")]
        public async Task<IActionResult> CleanupBookings()
        {
            try
            {
                // Get all deleted tests
                var deletedTestIds = await _context.Tests
                    .Where(t => t.IsDeleted)
                    .Select(t => t.Id)
                    .ToListAsync();

                _logger.LogInformation($"Found {deletedTestIds.Count} deleted tests");

                // Find bookings for deleted tests
                var orphanedBookings = await _context.TestBookings
                    .Where(tb => deletedTestIds.Contains(tb.TestId))
                    .ToListAsync();

                _logger.LogInformation($"Found {orphanedBookings.Count} orphaned bookings to delete");

                if (orphanedBookings.Any())
                {
                    // Remove the orphaned bookings
                    _context.TestBookings.RemoveRange(orphanedBookings);
                    await _context.SaveChangesAsync();

                    return Ok(new { success = true, message = $"Successfully deleted {orphanedBookings.Count} orphaned test bookings" });
                }

                return Ok(new { success = true, message = "No orphaned bookings found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up orphaned bookings");
                return StatusCode(500, new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // Redirect to the slot booking view page after checking payment
        [HttpGet]
        [Route("Test/BookSlot/{id}")]
        [Authorize(Roles = "Candidate")]
        public async Task<IActionResult> BookSlot(int id)
        {
            try
            {
                var test = await _context.Tests.FindAsync(id);
                if (test == null)
                {
                    return RedirectToAction("Index", "Test", new { error = "Test not found" });
                }

                // Message is passed via query parameter and handled directly in the view

                // Check if the user has already booked this test
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return RedirectToAction("Login", "Auth");
                }

                var candidateId = int.Parse(userId);
                var existingBooking = await _context.TestBookings
                    .FirstOrDefaultAsync(tb => tb.TestId == id && tb.UserId == candidateId);

                if (existingBooking != null)
                {
                    return RedirectToAction("Index", "Test", new { error = "You have already booked this test" });
                }

                // No payment required for slot booking
                return RedirectToAction("BookSlot", "Test", new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in BookSlot action");
                return RedirectToAction("Index", "Test", new { error = "An error occurred while loading the booking page" });
            }
        }

        // Process the slot booking - DISABLED, use TestController.ProcessBooking instead
        [HttpPost]
        [Route("Test/ProcessBooking/{id}")]
        [Authorize(Roles = "Admin")] // Restrict to admin only to prevent accidental use
        public async Task<IActionResult> ProcessBooking(int id, string selectedDate, int selectedSlot)
        {
            try
            {
                var test = await _context.Tests.FindAsync(id);
                if (test == null)
                {
                    return RedirectToAction("Index", "Test", new { error = "Test not found" });
                }

                // Check if the user has already booked this test
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return RedirectToAction("Login", "Auth");
                }

                var candidateId = int.Parse(userId);
                var existingBooking = await _context.TestBookings
                    .FirstOrDefaultAsync(tb => tb.TestId == id && tb.UserId == candidateId);

                if (existingBooking != null)
                {
                    return RedirectToAction("Index", "Test", new { error = "You have already booked this test" });
                }

                // Check if the user has already booked any other slot
                var hasAnyBooking = await _context.TestBookings
                    .AnyAsync(tb => tb.UserId == candidateId);

                // Parse the selected date
                if (!DateTime.TryParse(selectedDate, out DateTime bookingDate))
                {
                    return RedirectToAction("BookSlot", "Test", new { id, error = "Invalid date selected" });
                }

                // Calculate start and end times based on slot
                DateTime startTime;
                DateTime endTime;

                switch (selectedSlot)
                {
                    case 1: // 9:00 AM - 11:00 AM
                        startTime = bookingDate.Date.AddHours(9);
                        endTime = bookingDate.Date.AddHours(11);
                        break;
                    case 2: // 12:00 PM - 2:00 PM
                        startTime = bookingDate.Date.AddHours(12);
                        endTime = bookingDate.Date.AddHours(14);
                        break;
                    case 3: // 3:00 PM - 5:00 PM
                        startTime = bookingDate.Date.AddHours(15);
                        endTime = bookingDate.Date.AddHours(17);
                        break;
                    case 4: // 6:00 PM - 8:00 PM
                        startTime = bookingDate.Date.AddHours(18);
                        endTime = bookingDate.Date.AddHours(20);
                        break;
                    case 5: // 9:00 PM - 11:00 PM
                        startTime = bookingDate.Date.AddHours(21);
                        endTime = bookingDate.Date.AddHours(23);
                        break;
                    default:
                        return RedirectToAction("BookSlot", "Test", new { id, error = "Invalid slot selected" });
                }

                // Check if the time slot is available (no overlapping bookings)
                var overlappingBookings = await _context.TestBookings
                    .Where(tb => tb.TestId == id && tb.BookingDate.Date == bookingDate.Date &&
                           ((tb.StartTime <= startTime && tb.EndTime > startTime) ||
                            (tb.StartTime < endTime && tb.EndTime >= endTime) ||
                            (tb.StartTime >= startTime && tb.EndTime <= endTime)))
                    .CountAsync();

                if (overlappingBookings >= 200) // Max 200 users per time slot
                {
                    return RedirectToAction("BookSlot", "Test", new { id, error = "This time slot is already full. Please select a different time." });
                }

                // Create the booking
                var booking = new TestBooking
                {
                    TestId = id,
                    UserId = candidateId,
                    BookedAt = Utilities.TimeZoneHelper.GetCurrentIstTime(),
                    BookingDate = Utilities.TimeZoneHelper.ToIst(bookingDate.Date),
                    StartTime = Utilities.TimeZoneHelper.ToIst(startTime),
                    EndTime = Utilities.TimeZoneHelper.ToIst(endTime)
                };

                _context.TestBookings.Add(booking);

                // Update the test with scheduling information
                test.ScheduledStartTime = Utilities.TimeZoneHelper.ToIst(startTime);
                test.ScheduledEndTime = Utilities.TimeZoneHelper.ToIst(endTime);
                test.CurrentUserCount++;

                await _context.SaveChangesAsync();

                // Redirect to test list with success message
                return RedirectToAction("Index", "Test", new { message = "Your slot has been booked successfully! You can access the test during the scheduled time." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProcessBooking action");
                return RedirectToAction("BookSlot", "Test", new { id, error = "An error occurred while processing your booking" });
            }
        }

        // Check time slot availability
        [HttpGet]
        [Route("Test/CheckTimeAvailability")]
        [Authorize(Roles = "Candidate")]
        public async Task<IActionResult> CheckTimeAvailability(int testId, string date, string startTime, string endTime)
        {
            try
            {
                if (!DateTime.TryParse(date, out DateTime bookingDate))
                {
                    return Ok(new { isAvailable = false, message = "Invalid date format" });
                }

                if (!DateTime.TryParse(startTime, out DateTime start) || !DateTime.TryParse(endTime, out DateTime end))
                {
                    return Ok(new { isAvailable = false, message = "Invalid time format" });
                }

                // Combine the date with the time
                start = bookingDate.Date.Add(start.TimeOfDay);
                end = bookingDate.Date.Add(end.TimeOfDay);

                // Check if the time slot is available (no overlapping bookings)
                var overlappingBookings = await _context.TestBookings
                    .Where(tb => tb.TestId == testId && tb.BookingDate.Date == bookingDate.Date &&
                           ((tb.StartTime <= start && tb.EndTime > start) ||
                            (tb.StartTime < end && tb.EndTime >= end) ||
                            (tb.StartTime >= start && tb.EndTime <= end)))
                    .CountAsync();

                bool isAvailable = overlappingBookings < 200; // Max 200 users per time slot

                return Ok(new { isAvailable, currentCount = overlappingBookings, maxCount = 200 });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking time slot availability");
                return Ok(new { isAvailable = false, message = "Error checking availability" });
            }
        }

        // Check if a time slot is available for a domain
        [HttpGet("check-time-availability")]
        [Route("Test/check-time-availability")]
        [Authorize(Roles = "Organization")]
        public async Task<IActionResult> CheckTimeAvailabilityForDomain(string domain, string startTime, string endTime)
        {
            try
            {
                if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(startTime) || string.IsNullOrEmpty(endTime))
                {
                    return BadRequest(new { message = "Invalid domain or time parameters" });
                }

                if (!DateTime.TryParse(startTime, out DateTime start) || !DateTime.TryParse(endTime, out DateTime end))
                {
                    return BadRequest(new { message = "Invalid time format" });
                }

                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { message = "User not authenticated" });
                }

                var organizationId = int.Parse(userId);

                // Check if there's an overlapping test for this domain (excluding deleted tests)
                var existingTest = await _context.Tests
                    .Where(t => t.CreatedBy == organizationId &&
                           t.Domain == domain &&
                           !t.IsDeleted &&
                           ((t.ScheduledStartTime <= start && t.ScheduledEndTime > start) ||
                            (t.ScheduledStartTime < end && t.ScheduledEndTime >= end) ||
                            (t.ScheduledStartTime >= start && t.ScheduledEndTime <= end)))
                    .FirstOrDefaultAsync();

                return Ok(new { isAvailable = existingTest == null });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking time slot availability");
                return StatusCode(500, new { message = "Error checking time slot availability" });
            }
        }
    }
}
