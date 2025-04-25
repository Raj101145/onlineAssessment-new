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
    public class TestController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<TestController> _logger;

        public TestController(AppDbContext context, IWebHostEnvironment environment, ILogger<TestController> logger)
        {
            _context = context;
            _environment = environment;
            _logger = logger;
        }

        // View action for test list page
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Index(string message = null, string error = null, bool clear = false, int? testCreated = null, bool refresh = false, bool ajax = false)
        {
            try
            {
                // We're removing the automatic redirect to MyBookings
                // This allows users to see the test list after booking
                // They can still access their bookings through the navigation menu

                // If clear parameter is true or we have TestCompleted flag, clear all test-related TempData
                if (clear || TempData.ContainsKey("TestCompleted"))
                {
                    // Clear all test-related TempData
                    if (TempData.ContainsKey("TestCompleted"))
                    {
                        TempData.Remove("TestCompleted");
                    }
                    if (TempData.ContainsKey("JustPaid"))
                    {
                        TempData.Remove("JustPaid");
                    }
                    if (TempData.ContainsKey("BookedTestId"))
                    {
                        TempData.Remove("BookedTestId");
                    }
                    if (TempData.ContainsKey("BookedDate"))
                    {
                        TempData.Remove("BookedDate");
                    }
                    if (TempData.ContainsKey("BookedStartTime"))
                    {
                        TempData.Remove("BookedStartTime");
                    }
                    if (TempData.ContainsKey("BookedEndTime"))
                    {
                        TempData.Remove("BookedEndTime");
                    }

                    // Add a success message if coming from test completion
                    if (string.IsNullOrEmpty(message) && clear)
                    {
                        message = "You can now browse available tests or check your test history.";
                    }
                }

                // Set message and error in ViewBag
                if (testCreated.HasValue)
                {
                    // Get the test title if available
                    string testTitle = "Test";
                    try
                    {
                        var createdTest = await _context.Tests.FindAsync(testCreated.Value);
                        if (createdTest != null)
                        {
                            testTitle = createdTest.Title;
                        }
                    }
                    catch {}

                    ViewBag.SuccessMessage = $"{testTitle} created successfully! You can now manage it from this page.";
                }
                else if (!string.IsNullOrEmpty(message))
                {
                    ViewBag.SuccessMessage = message;
                }

                if (!string.IsNullOrEmpty(error))
                {
                    ViewBag.ErrorMessage = error;
                }

                // If this is an AJAX request for refresh checking, return a simple response
                if (ajax && refresh)
                {
                    return Ok();
                }

                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
                var username = User.Identity?.Name ?? "Guest";

                ViewBag.IsAdmin = false;
                ViewBag.Username = username;

                // Payment is now handled during test booking, not after registration
                // No need to check if the user has paid here

                if (userRole == "Organization" && userId != null)
                {
                    var organizationId = int.Parse(userId);

                    // Always allow organizations to create tests
                    ViewBag.HasActiveSubscription = true;
                    ViewBag.CanCreateMcq = true;
                    ViewBag.CanCreateCoding = true;

                    var tests = await _context.Tests
                        .AsNoTracking() // Force a fresh query to the database
                        .Where(t => t.CreatedBy == organizationId && !t.IsDeleted)
                        .ToListAsync();

                    return View(tests);
                }
                else if (userRole == "Candidate" && userId != null)
                {
                    // For candidates, get their domain from the user record
                    var candidateId = int.Parse(userId);
                    var candidate = await _context.Users.FindAsync(candidateId);

                    // Get the list of tests that the candidate has booked
                    var bookedTestIds = await _context.TestBookings
                        .Where(tb => tb.UserId == candidateId)
                        .Select(tb => tb.TestId)
                        .ToListAsync();

                    ViewBag.BookedTests = bookedTestIds;

                    // Set the timezone to IST for date/time operations
                    ViewBag.TimeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

                    if (candidate != null && !string.IsNullOrEmpty(candidate.Category))
                    {
                        // Get upcoming bookings for this candidate
                        var now = DateTime.Now;
                        var upcomingBookings = await _context.TestBookings
                            .Include(tb => tb.Test)
                            .Where(tb => tb.UserId == candidateId &&
                                         ((tb.BookingDate > now.Date) ||
                                         (tb.BookingDate == now.Date && tb.EndTime > now)))
                            .OrderBy(tb => tb.BookingDate)
                            .ThenBy(tb => tb.StartTime)
                            .ToListAsync();
                        ViewBag.UpcomingBookings = upcomingBookings;

                        // Get tests that match the candidate's domain and are not deleted
                        // AND are not already booked by the user
                        // Remove tests from available list if user already has an upcoming booking for the same test
                        var upcomingTestIds = upcomingBookings.Select(tb => tb.TestId).ToList();
                        var tests = await _context.Tests
                            .AsNoTracking()
                            .Where(t => t.Domain == candidate.Category &&
                                   !t.IsDeleted &&
                                   !bookedTestIds.Contains(t.Id) &&
                                   !upcomingTestIds.Contains(t.Id))
                            .ToListAsync();

                        ViewBag.UserDomain = candidate.Category;
                        return View(tests);
                    }
                }

                return View(new List<Test>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Test Index action");
                return View(new List<Test>());
            }
        }

        // View action for uploading questions has been removed in favor of using category questions only

        // View action for showing test instructions
        [HttpGet]
        [Route("Test/Instructions/{id}")]
        [Authorize(Roles = "Candidate")]
        public async Task<IActionResult> Instructions(int id)
        {
            try
            {
                // First check if the user can take the test at this time
                // We'll do this by checking the same conditions as in the Take action
                // Get the test
                var test = await _context.Tests.FindAsync(id);
                if (test == null)
                {
                    return NotFound("The test you're looking for doesn't exist.");
                }

                // Get the user ID for booking check
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return RedirectToAction("Login", "Auth");
                }
                var candidateId = int.Parse(userId);

                // Check if the user has a booking for this test
                var booking = await _context.TestBookings
                    .FirstOrDefaultAsync(tb => tb.TestId == id && tb.UserId == candidateId);

                if (booking == null)
                {
                    return RedirectToAction("Index", new { error = "You haven't booked this test yet. Please book a slot first." });
                }

                // STRICT TIME ENFORCEMENT: Check if the current time is within the scheduled slot
                var now = DateTime.Now;
                var bookingDate = booking.BookingDate.Date;
                var currentDate = now.Date;
                var startTime = booking.StartTime;
                var endTime = booking.EndTime;
                var slotNumber = booking.SlotNumber;

                // Since we're now storing times in IST, we can use them directly
                DateTime bookingStartDateTime = booking.StartTime;
                DateTime bookingEndDateTime = booking.EndTime;

                // Check if the test is in the future
                bool isFutureTest = now < bookingStartDateTime;
                _logger.LogInformation("Checking if test is in the future. Current time: {CurrentTime}, Start time: {StartTime}, IsFutureTest: {IsFutureTest}", now, bookingStartDateTime, isFutureTest);

                if (isFutureTest)
                {
                    // Calculate time remaining for display
                    TimeSpan timeRemaining;
                    if (bookingDate > currentDate)
                    {
                        timeRemaining = bookingDate.Add(startTime.TimeOfDay) - now;
                    }
                    else
                    {
                        timeRemaining = startTime - now;
                    }

                    string timeRemainingMessage = "";
                    if (timeRemaining.Days > 0)
                    {
                        timeRemainingMessage += $"{timeRemaining.Days} day{(timeRemaining.Days != 1 ? "s" : "")} ";
                    }
                    if (timeRemaining.Hours > 0)
                    {
                        timeRemainingMessage += $"{timeRemaining.Hours} hour{(timeRemaining.Hours != 1 ? "s" : "")} ";
                    }
                    if (timeRemaining.Minutes > 0)
                    {
                        timeRemainingMessage += $"{timeRemaining.Minutes} minute{(timeRemaining.Minutes != 1 ? "s" : "")} ";
                    }
                    timeRemainingMessage += $"until your scheduled test time";

                    TempData["ErrorMessage"] = $"You cannot start this test before the scheduled time. Please wait {timeRemainingMessage}.";
                    return RedirectToAction("ScheduledTest", new { id });
                }

                // Check if the test is expired
                bool isExpired = now > bookingEndDateTime;
                _logger.LogInformation("Checking if test is expired. Current time: {CurrentTime}, End time: {EndTime}, IsExpired: {IsExpired}", now, bookingEndDateTime, isExpired);

                if (isExpired)
                {
                    // Get the slot information based on the slot number for display purposes
                    string slotDisplayTime = "";
                    switch (slotNumber)
                    {
                        case 1:
                            slotDisplayTime = "9:00 AM - 11:00 AM";
                            break;
                        case 2:
                            slotDisplayTime = "12:00 PM - 2:00 PM";
                            break;
                        case 3:
                            slotDisplayTime = "3:00 PM - 5:00 PM";
                            break;
                        case 4:
                            slotDisplayTime = "6:00 PM - 8:00 PM";
                            break;
                        default:
                            slotDisplayTime = $"{Utilities.TimeZoneHelper.ToIst(startTime).ToString("hh:mm tt")} - {Utilities.TimeZoneHelper.ToIst(endTime).ToString("hh:mm tt")}";
                            break;
                    }

                    TempData["ErrorMessage"] = $"Your test slot (Slot {slotNumber}: {slotDisplayTime}) has ended. Please contact support if you need assistance.";
                    return RedirectToAction("ScheduledTest", new { id });
                }

                // If we get here, the user is allowed to take the test
                // Load the test with questions
                test = await _context.Tests
                    .Include(t => t.Questions)
                        .ThenInclude(q => q.AnswerOptions)
                    .FirstOrDefaultAsync(t => t.Id == id);

                if (test == null)
                {
                    return NotFound("The test you're looking for doesn't exist.");
                }

                // Check if the test is deleted if the IsDeleted property exists
                try
                {
                    if (test.IsDeleted)
                    {
                        return NotFound("The test you're looking for has been deleted.");
                    }
                }
                catch
                {
                    // IsDeleted property might not exist yet, ignore the error
                }

                // Check if the user has just paid (coming from payment flow)
                bool hasJustPaid = TempData["JustPaid"] != null;

                // If the user has just paid, set a success message
                if (hasJustPaid)
                {
                    ViewBag.SuccessMessage = "Payment successful! You can now take your test.";
                    // Keep the JustPaid flag for the next request
                    TempData.Keep("JustPaid");
                }

                // Check if the test is schedule-restricted and if it's currently available
                if (test.IsScheduleRestricted && test.ScheduledStartTime.HasValue && test.ScheduledEndTime.HasValue)
                {
                    DateTime utcNow = DateTime.UtcNow;
                    // If the user has just paid, bypass the time restrictions
                    if (!hasJustPaid && (utcNow < test.ScheduledStartTime.Value || utcNow > test.ScheduledEndTime.Value))
                    {
                        // Convert times to IST for display
                        var istTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
                        var nowIST = TimeZoneInfo.ConvertTimeFromUtc(utcNow, istTimeZone);
                        var testStartTimeIST = TimeZoneInfo.ConvertTimeFromUtc(test.ScheduledStartTime.Value, istTimeZone);
                        var testEndTimeIST = TimeZoneInfo.ConvertTimeFromUtc(test.ScheduledEndTime.Value, istTimeZone);

                        if (nowIST < testStartTimeIST)
                        {
                            ViewBag.ErrorMessage = $"This test is not available yet. It will be available from {Utilities.TimeZoneHelper.FormatIstDateTime(testStartTimeIST)} IST to {Utilities.TimeZoneHelper.FormatIstDateTime(testEndTimeIST)} IST.";
                        }
                        else
                        {
                            ViewBag.ErrorMessage = $"This test is no longer available. It was available from {Utilities.TimeZoneHelper.FormatIstDateTime(testStartTimeIST)} IST to {Utilities.TimeZoneHelper.FormatIstDateTime(testEndTimeIST)} IST.";
                        }

                        ViewBag.ScheduledStartTime = testStartTimeIST;
                        ViewBag.ScheduledEndTime = testEndTimeIST;
                        ViewBag.TimeZone = "IST";
                        return View("TestNotAvailable", test);
                    }

                    // If the user is a candidate, check if they've booked this test
                    if (User.IsInRole("Candidate"))
                    {
                        // Check if the user has booked this test
                        string userIdInRole = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                        if (string.IsNullOrEmpty(userIdInRole))
                        {
                            return RedirectToAction("Login", "Account");
                        }

                        int candidateIdInRole = int.Parse(userIdInRole);
                        var hasBookedSlot = await _context.TestBookings
                            .AnyAsync(tb => tb.TestId == id && tb.UserId == candidateIdInRole);

                        // Check if the user has just paid (coming from payment flow)
                        bool userJustPaid = TempData["JustPaid"] != null;
                        bool paymentSuccessful = false;

                        if (!hasBookedSlot && !userJustPaid && !paymentSuccessful)
                        {
                            ViewBag.ErrorMessage = "You need to book a slot for this test before you can take it.";
                            return View("TestNotAvailable", test);
                        }

                        // If the user doesn't have a booking yet but has just paid, create a booking
                        if (!hasBookedSlot && (userJustPaid || paymentSuccessful))
                        {
                            _logger.LogInformation($"Creating booking for user {candidateIdInRole} for test {id} after payment");

                            // Get booking details from TempData
                            DateTime bookingDateValue = DateTime.Today;
                            DateTime startTimeValue = DateTime.Now;
                            DateTime endTimeValue = DateTime.Now.AddHours(2);
                            int slotNumberValue = 1;

                            if (TempData.TryGetValue("BookedDate", out var bookedDateObj) && bookedDateObj != null)
                            {
                                if (DateTime.TryParse(bookedDateObj.ToString(), out DateTime parsedDate))
                                {
                                    bookingDateValue = parsedDate.Date;
                                    _logger.LogInformation($"Using booking date from TempData: {bookingDateValue:yyyy-MM-dd}");
                                }
                            }

                            if (TempData.TryGetValue("BookedStartTime", out var startTimeObj) && startTimeObj != null)
                            {
                                if (DateTime.TryParse(startTimeObj.ToString(), out DateTime parsedStartTime))
                                {
                                    startTimeValue = bookingDateValue.Date.Add(parsedStartTime.TimeOfDay);
                                    _logger.LogInformation($"Using start time from TempData: {startTimeValue:HH:mm:ss}");
                                }
                            }

                            if (TempData.TryGetValue("BookedEndTime", out var endTimeObj) && endTimeObj != null)
                            {
                                if (DateTime.TryParse(endTimeObj.ToString(), out DateTime parsedEndTime))
                                {
                                    endTimeValue = bookingDateValue.Date.Add(parsedEndTime.TimeOfDay);
                                    _logger.LogInformation($"Using end time from TempData: {endTimeValue:HH:mm:ss}");
                                }
                            }

                            if (TempData.TryGetValue("BookedSlotNumber", out var slotNumberObj) && slotNumberObj != null)
                            {
                                if (int.TryParse(slotNumberObj.ToString(), out int parsedSlotNumber))
                                {
                                    slotNumberValue = parsedSlotNumber;
                                    _logger.LogInformation($"Using slot number from TempData: {slotNumberValue}");
                                }
                            }

                            // Get the user's SAP ID
                            string? userSapId = null;
                            var user = await _context.Users.FindAsync(candidateIdInRole);
                            if (user != null && !string.IsNullOrEmpty(user.SapId))
                            {
                                userSapId = user.SapId;
                                _logger.LogInformation($"Using SAP ID from user: {userSapId}");
                            }

                            // Create a new booking
                            var newBooking = new TestBooking
                            {
                                TestId = id,
                                UserId = candidateIdInRole,
                                BookedAt = Utilities.TimeZoneHelper.GetCurrentIstTime(),
                                BookingDate = bookingDateValue,
                                StartTime = startTimeValue,
                                EndTime = endTimeValue,
                                SlotNumber = slotNumberValue,
                                UserSapId = userSapId ?? ""
                            };

                            _context.TestBookings.Add(newBooking);

                            // Increment the user count for this test
                            test.CurrentUserCount++;

                            await _context.SaveChangesAsync();
                            _logger.LogInformation($"Successfully created booking with ID {newBooking.Id} after payment");

                            // Verify the booking was created
                            _context.ChangeTracker.Clear();
                            var verifyBooking = await _context.TestBookings.FirstOrDefaultAsync(b => b.Id == newBooking.Id);
                            if (verifyBooking != null) {
                                _logger.LogInformation($"Verified booking exists with ID: {verifyBooking.Id}");
                            } else {
                                _logger.LogWarning("Could not verify booking exists after creation");
                            }
                        }
                    }
                }

                return View(test);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Instructions action");
                return RedirectToAction(nameof(Index));
            }
        }

        // View action for taking a test
        [HttpGet]
        [Route("Test/Take/{id}")]
        [Authorize(Roles = "Candidate")]
        public async Task<IActionResult> Take(int id)
        {
            // Set flag to include code execution scripts
            ViewData["IncludeCodeExecution"] = true;

            try
            {
                // Get the test
                var test = await _context.Tests.FindAsync(id);
                if (test == null)
                {
                    return NotFound("The test you're looking for doesn't exist.");
                }

                // Get the user ID for booking check
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return RedirectToAction("Login", "Auth");
                }
                var candidateId = int.Parse(userId);

                // Check if the user has a booking for this test
                var booking = await _context.TestBookings
                    .FirstOrDefaultAsync(tb => tb.TestId == id && tb.UserId == candidateId);

                if (booking == null)
                {
                    return RedirectToAction("Index", new { error = "You haven't booked this test yet. Please book a slot first." });
                }

                // STRICT TIME ENFORCEMENT: Check if the current time is within the scheduled slot
                var now = DateTime.Now;
                var bookingDate = booking.BookingDate.Date;
                var currentDate = now.Date;
                var startTime = booking.StartTime;
                var endTime = booking.EndTime;
                var slotNumber = booking.SlotNumber;

                // Get the slot information based on the slot number for display purposes
                string slotDisplayTime = "";
                switch (slotNumber)
                {
                    case 1:
                        slotDisplayTime = "9:00 AM - 11:00 AM";
                        break;
                    case 2:
                        slotDisplayTime = "12:00 PM - 2:00 PM";
                        break;
                    case 3:
                        slotDisplayTime = "3:00 PM - 5:00 PM";
                        break;
                    case 4:
                        slotDisplayTime = "6:00 PM - 8:00 PM";
                        break;
                    default:
                        slotDisplayTime = $"{startTime.ToString("hh:mm tt")} - {endTime.ToString("hh:mm tt")}";
                        break;
                }

                // Since we're now storing times in IST, we can use them directly
                DateTime bookingStartDateTime = booking.StartTime;
                DateTime bookingEndDateTime = booking.EndTime;

                // Check if the test is in the future
                bool isFutureTest = now < bookingStartDateTime;
                if (isFutureTest)
                {
                    _logger.LogWarning("User attempted to start test {TestId} before scheduled time. Current time: {CurrentTime}, Test start time: {StartTime}",
                        id, now, startTime);

                    // Calculate time remaining for display
                    TimeSpan timeRemaining;
                    if (bookingDate > currentDate)
                    {
                        timeRemaining = bookingDate.Add(startTime.TimeOfDay) - now;
                    }
                    else
                    {
                        timeRemaining = startTime - now;
                    }

                    string timeRemainingMessage = "";
                    if (timeRemaining.Days > 0)
                    {
                        timeRemainingMessage += $"{timeRemaining.Days} day{(timeRemaining.Days != 1 ? "s" : "")} ";
                    }
                    if (timeRemaining.Hours > 0)
                    {
                        timeRemainingMessage += $"{timeRemaining.Hours} hour{(timeRemaining.Hours != 1 ? "s" : "")} ";
                    }
                    if (timeRemaining.Minutes > 0)
                    {
                        timeRemainingMessage += $"{timeRemaining.Minutes} minute{(timeRemaining.Minutes != 1 ? "s" : "")} ";
                    }
                    timeRemainingMessage += $"until your scheduled test time";

                    TempData["ErrorMessage"] = $"Your test is scheduled for {booking.BookingDate.Date.ToString("yyyy-MM-dd")} at {Utilities.TimeZoneHelper.ToIst(startTime).ToString("hh:mm tt")}. Please wait {timeRemainingMessage}.";
                    return RedirectToAction("ScheduledTest", new { id });
                }

                // We already created bookingStartDateTime and bookingEndDateTime above

                // Check if the test is expired
                bool isExpired = now > bookingEndDateTime;
                if (isExpired)
                {
                    _logger.LogWarning("User attempted to start test {TestId} after scheduled time. Current time: {CurrentTime}, Test end time: {EndTime}",
                        id, now, bookingEndDateTime);

                    TempData["ErrorMessage"] = $"Your test slot (Slot {slotNumber}: {slotDisplayTime}) has ended. Please contact support if you need assistance.";
                    return RedirectToAction("ScheduledTest", new { id });
                }
                // Check if the user has already taken this test
                var username = User.Identity?.Name;
                var testResult = await _context.TestResults
                    .FirstOrDefaultAsync(tr => tr.TestId == id && tr.Username == username);

                // Check if this is a retake attempt
                bool isRetake = Request.Query.ContainsKey("retake") && Request.Query["retake"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);

                if (testResult != null && !isRetake)
                {
                    // User has already taken this test, redirect to the result page
                    _logger.LogInformation($"User {username} attempted to take test {id} again, redirecting to result {testResult.Id}");
                    return RedirectToAction("Result", new { id = testResult.Id, message = "You have already taken this test. Here are your results." });
                }

                if (isRetake)
                {
                    _logger.LogInformation($"User {username} is retaking test {id}");
                    ViewBag.IsRetake = true;
                }

                // STRICT TIME ENFORCEMENT: Double-check that the current time is within the scheduled slot
                bool isWithinTimeSlot;

                // Check if the current time is within the time slot
                isWithinTimeSlot = now >= bookingStartDateTime && now <= bookingEndDateTime;
                _logger.LogInformation("Checking time slot: Current time: {CurrentTime}, Start time: {StartTime}, End time: {EndTime}, IsWithinTimeSlot: {IsWithinTimeSlot}",
                    now, bookingStartDateTime, bookingEndDateTime, isWithinTimeSlot);

                if (!isWithinTimeSlot)
                {
                    _logger.LogWarning("User {Username} attempted to take test {TestId} outside the scheduled time slot. Current time: {CurrentTime}, Slot: {StartTime} - {EndTime}",
                        username, id, now, booking.StartTime, booking.EndTime);

                    // Set an error message explaining why they're being redirected
                    string slotTimeDisplay = $"{Utilities.TimeZoneHelper.ToIst(booking.StartTime).ToString("hh:mm tt")} - {Utilities.TimeZoneHelper.ToIst(booking.EndTime).ToString("hh:mm tt")}";
                    TempData["ErrorMessage"] = $"You can only take this test during your scheduled time slot: {slotTimeDisplay}. Please try again during that time.";
                    return RedirectToAction("ScheduledTest", new { id = id });
                }

                // We already have slotDisplayTime from earlier

                // STRICT TIME ENFORCEMENT: Always check if the current time is within the scheduled slot
                // If the booking date is in the future, show a message
                if (now < bookingStartDateTime)
                {
                    // Log the attempt to start test before scheduled date/time
                    _logger.LogWarning("User {Username} attempted to start test {TestId} before scheduled time. Current time: {CurrentTime}, Test start time: {StartTime}",
                        username, id, now, bookingStartDateTime);

                    // Calculate time remaining
                    TimeSpan timeRemaining = bookingStartDateTime - now;
                    string timeRemainingMessage = "";
                    if (timeRemaining.Days > 0)
                    {
                        timeRemainingMessage += $"{timeRemaining.Days} day{(timeRemaining.Days != 1 ? "s" : "")} ";
                    }
                    if (timeRemaining.Hours > 0)
                    {
                        timeRemainingMessage += $"{timeRemaining.Hours} hour{(timeRemaining.Hours != 1 ? "s" : "")} ";
                    }
                    if (timeRemaining.Minutes > 0)
                    {
                        timeRemainingMessage += $"{timeRemaining.Minutes} minute{(timeRemaining.Minutes != 1 ? "s" : "")} ";
                    }
                    timeRemainingMessage += $"until your scheduled test time";

                    // Redirect to the scheduled test page with an error message
                    TempData["ErrorMessage"] = $"Your test is scheduled for {booking.BookingDate.Date.ToString("yyyy-MM-dd")} at {Utilities.TimeZoneHelper.ToIst(bookingStartDateTime).ToString("hh:mm tt")}. Please wait {timeRemainingMessage}.";
                    return RedirectToAction("ScheduledTest", new { id = id });
                }

                // Check if the user just booked this test
                bool justBooked = TempData["TestBooked"] != null && TempData["BookedTestId"] != null && (int)TempData["BookedTestId"] == id;

                // Even if the user just booked the test, enforce time restrictions
                if (justBooked)
                {
                    _logger.LogInformation("User just booked test ID: {TestId}. Still enforcing time restrictions.", id);
                    // Do not skip the time checks - continue with normal validation
                }
                // Otherwise, apply the normal time restrictions - this code is now redundant since we've already checked
                // if the test is in the future or expired, but we'll keep it for extra safety
                else if (now >= bookingStartDateTime && now <= bookingEndDateTime)
                {
                    // Log that the user is starting the test during the correct time slot
                    _logger.LogInformation("User {Username} is starting test {TestId} during slot {SlotNumber} ({SlotTime})",
                        username, id, slotNumber, slotDisplayTime);
                }
                else
                {
                    // If the booking date is in the past, show an error message
                    _logger.LogWarning("User {Username} attempted to start test {TestId} after the scheduled time. Current time: {CurrentTime}, Test end time: {EndTime}",
                        username, id, now, bookingEndDateTime);

                    // Redirect to the scheduled test page with an error message
                    TempData["ErrorMessage"] = $"Your test was scheduled for {booking.BookingDate.Date.ToString("yyyy-MM-dd")} (Slot {slotNumber}: {slotDisplayTime}). The scheduled time has passed.";
                    return RedirectToAction("ScheduledTest", new { id = id });
                }

                // Update the test with scheduling information
                test = await _context.Tests
                    .FirstOrDefaultAsync(t => t.Id == id);

                if (test == null)
                {
                    return NotFound("The test you're looking for doesn't exist.");
                }

                // Check if the test is schedule-restricted and if it's currently available
                if (test.IsScheduleRestricted && test.ScheduledStartTime.HasValue && test.ScheduledEndTime.HasValue)
                {
                    // Get the booking for this test
                    var testBooking = await _context.TestBookings
                        .FirstOrDefaultAsync(tb => tb.TestId == id && tb.UserId == candidateId);

                    if (testBooking == null)
                    {
                        return RedirectToAction("Index", new { error = "You need to book this test before you can take it." });
                    }

                    // Check if the current time is within the scheduled time slot
                    var currentTime = Utilities.TimeZoneHelper.GetCurrentIstTime();

                    // STRICT ENFORCEMENT: Check if the user is trying to start the test before the scheduled time
                    if (currentTime < test.ScheduledStartTime.Value)
                    {
                        // Convert times to IST for display
                        var istTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
                        var nowIST = TimeZoneInfo.ConvertTimeFromUtc(currentTime, istTimeZone);
                        var testStartTimeIST = TimeZoneInfo.ConvertTimeFromUtc(test.ScheduledStartTime.Value, istTimeZone);
                        var testEndTimeIST = TimeZoneInfo.ConvertTimeFromUtc(test.ScheduledEndTime.Value, istTimeZone);

                        // Log the attempt to start test before scheduled time
                        _logger.LogWarning($"User {username} attempted to start test {id} before scheduled time. Current time: {nowIST}, Test start time: {testStartTimeIST}");

                        ViewBag.ErrorMessage = $"This test is not available yet. It will be available from {Utilities.TimeZoneHelper.FormatIstDateTime(testStartTimeIST)} IST to {Utilities.TimeZoneHelper.FormatIstDateTime(testEndTimeIST)} IST.";
                        ViewBag.ScheduledStartTime = testStartTimeIST;
                        ViewBag.ScheduledEndTime = testEndTimeIST;
                        ViewBag.TimeZone = "IST";

                        // Redirect to the scheduled test page with an error message
                        TempData["ErrorMessage"] = $"You cannot start this test before the scheduled time. Please wait until {Utilities.TimeZoneHelper.FormatIstDateTime(testStartTimeIST)} IST.";
                        return RedirectToAction("ScheduledTest", new { id = test.Id });
                    }

                    // Check if the test has expired
                    if (currentTime > test.ScheduledEndTime.Value)
                    {
                        // Convert times to IST for display
                        var istTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
                        var nowIST = TimeZoneInfo.ConvertTimeFromUtc(currentTime, istTimeZone);
                        var testStartTimeIST = TimeZoneInfo.ConvertTimeFromUtc(test.ScheduledStartTime.Value, istTimeZone);
                        var testEndTimeIST = TimeZoneInfo.ConvertTimeFromUtc(test.ScheduledEndTime.Value, istTimeZone);

                        ViewBag.ErrorMessage = $"This test is no longer available. It was available from {Utilities.TimeZoneHelper.FormatIstDateTime(testStartTimeIST)} IST to {Utilities.TimeZoneHelper.FormatIstDateTime(testEndTimeIST)} IST.";
                        ViewBag.ScheduledStartTime = testStartTimeIST;
                        ViewBag.ScheduledEndTime = testEndTimeIST;
                        ViewBag.TimeZone = "IST";

                        // Redirect to the scheduled test page instead of showing the test not available page
                        return RedirectToAction("ScheduledTest", new { id = test.Id });
                    }
                }

                // Load questions for the test
                // Note: We already have the test object from earlier

                // Load questions either from the Questions table or from CategoryQuestions
                if (test != null && test.CategoryQuestionsId.HasValue)
                {
                    // Load questions from CategoryQuestions
                    var categoryQuestions = await _context.CategoryQuestions
                        .FirstOrDefaultAsync(cq => cq.Id == test.CategoryQuestionsId);

                    if (categoryQuestions != null)
                    {
                        // Deserialize questions from JSON
                        var options = new JsonSerializerOptions {
                            ReferenceHandler = ReferenceHandler.Preserve,
                            MaxDepth = 64
                        };
                        var allQuestions = JsonSerializer.Deserialize<List<QuestionDto>>(categoryQuestions.QuestionsJson, options);

                        // Randomly select questions if needed
                        var random = new Random();
                        var selectedQuestions = allQuestions
                            .OrderBy(q => random.Next())
                            .Take(Math.Min(test.QuestionCount, allQuestions.Count))
                            .ToList();

                        // Convert QuestionDto to InMemoryQuestion objects for the view
                        test.Questions = new List<InMemoryQuestion>();
                        for (int i = 0; i < selectedQuestions.Count; i++)
                        {
                            var q = selectedQuestions[i];
                            var questionId = 10000 + i; // Use consistent ID generation

                            var question = new InMemoryQuestion {
                                Id = questionId,
                                Text = q.Text,
                                Title = q.Title ?? q.Text.Substring(0, Math.Min(q.Text.Length, 100)),
                                Type = q.Type,
                                TestId = test.Id,
                                AnswerOptions = new List<InMemoryAnswerOption>()
                            };

                            // Add answer options
                            if (q.AnswerOptions != null)
                            {
                                for (int j = 0; j < q.AnswerOptions.Count; j++)
                                {
                                    var o = q.AnswerOptions[j];
                                    var optionId = 100000 + (i * 100) + j; // Use consistent ID generation

                                    question.AnswerOptions.Add(new InMemoryAnswerOption {
                                        Id = optionId,
                                        Text = o.Text,
                                        IsCorrect = o.IsCorrect
                                    });
                                }
                            }

                            test.Questions.Add(question);
                        }
                    }
                }
                else if (test != null)
                {
                    // If CategoryQuestionsId is not set, the test doesn't have any questions
                    // Initialize an empty collection
                    test.Questions = new List<InMemoryQuestion>();
                    _logger.LogWarning("Test {TestId} does not have CategoryQuestionsId set. No questions will be displayed.", id);
                }

                if (test == null)
                {
                    return NotFound("The test you're looking for doesn't exist.");
                }

                // Check if the test is deleted if the IsDeleted property exists
                try
                {
                    if (test.IsDeleted)
                    {
                        return NotFound("The test you're looking for has been deleted.");
                    }
                }
                catch
                {
                    // IsDeleted property might not exist yet, ignore the error
                }

                // Check if the test is schedule-restricted
                if (test.IsScheduleRestricted && test.ScheduledStartTime.HasValue && test.ScheduledEndTime.HasValue)
                {
                    // Convert test times to IST using our helper
                    DateTime? testStartTimeIST = test.ScheduledStartTime.HasValue
                        ? Utilities.TimeZoneHelper.ToIst(test.ScheduledStartTime.Value)
                        : null;

                    DateTime? testEndTimeIST = test.ScheduledEndTime.HasValue
                        ? Utilities.TimeZoneHelper.ToIst(test.ScheduledEndTime.Value)
                        : null;

                    // Get current time in IST
                    var nowIST = Utilities.TimeZoneHelper.GetCurrentIstTime();
                    var isTestTime = testStartTimeIST.HasValue && testEndTimeIST.HasValue &&
                                    nowIST >= testStartTimeIST && nowIST <= testEndTimeIST;

                    // Check if the user has booked this test
                    string userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (string.IsNullOrEmpty(userIdStr))
                    {
                        return RedirectToAction("Login", "Account");
                    }

                    int candidateIdValue = int.Parse(userIdStr);
                    var hasBookedSlot = await _context.TestBookings
                        .AnyAsync(tb => tb.TestId == id && tb.UserId == candidateIdValue);

                    if (!hasBookedSlot)
                    {
                        ViewBag.ErrorMessage = "You need to book a slot for this test before you can take it.";
                        return View("TestNotAvailable", test);
                    }

                    // Check if the user has just paid (coming from payment flow)
                    bool justPaid = TempData["JustPaid"] != null;

                    // If the user has just paid, set a success message
                    if (justPaid)
                    {
                        ViewBag.SuccessMessage = "Payment successful! You can now take your test.";
                        // Keep the JustPaid flag for the next request
                        TempData.Keep("JustPaid");
                    }

                    // Never allow users to take tests before the scheduled time, even if they just paid
                    if (!isTestTime)
                    {
                        if (nowIST < testStartTimeIST)
                        {
                            ViewBag.ErrorMessage = $"This test is not available yet. It will be available from {Utilities.TimeZoneHelper.FormatIstDateTime(testStartTimeIST)} IST to {Utilities.TimeZoneHelper.FormatIstDateTime(testEndTimeIST)} IST.";
                        }
                        else
                        {
                            ViewBag.ErrorMessage = $"This test is no longer available. It was available from {Utilities.TimeZoneHelper.FormatIstDateTime(testStartTimeIST)} IST to {Utilities.TimeZoneHelper.FormatIstDateTime(testEndTimeIST)} IST.";
                        }

                        ViewBag.ScheduledStartTime = testStartTimeIST;
                        ViewBag.ScheduledEndTime = testEndTimeIST;
                        ViewBag.TimeZone = "IST";
                        return View("TestNotAvailable", test);
                    }
                }

                return View(test);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error taking test: {ErrorMessage}", ex.Message);
                return StatusCode(500, new { message = "Error taking test: " + ex.Message });
            }
        }

        [HttpGet]
        [Route("Test/view-uploads")]
        [Authorize(Roles = "Organization")]
        public IActionResult ViewUploads()
        {
            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var files = Directory.GetFiles(uploadsFolder)
                .Select(f => new FileInfo(f))
                .Select(f => new
                {
                    Name = f.Name,
                    Size = f.Length,
                    LastModified = f.LastWriteTime,
                    Path = $"/uploads/{f.Name}"
                })
                .ToList();

            return View(files);
        }

        // View action for showing a scheduled test
        [HttpGet]
        [Route("Test/ScheduledTest/{id}")]
        [Authorize(Roles = "Candidate")]
        public async Task<IActionResult> ScheduledTest(int id, string message = null)
        {
            try
            {
                var test = await _context.Tests.FindAsync(id);
                if (test == null)
                {
                    return RedirectToAction("Index", new { error = "Test not found" });
                }

                // Pass any messages to the view
                if (!string.IsNullOrEmpty(message))
                {
                    ViewBag.SuccessMessage = message;
                }

                // Check if the user has a booking for this test
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return RedirectToAction("Login", "Auth");
                }

                var candidateId = int.Parse(userId);
                var booking = await _context.TestBookings
                    .FirstOrDefaultAsync(tb => tb.TestId == id && tb.UserId == candidateId);

                if (booking == null)
                {
                    return RedirectToAction("Index", new { error = "You haven't booked this test yet. Please book a slot first." });
                }

                // Check if the user has already taken this test
                var username = User.Identity?.Name;
                var testResult = await _context.TestResults
                    .FirstOrDefaultAsync(tr => tr.TestId == id && tr.Username == username);

                // Check if this is a retake attempt
                bool isRetake = Request.Query.ContainsKey("retake") && Request.Query["retake"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);

                if (testResult != null && !isRetake)
                {
                    // User has already taken this test, redirect to the result page
                    _logger.LogInformation($"User {username} has already taken test {id}, redirecting to result {testResult.Id}");
                    return RedirectToAction("Result", new { id = testResult.Id, message = "You have already taken this test. Here are your results." });
                }

                if (isRetake)
                {
                    _logger.LogInformation($"User {username} is retaking test {id}");
                    ViewBag.IsRetake = true;
                }

                // Check if the user has just paid (coming from payment flow)
                bool justPaid = TempData["JustPaid"] != null;
                if (justPaid)
                {
                    ViewBag.SuccessMessage = "Payment successful! You can now start your test during your scheduled time slot.";
                    ViewBag.JustPaid = true;
                    // Keep the JustPaid flag for the next request
                    TempData.Keep("JustPaid");
                }

                // Get the booking details
                var bookingDate = booking.BookingDate.Date;
                var startTime = booking.StartTime;
                var endTime = booking.EndTime;
                var slotNumber = booking.SlotNumber;
                var now = DateTime.Now;
                var currentDate = now.Date;

                // Set the booking details for the view
                // Use the exact booking date without timezone conversion
                ViewBag.BookedDate = bookingDate.ToString("dddd, MMMM d, yyyy");
                ViewBag.BookedStartTime = Utilities.TimeZoneHelper.ToIst(startTime).ToString("hh:mm tt");
                ViewBag.BookedEndTime = Utilities.TimeZoneHelper.ToIst(endTime).ToString("hh:mm tt");
                ViewBag.BookedSlotNumber = slotNumber;
                ViewBag.UserSapId = booking.UserSapId;

                // Get the slot information based on the slot number
                string slotDisplayTime = "";
                switch (slotNumber)
                {
                    case 1:
                        slotDisplayTime = "9:00 AM - 11:00 AM";
                        break;
                    case 2:
                        slotDisplayTime = "12:00 PM - 2:00 PM";
                        break;
                    case 3:
                        slotDisplayTime = "3:00 PM - 5:00 PM";
                        break;
                    case 4:
                        slotDisplayTime = "6:00 PM - 8:00 PM";
                        break;
                    default:
                        slotDisplayTime = $"{Utilities.TimeZoneHelper.ToIst(startTime).ToString("hh:mm tt")} - {Utilities.TimeZoneHelper.ToIst(endTime).ToString("hh:mm tt")}";
                        break;
                }
                ViewBag.SlotDisplayTime = slotDisplayTime;

                // Since we're now storing times in IST, we can use them directly
                DateTime bookingStartDateTime = booking.StartTime;
                DateTime bookingEndDateTime = booking.EndTime;

                // Determine if the test is in the future, current, or past
                ViewBag.IsFutureTest = now < bookingStartDateTime;
                ViewBag.IsExpired = now > bookingEndDateTime;

                // Check if the user just booked this test
                bool justBooked = TempData["TestBooked"] != null && TempData["BookedTestId"] != null && (int)TempData["BookedTestId"] == id;

                // Even if the user just booked the test, enforce time restrictions
                if (justBooked)
                {
                    _logger.LogInformation($"User just booked test ID: {test.Id}. Still enforcing time restrictions.");
                    ViewBag.JustBooked = true;
                    // Do not set ViewBag.IsTestTime = true here, let the normal time checks below handle it
                }
                // Otherwise, apply the normal time restrictions
                else if (ViewBag.IsFutureTest)
                {
                    _logger.LogInformation($"Test is in the future. Setting status to 'Upcoming' for test ID: {test.Id}");
                    ViewBag.IsTestTime = false; // Explicitly set IsTestTime to false for future tests
                }
                else if (ViewBag.IsExpired)
                {
                    _logger.LogInformation($"Test is expired. Setting status to 'Expired' for test ID: {test.Id}");
                    ViewBag.IsTestTime = false; // Explicitly set IsTestTime to false for expired tests
                }
                else
                {
                    // Use the bookingStartDateTime and bookingEndDateTime we created above
                    ViewBag.IsTestTime = now >= bookingStartDateTime && now <= bookingEndDateTime;
                    _logger.LogInformation($"Test is scheduled for today. Current time: {now}, Start time: {bookingStartDateTime}, End time: {bookingEndDateTime}, IsTestTime: {ViewBag.IsTestTime} for test ID: {test.Id}");
                }

                // If the test is in the future, set the target date for the countdown
                if (ViewBag.IsFutureTest)
                {
                    // Use the startTime variable which is already safely set
                    DateTime targetDate = startTime;
                    ViewBag.TargetDateString = targetDate.ToString("yyyy-MM-ddTHH:mm:ss");

                    // Calculate time remaining
                    TimeSpan timeRemaining = startTime - now;
                    ViewBag.DaysRemaining = timeRemaining.Days;
                    ViewBag.HoursRemaining = timeRemaining.Hours;
                    ViewBag.MinutesRemaining = timeRemaining.Minutes;
                    ViewBag.SecondsRemaining = timeRemaining.Seconds;

                    // Format a human-readable time remaining message
                    string timeRemainingMessage = "";
                    if (timeRemaining.Days > 0)
                    {
                        timeRemainingMessage += $"{timeRemaining.Days} day{(timeRemaining.Days != 1 ? "s" : "")} ";
                    }
                    if (timeRemaining.Hours > 0)
                    {
                        timeRemainingMessage += $"{timeRemaining.Hours} hour{(timeRemaining.Hours != 1 ? "s" : "")} ";
                    }
                    if (timeRemaining.Minutes > 0)
                    {
                        timeRemainingMessage += $"{timeRemaining.Minutes} minute{(timeRemaining.Minutes != 1 ? "s" : "")} ";
                    }
                    timeRemainingMessage += $"until your scheduled test time";
                    ViewBag.TimeRemainingMessage = timeRemainingMessage;
                }

                return View(test);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ScheduledTest action");
                return RedirectToAction("Index", new { error = "An error occurred while loading your scheduled test" });
            }
        }

        // Upload questions functionality has been removed in favor of using category questions only

        // View action for starting a test based on scheduled slot
        [HttpGet]
        [Route("Test/Start/{id}")]
        [Authorize(Roles = "Candidate")]
        public async Task<IActionResult> Start(int id)
        {
            // Redirect to the Take action which has all the time validation logic
            return RedirectToAction("Take", new { id });
        }

        // Legacy Start action implementation - removed and replaced with redirect to Take

        // View action for booking a test slot
        [HttpGet]
        [Route("Test/BookSlot/{id}")]
        [Authorize(Roles = "Candidate")]
        public async Task<IActionResult> BookSlot(int id, bool isReattempt = false)
        {
            try
            {
                var test = await _context.Tests.FindAsync(id);
                if (test == null)
                {
                    return RedirectToAction("Index", new { error = "Test not found" });
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

                // Only check for existing booking if this is not a reattempt
                if (existingBooking != null && !isReattempt)
                {
                    return RedirectToAction("Index", new { error = "You have already booked this test" });
                }

                // Pass any error or message from query parameters to the view
                if (!string.IsNullOrEmpty(Request.Query["error"]))
                {
                    ViewBag.Error = Request.Query["error"];
                }

                if (!string.IsNullOrEmpty(Request.Query["message"]))
                {
                    ViewBag.Message = Request.Query["message"];
                }

                // Pass the isReattempt flag to the view
                ViewBag.IsReattempt = isReattempt;

                // If this is a reattempt, get the number of previous attempts
                if (isReattempt)
                {
                    var username = User.Identity?.Name;
                    var attemptCount = await _context.TestResults
                        .CountAsync(tr => tr.TestId == id && tr.Username == username);
                    ViewBag.AttemptCount = attemptCount;
                }

                return View(test);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in BookSlot action");
                return RedirectToAction("Index", new { error = "An error occurred while loading the booking page" });
            }
        }

        // Process the booking with fixed slot selection
        [HttpPost]
        [Route("Test/ProcessBooking/{id}")]
        [Authorize(Roles = "Candidate")]
        public async Task<IActionResult> ProcessBooking(int id, string selectedDate, string selectedSlot, string selectedStartTime, string selectedEndTime, bool isReattempt = false)
        {
            try
            {
                var test = await _context.Tests.FindAsync(id);
                if (test == null)
                {
                    return RedirectToAction("Index", new { error = "Test not found" });
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

                // Only check for existing booking if this is not a reattempt
                if (existingBooking != null && !isReattempt)
                {
                    return RedirectToAction("Index", new { error = "You have already booked this test" });
                }

                // Parse the selected date
                if (!DateTime.TryParse(selectedDate, out DateTime bookingDate))
                {
                    return RedirectToAction("BookSlot", new { id, error = "Invalid date selected" });
                }

                // CRITICAL FIX: Ensure we're using the date exactly as selected without any timezone conversion
                // Do NOT modify the date in any way - keep it exactly as parsed
                _logger.LogInformation($"Selected booking date (no timezone conversion): {bookingDate:yyyy-MM-dd}");

                // Validate that the booking date is not more than 7 days in the future
                if (bookingDate.Date > DateTime.Today.AddDays(7))
                {
                    return RedirectToAction("BookSlot", new { id, error = "You can only book a test up to 7 days in advance" });
                }

                // Parse the selected slot number
                if (!int.TryParse(selectedSlot, out int slotNumber) || slotNumber < 1 || slotNumber > 4)
                {
                    return RedirectToAction("BookSlot", new { id, error = "Invalid slot selected" });
                }

                // Get the time range for the selected slot
                DateTime startTime, endTime;
                switch (slotNumber)
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
                    default:
                        return RedirectToAction("BookSlot", new { id, error = "Invalid slot selected" });
                }

                // Check if the slot is available (count bookings for this slot)
                int slotBookings;
                try
                {
                    slotBookings = await _context.TestBookings
                        .Where(tb => tb.TestId == id &&
                               tb.BookingDate.Date == bookingDate.Date &&
                               tb.SlotNumber == slotNumber)
                        .CountAsync();
                }
                catch (Exception)
                {
                    // Fallback to time-based filtering if SlotNumber doesn't exist
                    _logger.LogWarning("SlotNumber field not found in TestBookings table. Using time-based filtering.");
                    slotBookings = await _context.TestBookings
                        .Where(tb => tb.TestId == id &&
                               tb.BookingDate.Date == bookingDate.Date &&
                               tb.StartTime >= startTime && tb.StartTime < endTime)
                        .CountAsync();
                }

                const int maxUsersPerSlot = 200; // Max 200 users per slot
                if (slotBookings >= maxUsersPerSlot)
                {
                    return RedirectToAction("BookSlot", new { id, error = "This slot is already full. Please select a different slot." });
                }

                // Get the user's SAP ID
                string? userSapId = null;
                var user = await _context.Users.FindAsync(candidateId);
                if (user != null && !string.IsNullOrEmpty(user.SapId))
                {
                    userSapId = user.SapId;
                    _logger.LogInformation("User {UserId} has SAP ID: {SapId}", candidateId, userSapId);
                }
                else
                {
                    _logger.LogWarning("User {UserId} does not have a SAP ID", candidateId);
                }

                // Store booking details in Session for PayUInitiate
                HttpContext.Session.SetInt32("PendingTestId", id);
                HttpContext.Session.SetString("PendingDate", bookingDate.ToString("yyyy-MM-dd"));
                HttpContext.Session.SetInt32("PendingSlotNumber", slotNumber);
                HttpContext.Session.SetString("PendingStartTime", selectedStartTime);
                HttpContext.Session.SetString("PendingEndTime", selectedEndTime);
                if (!string.IsNullOrEmpty(userSapId))
                    HttpContext.Session.SetString("PendingUserSapId", userSapId);
                HttpContext.Session.SetInt32("PendingUserId", candidateId);
                HttpContext.Session.SetString("IsReattempt", isReattempt.ToString());

                // Redirect to payment page
                return RedirectToAction("PayUInitiate", "Payment");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProcessBooking action");
                return RedirectToAction("BookSlot", new { id, error = "An error occurred while processing your booking" });
            }
        }

        // Check slot availability
        [HttpGet]
        [Route("Test/CheckSlotAvailability")]
        [Authorize(Roles = "Candidate")]
        public async Task<IActionResult> CheckSlotAvailability(int testId, string date, int slotNumber)
        {
            try
            {
                if (!DateTime.TryParse(date, out DateTime bookingDate))
                {
                    return Ok(new { isAvailable = false, message = "Invalid date format", currentCount = 0, maxCount = 200 });
                }

                // CRITICAL FIX: Do NOT modify the booking date in any way
                // Just log it for debugging purposes
                _logger.LogInformation($"Checking slot availability for date: {bookingDate:yyyy-MM-dd}");

                if (slotNumber < 1 || slotNumber > 4)
                {
                    return Ok(new { isAvailable = false, message = "Invalid slot number", currentCount = 0, maxCount = 200 });
                }

                // Get the time range for the selected slot
                DateTime startTime, endTime;
                switch (slotNumber)
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
                    default:
                        return Ok(new { isAvailable = false, message = "Invalid slot number", currentCount = 0, maxCount = 200 });
                }

                // Count bookings for this test, date, and slot
                // Try to use SlotNumber if it exists, otherwise use time-based filtering
                int slotBookings;
                try
                {
                    slotBookings = await _context.TestBookings
                        .Where(tb => tb.TestId == testId &&
                               tb.BookingDate.Date == bookingDate.Date &&
                               tb.SlotNumber == slotNumber)
                        .CountAsync();
                }
                catch (Exception)
                {
                    // Fallback to time-based filtering if SlotNumber doesn't exist
                    _logger.LogWarning("SlotNumber field not found in TestBookings table. Using time-based filtering.");
                    slotBookings = await _context.TestBookings
                        .Where(tb => tb.TestId == testId &&
                               tb.BookingDate.Date == bookingDate.Date &&
                               tb.StartTime >= startTime && tb.StartTime < endTime)
                        .CountAsync();
                }

                const int maxUsersPerSlot = 200; // Max 200 users per slot
                bool isAvailable = slotBookings < maxUsersPerSlot;

                return Ok(new { isAvailable, currentCount = slotBookings, maxCount = maxUsersPerSlot });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking slot availability");
                return Ok(new { isAvailable = false, message = "Error checking availability", currentCount = 0, maxCount = 200 });
            }
        }

        // Legacy method - kept for backward compatibility
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

                // CRITICAL FIX: Do NOT modify the booking date in any way
                // Just log it for debugging purposes
                _logger.LogInformation($"Checking time availability for date: {bookingDate:yyyy-MM-dd}");

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

        [HttpDelete]
        [Route("Test/delete/{id}")]
        [Authorize(Roles = "Organization")]
        public async Task<IActionResult> DeleteTest(int id)
        {
            try
            {
                // Check if user is organization
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userId == null)
                {
                    return Json(new { success = false, message = "Unauthorized: Organization access required." });
                }

                var organizationId = int.Parse(userId);

                // Get test with related data
                var test = await _context.Tests
                    .FirstOrDefaultAsync(t => t.Id == id);

                if (test == null)
                {
                    return Json(new { success = false, message = "Test not found." });
                }

                // Verify ownership - only allow organizations to delete their own tests
                if (test.CreatedBy != organizationId)
                {
                    return Json(new { success = false, message = "You can only delete tests that you created." });
                }

                // Begin transaction
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    // Delete all related data

                    // Delete test bookings using raw SQL to avoid issues with schema changes
                    try
                    {
                        // Use raw SQL to delete test bookings to avoid Entity Framework issues with schema changes
                        var deleteBookingsCommand = $"DELETE FROM testbookings WHERE TestId = {id}";
                        await _context.Database.ExecuteSqlRawAsync(deleteBookingsCommand);
                        _logger.LogInformation($"Test bookings for test {id} deleted successfully using raw SQL");

                        // Reset the current user count since we're removing all bookings
                        test.CurrentUserCount = 0;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error deleting test bookings for test {id} using raw SQL. Trying alternative method...");

                        try
                        {
                            // Fallback to Entity Framework if raw SQL fails
                            var testBookings = await _context.TestBookings
                                .Where(tb => tb.TestId == id)
                                .ToListAsync();

                            if (testBookings.Any())
                            {
                                _context.TestBookings.RemoveRange(testBookings);
                                _logger.LogInformation($"Test bookings for test {id} deleted successfully using Entity Framework");

                                // Reset the current user count since we're removing all bookings
                                test.CurrentUserCount = 0;
                            }
                        }
                        catch (Exception innerEx)
                        {
                            _logger.LogError(innerEx, $"Both methods failed to delete test bookings for test {id}. Continuing with other deletions...");
                            // Continue with the rest of the deletion process
                        }
                    }

                    // Delete test results using raw SQL to avoid issues with schema changes
                    try
                    {
                        // Use raw SQL to delete test results to avoid Entity Framework issues with schema changes
                        var deleteResultsCommand = $"DELETE FROM testresult WHERE TestId = {id}";
                        await _context.Database.ExecuteSqlRawAsync(deleteResultsCommand);
                        _logger.LogInformation($"Test results for test {id} deleted successfully using raw SQL");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error deleting test results for test {id} using raw SQL. Trying alternative method...");

                        try
                        {
                            // Fallback to Entity Framework if raw SQL fails
                            var testResults = await _context.TestResults
                                .Where(tr => tr.TestId == id)
                                .ToListAsync();

                            if (testResults.Any())
                            {
                                _context.TestResults.RemoveRange(testResults);
                                _logger.LogInformation($"Test results for test {id} deleted successfully using Entity Framework");
                            }
                        }
                        catch (Exception innerEx)
                        {
                            _logger.LogError(innerEx, $"Both methods failed to delete test results for test {id}. Continuing with other deletions...");
                            // Continue with the rest of the deletion process
                        }
                    }

                    // Questions and answer options are now stored in CategoryQuestions table
                    // No need to delete them separately

                    // Soft delete the test
                    test.IsDeleted = true;
                    test.DeletedAt = DateTime.UtcNow;
                    _context.Tests.Update(test);

                    // Save changes
                    await _context.SaveChangesAsync();

                    // Commit transaction
                    await transaction.CommitAsync();

                    return Json(new { success = true, message = "Test deleted successfully! It will no longer be visible to candidates." });
                }
                catch (Exception ex)
                {
                    // Rollback transaction on error
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, $"Error deleting test {id} and its related data");
                    return Json(new { success = false, message = "An error occurred while deleting the test and its related data." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in DeleteTest action for test {id}");
                return Json(new { success = false, message = "An unexpected error occurred while deleting the test." });
            }
        }

        // Debug action to help diagnose booking issues
        [HttpGet]
        [Route("Test/DebugBookings")]
        [Authorize(Roles = "Candidate")]
        public async Task<IActionResult> DebugBookings()
        {
            try
            {
                // Get the current user ID
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return RedirectToAction("Login", "Auth");
                }

                var candidateId = int.Parse(userId);

                // Get all bookings for this user - use AsNoTracking for fresh data
                var bookings = await _context.TestBookings
                    .AsNoTracking()
                    .Include(tb => tb.Test)
                    .Where(tb => tb.UserId == candidateId)
                    .OrderBy(tb => tb.BookingDate)
                    .ThenBy(tb => tb.StartTime)
                    .ToListAsync();

                // Get the current date and time
                var now = DateTime.Now;
                _logger.LogInformation($"Current time: {now}");

                // Process each booking to determine its category
                foreach (var booking in bookings)
                {
                    // Log the booking details for debugging
                    _logger.LogInformation($"Processing booking: ID={booking.Id}, TestId={booking.TestId}, BookingDate={booking.BookingDate:yyyy-MM-dd}, StartTime={booking.StartTime:yyyy-MM-dd HH:mm:ss}, EndTime={booking.EndTime:yyyy-MM-dd HH:mm:ss}");

                    // Create proper DateTime objects for comparison using the actual slot times
                    // Get the time range for the selected slot based on SlotNumber
                    DateTime bookingStartDateTime, bookingEndDateTime;

                    switch (booking.SlotNumber)
                    {
                        case 1: // 9:00 AM - 11:00 AM
                            bookingStartDateTime = booking.BookingDate.AddHours(9);
                            bookingEndDateTime = booking.BookingDate.AddHours(11);
                            break;
                        case 2: // 12:00 PM - 2:00 PM
                            bookingStartDateTime = booking.BookingDate.AddHours(12);
                            bookingEndDateTime = booking.BookingDate.AddHours(14);
                            break;
                        case 3: // 3:00 PM - 5:00 PM
                            bookingStartDateTime = booking.BookingDate.AddHours(15);
                            bookingEndDateTime = booking.BookingDate.AddHours(17);
                            break;
                        case 4: // 6:00 PM - 8:00 PM
                            bookingStartDateTime = booking.BookingDate.AddHours(18);
                            bookingEndDateTime = booking.BookingDate.AddHours(20);
                            break;
                        default:
                            // Fallback to using the stored times if slot number is invalid
                            bookingStartDateTime = booking.BookingDate.Add(booking.StartTime.TimeOfDay);
                            bookingEndDateTime = booking.BookingDate.Add(booking.EndTime.TimeOfDay);
                            break;
                    }

                    _logger.LogInformation($"Calculated times: StartDateTime={bookingStartDateTime:yyyy-MM-dd HH:mm:ss}, EndDateTime={bookingEndDateTime:yyyy-MM-dd HH:mm:ss}");
                    _logger.LogInformation($"Current time: {now:yyyy-MM-dd HH:mm:ss}");
                    _logger.LogInformation($"Is future test? {now < bookingStartDateTime}");
                    _logger.LogInformation($"Is current test? {now >= bookingStartDateTime && now <= bookingEndDateTime}");
                    _logger.LogInformation($"Is past test? {now > bookingEndDateTime}");

                    // Explicitly compare year, month, day, hour, minute, second for debugging
                    bool isFutureYear = bookingStartDateTime.Year > now.Year;
                    bool isSameYear = bookingStartDateTime.Year == now.Year;
                    bool isFutureMonth = isSameYear && bookingStartDateTime.Month > now.Month;
                    bool isSameMonth = isSameYear && bookingStartDateTime.Month == now.Month;
                    bool isFutureDay = isSameMonth && bookingStartDateTime.Day > now.Day;
                    bool isSameDay = isSameMonth && bookingStartDateTime.Day == now.Day;
                    bool isFutureHour = isSameDay && bookingStartDateTime.Hour > now.Hour;
                    bool isSameHour = isSameDay && bookingStartDateTime.Hour == now.Hour;
                    bool isFutureMinute = isSameHour && bookingStartDateTime.Minute > now.Minute;
                    bool isSameMinute = isSameHour && bookingStartDateTime.Minute == now.Minute;
                    bool isFutureSecond = isSameMinute && bookingStartDateTime.Second > now.Second;

                    _logger.LogInformation($"Detailed comparison: isFutureYear={isFutureYear}, isSameYear={isSameYear}, isFutureMonth={isFutureMonth}, isSameMonth={isSameMonth}, isFutureDay={isFutureDay}, isSameDay={isSameDay}, isFutureHour={isFutureHour}, isSameHour={isSameHour}, isFutureMinute={isFutureMinute}, isSameMinute={isSameMinute}, isFutureSecond={isFutureSecond}");

                    // Force the booking to be in the correct category based on the date
                    // First check if the year is in the future
                    bool isUpcoming = false;
                    if (booking.BookingDate.Year > now.Year)
                    {
                        isUpcoming = true;
                        _logger.LogInformation($"Booking {booking.Id} is in a future year: {booking.BookingDate.Year} > {now.Year}");
                    }
                    // If same year, check if month is in the future
                    else if (booking.BookingDate.Year == now.Year && booking.BookingDate.Month > now.Month)
                    {
                        isUpcoming = true;
                        _logger.LogInformation($"Booking {booking.Id} is in a future month: {booking.BookingDate.Month} > {now.Month}");
                    }
                    // If same year and month, check if day is in the future
                    else if (booking.BookingDate.Year == now.Year && booking.BookingDate.Month == now.Month && booking.BookingDate.Day > now.Day)
                    {
                        isUpcoming = true;
                        _logger.LogInformation($"Booking {booking.Id} is in a future day: {booking.BookingDate.Day} > {now.Day}");
                    }
                    // If same day, check if the start time is in the future
                    else if (booking.BookingDate.Year == now.Year && booking.BookingDate.Month == now.Month && booking.BookingDate.Day == now.Day)
                    {
                        // Create a DateTime for the start time today
                        var startTimeToday = new DateTime(now.Year, now.Month, now.Day, booking.StartTime.Hour, booking.StartTime.Minute, booking.StartTime.Second);
                        if (startTimeToday > now)
                        {
                            isUpcoming = true;
                            _logger.LogInformation($"Booking {booking.Id} is later today: {startTimeToday:HH:mm:ss} > {now:HH:mm:ss}");
                        }
                    }

                    // If not upcoming, check if it's in the past
                    bool isPast = false;
                    if (!isUpcoming)
                    {
                        // Check if the booking is in the past
                        if (booking.BookingDate.Year < now.Year ||
                            (booking.BookingDate.Year == now.Year && booking.BookingDate.Month < now.Month) ||
                            (booking.BookingDate.Year == now.Year && booking.BookingDate.Month == now.Month && booking.BookingDate.Day < now.Day) ||
                            (booking.BookingDate.Year == now.Year && booking.BookingDate.Month == now.Month && booking.BookingDate.Day == now.Day &&
                             booking.EndTime.Hour < now.Hour) ||
                            (booking.BookingDate.Year == now.Year && booking.BookingDate.Month == now.Month && booking.BookingDate.Day == now.Day &&
                             booking.EndTime.Hour == now.Hour && booking.EndTime.Minute < now.Minute))
                        {
                            isPast = true;
                            _logger.LogInformation($"Booking {booking.Id} is in the past");
                        }
                    }

                    bool isCurrent = !isUpcoming && !isPast;

                    _logger.LogInformation($"Final categorization: isUpcoming={isUpcoming}, isCurrent={isCurrent}, isPast={isPast}");

                    // Categorize the booking
                    if (isUpcoming)
                    {
                        // Upcoming: current time is before the start time
                        _logger.LogInformation($"Booking {booking.Id} categorized as UPCOMING");
                        booking.CanStartTest = false;
                    }
                    else if (isCurrent)
                    {
                        // Current: current time is within the time slot
                        _logger.LogInformation($"Booking {booking.Id} categorized as CURRENT");
                        booking.CanStartTest = true;
                    }
                    else
                    {
                        // Past: current time is after the end time
                        _logger.LogInformation($"Booking {booking.Id} categorized as PAST");
                        booking.CanStartTest = false;
                    }
                }

                // Pass the data to the view
                ViewBag.AllBookings = bookings;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DebugBookings action");
                return RedirectToAction("Index", new { error = "An error occurred while loading your bookings" });
            }
        }

        // Create a reusable JsonSerializerOptions instance
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions {
            ReferenceHandler = ReferenceHandler.Preserve,
            MaxDepth = 64
        };

        [HttpPost]
        [Route("Test/Submit/{id}")]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(int id, [FromBody] object data)
        {
            try
            {
                _logger.LogInformation("Received test submission for test ID: {TestId} from user: {Username}", id, User.Identity?.Name ?? "Anonymous");

                // Check if the data is a dictionary with a security violation or auto-submission flag
                bool isSecurityViolation = false;
                bool isAutoSubmitting = false;
                Dictionary<string, string> answers = new Dictionary<string, string>();

                if (data is JsonElement jsonElement)
                {
                    // Extract answers and check for security violation and auto-submission flags
                    foreach (var property in jsonElement.EnumerateObject())
                    {
                        if (property.Name == "isSecurityViolation" && property.Value.ValueKind == JsonValueKind.True)
                        {
                            isSecurityViolation = true;
                            _logger.LogWarning("Security violation detected in test submission for test ID: {TestId}", id);
                        }
                        else if (property.Name == "isAutoSubmitting" && property.Value.ValueKind == JsonValueKind.True)
                        {
                            isAutoSubmitting = true;
                            _logger.LogInformation("Auto-submission detected for test ID: {TestId}", id);
                        }
                        else if (property.Name.StartsWith("question_"))
                        {
                            answers[property.Name] = property.Value.GetString() ?? "";
                        }
                    }
                }
                else if (data is Dictionary<string, string> answerDict)
                {
                    answers = answerDict;
                }

                _logger.LogInformation("Received {Count} answers", answers?.Count ?? 0);

                if (answers == null || answers.Count == 0)
                {
                    _logger.LogWarning("No answers provided for test ID: {TestId}", id);
                    if (isSecurityViolation || isAutoSubmitting)
                    {
                        // For security violations or auto-submissions, continue with empty answers
                        answers = new Dictionary<string, string>();
                        _logger.LogWarning("Continuing with empty answers due to {Reason}",
                            isSecurityViolation ? "security violation" : "auto-submission");
                    }
                    else
                    {
                        return BadRequest(new { success = false, message = "No answers provided" });
                    }
                }

                // Get the test without loading questions
                var test = await _context.Tests
                    .FirstOrDefaultAsync(t => t.Id == id);

                // Load questions either from the Questions table or from CategoryQuestions
                if (test != null && test.CategoryQuestionsId.HasValue)
                {
                    // Load questions from CategoryQuestions
                    var categoryQuestions = await _context.CategoryQuestions
                        .FirstOrDefaultAsync(cq => cq.Id == test.CategoryQuestionsId);

                    if (categoryQuestions != null)
                    {
                        // Deserialize questions from JSON using the shared options
                        var allQuestions = JsonSerializer.Deserialize<List<QuestionDto>>(categoryQuestions.QuestionsJson, _jsonOptions);

                        // Randomly select questions if needed
                        var random = new Random();
                        var selectedQuestions = allQuestions != null
                            ? allQuestions
                                .OrderBy(q => random.Next())
                                .Take(Math.Min(test.QuestionCount, allQuestions.Count))
                                .ToList()
                            : new List<QuestionDto>();

                        // Create a dictionary to map question IDs to their answers
                        var questionAnswers = new Dictionary<int, string>();
                        foreach (var key in answers.Keys)
                        {
                            if (key.StartsWith("question_") && int.TryParse(key.Substring(9), out int questionId))
                            {
                                questionAnswers[questionId] = answers[key];
                            }
                        }

                        // Convert QuestionDto to InMemoryQuestion objects for the view
                        test.Questions = new List<InMemoryQuestion>();
                        for (int i = 0; i < selectedQuestions.Count; i++)
                        {
                            var q = selectedQuestions[i];
                            var questionId = 10000 + i; // Use the same ID generation logic as in Take method

                            var question = new InMemoryQuestion {
                                Id = questionId,
                                Text = q.Text,
                                Title = q.Title ?? q.Text.Substring(0, Math.Min(q.Text.Length, 100)),
                                Type = q.Type,
                                TestId = test.Id,
                                AnswerOptions = new List<InMemoryAnswerOption>()
                            };

                            // Add answer options
                            if (q.AnswerOptions != null)
                            {
                                for (int j = 0; j < q.AnswerOptions.Count; j++)
                                {
                                    var o = q.AnswerOptions[j];
                                    var optionId = 100000 + (i * 100) + j; // Use the same ID generation logic as in Take method

                                    question.AnswerOptions.Add(new InMemoryAnswerOption {
                                        Id = optionId,
                                        Text = o.Text,
                                        IsCorrect = o.IsCorrect
                                    });
                                }
                            }

                            test.Questions.Add(question);
                        }
                    }
                }
                else if (test != null)
                {
                    // If CategoryQuestionsId is not set, the test doesn't have any questions
                    // Initialize an empty collection
                    test.Questions = new List<InMemoryQuestion>();
                    _logger.LogWarning("Test {TestId} does not have CategoryQuestionsId set. No questions will be evaluated.", id);
                }

                if (test == null)
                {
                    _logger.LogWarning("Test not found with ID: {TestId}", id);
                    return NotFound(new { success = false, message = "Test not found" });
                }

                // Check if user has already submitted this test
                var username = User.Identity?.Name ?? "Anonymous";
                var existingSubmissions = await _context.TestResults
                    .Where(r => r.TestId == id && r.Username == username)
                    .CountAsync();

                // Log the number of existing submissions but don't limit attempts
                _logger.LogInformation("User {Username} is submitting test {TestId}. Previous attempts: {AttemptCount}", username, id, existingSubmissions);

                int mcqCorrect = 0;
                int totalMcq = test.Questions.Count;
                var evaluationDetails = new List<string>();

                // Evaluate MCQ questions
                foreach (var question in test.Questions.Where(q => q.Type == QuestionType.MultipleChoice))
                {
                    var questionNumber = test.Questions.ToList().IndexOf(question) + 1;
                    var selectedOptionId = answers.GetValueOrDefault($"question_{question.Id}");

                    if (selectedOptionId != null)
                    {
                        var selectedOption = question.AnswerOptions.FirstOrDefault(a => a.Id.ToString() == selectedOptionId);
                        var isCorrect = selectedOption != null && selectedOption.IsCorrect;
                        if (isCorrect)
                        {
                            mcqCorrect++;
                        }
                        evaluationDetails.Add($"MCQ {questionNumber}: Selected: {selectedOption?.Text ?? "None"} - Correct: {isCorrect}");
                    }
                    else
                    {
                        evaluationDetails.Add($"MCQ {questionNumber}: No answer selected");
                    }
                }

                // Calculate total score - now the total score is equal to the number of correct answers
                double totalScore = mcqCorrect;

                // Get the user's SAP ID
                string? sapId = null;
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(userId))
                {
                    var user = await _context.Users.FindAsync(int.Parse(userId));
                    if (user != null && !string.IsNullOrEmpty(user.SapId))
                    {
                        sapId = user.SapId;
                    }
                }

                // Get the test time information from the user's booking
                var (startTime, endTime) = await GetUserTestTime(id, username);

                var result = new TestResult
                {
                    TestId = id,
                    Username = username,
                    TotalQuestions = totalMcq,
                    CorrectAnswers = mcqCorrect,
                    Score = totalScore,
                    McqScore = totalScore, // Set McqScore to the same value as Score since we only have MCQ questions now
                    CodingScore = 0, // Set CodingScore to 0 since we don't have coding questions anymore
                    SubmittedAt = Utilities.TimeZoneHelper.GetCurrentIstTime(),
                    StartTime = startTime != DateTime.MinValue ? Utilities.TimeZoneHelper.ToIst(startTime) : null,
                    EndTime = endTime != DateTime.MinValue ? Utilities.TimeZoneHelper.ToIst(endTime) : null,
                    UserSapId = sapId // Add the SAP ID from the user
                };

                _context.TestResults.Add(result);

                        // No need to increment the user count here as it's already done when the booking is created

                // Log information about the test and organization
                if (test.CreatedBy.HasValue)
                {
                    var organizationId = test.CreatedBy.GetValueOrDefault();
                    _logger.LogInformation("Test created by organization ID: {OrganizationId}, test ID: {TestId}, username: {Username}", organizationId, id, username);

                    // We'll update the organization test results in the OrganizationResults action instead
                    // This avoids the foreign key constraint error
                    _logger.LogInformation("Skipping organization test result update to avoid foreign key constraint error");
                }

                await _context.SaveChangesAsync();

                return Ok(new {
                    success = true,
                    redirectUrl = $"/Test/Result/{result.Id}",
                    evaluationDetails = evaluationDetails,
                    score = totalScore,
                    mcqCorrect = mcqCorrect,
                    totalMcq = totalMcq
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting test {TestId}", id);
                return StatusCode(500, new { success = false, message = "Error submitting test: " + ex.Message });
            }
        }

        [HttpGet]
        [Route("Test/Result/{id}")]
        [Authorize]
        public async Task<IActionResult> Result(int id, string? message = null)
        {
            var result = await _context.TestResults
                .Include(r => r.Test)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (result == null)
            {
                return NotFound();
            }

            // Set a flag to indicate that the test has been completed
            // This will be used to prevent redirection loops
            TempData["TestCompleted"] = true;

            // Preserve the TestRecreated flag if it exists
            if (TempData.ContainsKey("TestRecreated"))
            {
                // Keep the flag for the view to use, but make sure it persists
                TempData.Keep("TestRecreated");
            }

            // If a message was passed, display it to the user
            if (!string.IsNullOrEmpty(message))
            {
                ViewBag.Message = message;
            }

            // Get the count of attempts for this test by this user
            var username = User.Identity?.Name;
            var attemptCount = await _context.TestResults
                .CountAsync(tr => tr.TestId == result.TestId && tr.Username == username);

            ViewBag.AttemptCount = attemptCount;

            // Log the result information
            _logger.LogInformation("Showing test result for test ID: {TestId}, result ID: {ResultId}, attempt {AttemptCount} of {MaxAttempts}",
                result.TestId, id, attemptCount, result.Test.MaxAttempts);

            return View(result);
        }

        // We only support MCQ questions

        [HttpPost]
        [Authorize(Roles = "Organization")]
        public async Task<IActionResult> Create([FromBody] Test test)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return Json(new { success = false, message = "Invalid test data" });
                }

                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

                if (userRole == "Organization" && userId != null)
                {
                    var organizationId = int.Parse(userId);
                    test.CreatedBy = organizationId;
                }
                else if (!User.IsInRole("Admin"))
                {
                    return Json(new { success = false, message = "Unauthorized" });
                }

                // Ensure the test is immediately visible to users
                test.HasUploadedFile = true;

                _context.Tests.Add(test);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Test created successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating test");
                return Json(new { success = false, message = "An error occurred while creating the test" });
            }
        }

        // Share functionality removed

        // Access functionality removed

        // StartShared functionality removed

        [HttpGet]
        [Route("Test/Details/{id}")]
        [Authorize(Roles = "Organization")]
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var test = await _context.Tests
                    .Include(t => t.Questions)
                        .ThenInclude(q => q.AnswerOptions)
                    .FirstOrDefaultAsync(t => t.Id == id);

                if (test == null)
                {
                    return NotFound("The test you're looking for doesn't exist.");
                }

                // Check if the test is deleted if the IsDeleted property exists
                try
                {
                    if (test.IsDeleted)
                    {
                        return NotFound("The test you're looking for has been deleted.");
                    }
                }
                catch
                {
                    // IsDeleted property might not exist yet, ignore the error
                }

                return View(test);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Test Details action");
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpGet]
        [Route("Test/Create")]
        [Authorize(Roles = "Organization")]
        public IActionResult Create()
        {
            try
            {
                // Always allow organizations to create tests
                ViewBag.CanCreateMcq = true;
                ViewBag.CanCreateCoding = true;


                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Create action");
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpGet]
        [Route("Test/ReAttempt/{id}")]
        [Authorize(Roles = "Candidate")]
        public async Task<IActionResult> ReAttempt(int id)
        {
            try
            {
                // Get the test
                var test = await _context.Tests.FindAsync(id);
                if (test == null)
                {
                    return RedirectToAction("Index", new { error = "Test not found" });
                }

                // Get the current user ID
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return RedirectToAction("Login", "Auth");
                }

                var candidateId = int.Parse(userId);
                var username = User.Identity?.Name;

                // Check if the user has already taken this test
                var testResults = await _context.TestResults
                    .Where(tr => tr.TestId == id && tr.Username == username)
                    .ToListAsync();

                // No limit on attempts as long as the user pays
                // Just log the number of previous attempts
                _logger.LogInformation("User {Username} is retaking test {TestId}. Previous attempts: {AttemptCount}", username, id, testResults.Count);

                // Redirect to the BookSlot page with isReattempt flag
                return RedirectToAction("BookSlot", new { id, isReattempt = true, retake = "true" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ReAttempt action");
                return RedirectToAction("MyBookings", new { error = "An error occurred while processing your request" });
            }
        }

        [HttpGet]
        [Route("Test/TestCard/{id}")]
        [Authorize(Roles = "Candidate")]
        public async Task<IActionResult> TestCard(int id)
        {
            try
            {
                // Get the test
                var test = await _context.Tests.FindAsync(id);
                if (test == null)
                {
                    return NotFound("Test not found");
                }

                // Get the current user
                var username = User.Identity?.Name;

                // Check if the user has already taken this test
                var testResults = await _context.TestResults
                    .Where(tr => tr.Username == username)
                    .ToListAsync();

                var alreadyTaken = testResults.Any(tr => tr.TestId == id);

                ViewBag.AlreadyTaken = alreadyTaken;
                ViewBag.TestResults = testResults;

                return View(test);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TestCard action");
                return StatusCode(500, "An error occurred while loading the test card");
            }
        }

        [HttpGet]
        [Route("Test/MyBookings")]
        [AllowAnonymous] // Allow anonymous access for payment redirects
        public async Task<IActionResult> MyBookings(string? message = null, string? error = null, bool refresh = true, string? testId = null, bool fromPayment = false)
        {
            try
            {
                // Get the current user ID
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    // Check if we have booking information in TempData
                    int? bookingTestId = null;

                    // First check if we have a testId parameter
                    if (!string.IsNullOrEmpty(testId))
                    {
                        if (int.TryParse(testId, out int id))
                        {
                            bookingTestId = id;
                            _logger.LogInformation($"Found test ID in URL parameter: {bookingTestId}");

                            // Log more information about this test
                            var test = await _context.Tests.FindAsync(id);
                            if (test != null)
                            {
                                _logger.LogInformation($"Found test with ID {id}: Title={test.Title}, Type={test.Type}");

                                // Check if there are any bookings for this test
                                var bookingsForTest = await _context.TestBookings
                                    .AsNoTracking()
                                    .Where(tb => tb.TestId == id)
                                    .ToListAsync();

                                _logger.LogInformation($"Found {bookingsForTest.Count} bookings for test ID {id}");
                                foreach (var booking in bookingsForTest)
                                {
                                    _logger.LogInformation($"Booking ID: {booking.Id}, UserId: {booking.UserId}, BookingDate: {booking.BookingDate:yyyy-MM-dd}, StartTime: {booking.StartTime:HH:mm:ss}");
                                }
                            }
                            else
                            {
                                _logger.LogWarning($"Test with ID {id} not found in database");
                            }
                        }
                    }
                    // Then check TempData
                    else if (TempData.ContainsKey("BookedTestId") && TempData["BookedTestId"] != null)
                    {
                        if (int.TryParse(TempData["BookedTestId"].ToString(), out int id))
                        {
                            bookingTestId = id;
                            _logger.LogInformation($"Found booked test ID in TempData: {bookingTestId}");
                        }
                    }

                    // If we still don't have a test ID, use a default one for testing
                    if (!bookingTestId.HasValue)
                    {
                        // Get the first test from the database
                        var firstTest = await _context.Tests.FirstOrDefaultAsync();
                        if (firstTest != null)
                        {
                            bookingTestId = firstTest.Id;
                            _logger.LogInformation($"Using default test ID for testing: {bookingTestId}");
                        }
                    }

                    // If we have a test ID, try to find the booking
                    List<TestBooking> dummyBookings = new List<TestBooking>();
                    if (bookingTestId.HasValue)
                    {
                        // Try to find the test
                        var test = await _context.Tests.FindAsync(bookingTestId.Value);
                        if (test != null)
                        {
                            // Create a current dummy booking (available now)
                            var currentBooking = new TestBooking
                            {
                                TestId = test.Id,
                                Test = test,
                                BookingDate = DateTime.Now.Date,
                                StartTime = DateTime.Now.AddMinutes(-30), // Started 30 minutes ago
                                EndTime = DateTime.Now.AddHours(2),       // Ends in 2 hours
                                BookedAt = DateTime.Now.AddDays(-1),      // Booked yesterday
                                CanStartTest = true,                      // Set to true so the Start Test button appears
                                SlotNumber = TempData.ContainsKey("BookedSlotNumber") && TempData["BookedSlotNumber"] != null ?
                                    Convert.ToInt32(TempData["BookedSlotNumber"]) : 2  // Default to slot 2 if not specified
                            };
                            dummyBookings.Add(currentBooking);
                            _logger.LogInformation($"Created current dummy booking for test ID: {bookingTestId}");

                            // Also create an upcoming dummy booking for tomorrow
                            var upcomingBooking = new TestBooking
                            {
                                TestId = test.Id,
                                Test = test,
                                BookingDate = DateTime.Now.Date.AddDays(1), // Tomorrow
                                StartTime = DateTime.Now.Date.AddDays(1).AddHours(14), // Tomorrow at 2 PM
                                EndTime = DateTime.Now.Date.AddDays(1).AddHours(16),   // Tomorrow at 4 PM
                                BookedAt = DateTime.Now,
                                CanStartTest = false,  // Cannot start future test
                                SlotNumber = 3  // Slot 3 (3 PM - 5 PM)
                            };
                            dummyBookings.Add(upcomingBooking);
                            _logger.LogInformation($"Created upcoming dummy booking for test ID: {bookingTestId}");
                        }
                    }

                    // If we still don't have a booking, try to find any test
                    if (dummyBookings.Count == 0)
                    {
                        var test = await _context.Tests.FirstOrDefaultAsync();
                        if (test != null)
                        {
                            // Create a current dummy booking (available now)
                            var currentBooking = new TestBooking
                            {
                                TestId = test.Id,
                                Test = test,
                                BookingDate = DateTime.Now.Date,
                                StartTime = DateTime.Now.AddMinutes(-30), // Started 30 minutes ago
                                EndTime = DateTime.Now.AddHours(2),       // Ends in 2 hours
                                BookedAt = DateTime.Now.AddDays(-1),      // Booked yesterday
                                CanStartTest = true,                      // Set to true so the Start Test button appears
                                SlotNumber = 1  // Slot 1 (9 AM - 11 AM)
                            };
                            dummyBookings.Add(currentBooking);
                            _logger.LogInformation($"Created fallback current dummy booking for test ID: {test.Id}");

                            // Also create an upcoming dummy booking for tomorrow
                            var upcomingBooking = new TestBooking
                            {
                                TestId = test.Id,
                                Test = test,
                                BookingDate = DateTime.Now.Date.AddDays(1), // Tomorrow
                                StartTime = DateTime.Now.Date.AddDays(1).AddHours(14), // Tomorrow at 2 PM
                                EndTime = DateTime.Now.Date.AddDays(1).AddHours(16),   // Tomorrow at 4 PM
                                BookedAt = DateTime.Now,
                                CanStartTest = false,  // Cannot start future test
                                SlotNumber = 3  // Slot 3 (3 PM - 5 PM)
                            };
                            dummyBookings.Add(upcomingBooking);
                            _logger.LogInformation($"Created fallback upcoming dummy booking for test ID: {test.Id}");
                        }
                    }

                    // For anonymous users, show a message and the booking if available
                    ViewBag.SuccessMessage = message ?? "Your payment was successful! You can now access your scheduled test.";

                    // Categorize dummy bookings just like we do for regular bookings
                    var currentTime = DateTime.Now;
                    var upcomingDummyBookings = new List<TestBooking>();
                    var currentDummyBookings = new List<TestBooking>();
                    var pastDummyBookings = new List<TestBooking>();

                    foreach (var booking in dummyBookings)
                    {
                        // Calculate start and end times based on slot number
                        DateTime bookingStartDateTime, bookingEndDateTime;

                        switch (booking.SlotNumber)
                        {
                            case 1: // 9:00 AM - 11:00 AM
                                bookingStartDateTime = booking.BookingDate.AddHours(9);
                                bookingEndDateTime = booking.BookingDate.AddHours(11);
                                break;
                            case 2: // 12:00 PM - 2:00 PM
                                bookingStartDateTime = booking.BookingDate.AddHours(12);
                                bookingEndDateTime = booking.BookingDate.AddHours(14);
                                break;
                            case 3: // 3:00 PM - 5:00 PM
                                bookingStartDateTime = booking.BookingDate.AddHours(15);
                                bookingEndDateTime = booking.BookingDate.AddHours(17);
                                break;
                            case 4: // 6:00 PM - 8:00 PM
                                bookingStartDateTime = booking.BookingDate.AddHours(18);
                                bookingEndDateTime = booking.BookingDate.AddHours(20);
                                break;
                            default:
                                // If not using standard slots, use the stored StartTime and EndTime
                                bookingStartDateTime = booking.StartTime;
                                bookingEndDateTime = booking.EndTime;
                                break;
                        }

                        _logger.LogInformation($"Dummy booking times: StartDateTime={bookingStartDateTime:yyyy-MM-dd HH:mm:ss}, EndDateTime={bookingEndDateTime:yyyy-MM-dd HH:mm:ss}, Now={currentTime:yyyy-MM-dd HH:mm:ss}");

                        // Categorize the booking
                        bool isUpcoming = currentTime < bookingStartDateTime;
                        bool isPast = currentTime > bookingEndDateTime;
                        bool isCurrent = !isUpcoming && !isPast;

                        if (isUpcoming)
                        {
                            _logger.LogInformation($"Dummy booking for test ID {booking.TestId} categorized as UPCOMING");
                            booking.CanStartTest = false;
                            upcomingDummyBookings.Add(booking);
                        }
                        else if (isCurrent)
                        {
                            _logger.LogInformation($"Dummy booking for test ID {booking.TestId} categorized as CURRENT");
                            booking.CanStartTest = true;
                            currentDummyBookings.Add(booking);
                        }
                        else
                        {
                            _logger.LogInformation($"Dummy booking for test ID {booking.TestId} categorized as PAST");
                            booking.CanStartTest = false;
                            pastDummyBookings.Add(booking);
                        }
                    }

                    // Set the ViewBag properties
                    ViewBag.UpcomingBookings = upcomingDummyBookings;
                    ViewBag.CurrentBookings = currentDummyBookings;
                    ViewBag.PastBookings = pastDummyBookings;
                    ViewBag.TestResults = new List<TestResult>();
                    ViewBag.AllBookings = dummyBookings;

                    // Log the dummy bookings for debugging
                    _logger.LogInformation($"Created {dummyBookings.Count} dummy bookings: {upcomingDummyBookings.Count} upcoming, {currentDummyBookings.Count} current, {pastDummyBookings.Count} past");
                    return View();
                }

                // Set refresh message if requested
                if (refresh)
                {
                    message = "Dashboard refreshed successfully. You're viewing the latest data.";
                    // Clear EF tracking to ensure fresh data
                    _context.ChangeTracker.Clear();

                    // Force a database refresh by creating a new context
                    _logger.LogInformation("Forcing database refresh due to refresh parameter");

                    // Log the testId parameter if present
                    if (!string.IsNullOrEmpty(testId))
                    {
                        _logger.LogInformation($"Refresh requested with testId: {testId}");
                    }
                }

                // Pass any messages to the view
                if (!string.IsNullOrEmpty(message))
                {
                    ViewBag.SuccessMessage = message;
                }

                if (!string.IsNullOrEmpty(error))
                {
                    ViewBag.ErrorMessage = error;
                }

                var candidateId = int.Parse(userId);

                // Get all bookings for this user - use AsNoTracking for fresh data
                _logger.LogInformation($"Fetching bookings for user ID: {candidateId}");

                // Force a database refresh by using a new query with AsNoTracking
                _logger.LogInformation($"Executing fresh query for bookings with UserId={candidateId}");

                // Create a completely new query to avoid any caching issues
                // First, clear the context to ensure fresh data
                _context.ChangeTracker.Clear();

                // If coming from payment page, we need to make extra sure we get the latest data
                if (fromPayment)
                {
                    _logger.LogInformation("Coming from payment page, forcing database refresh");

                    // Execute a simple query to flush any pending transactions
                    await _context.Database.ExecuteSqlRawAsync("SELECT 1");

                    // Clear the context again
                    _context.ChangeTracker.Clear();

                    // Check if we have a specific test ID from TempData
                    int? bookedTestIdValue = null;
                    if (TempData.TryGetValue("BookedTestId", out var bookedTestIdObj) && bookedTestIdObj != null)
                    {
                        if (int.TryParse(bookedTestIdObj.ToString(), out int parsedTestId))
                        {
                            bookedTestIdValue = parsedTestId;
                            _logger.LogInformation($"Found specific test ID in TempData: {bookedTestIdValue}");

                            // Check if we have a booking for this test
                            var specificBooking = await _context.TestBookings
                                .AsNoTracking()
                                .FirstOrDefaultAsync(b => b.TestId == bookedTestIdValue && b.UserId == candidateId);

                            if (specificBooking != null)
                            {
                                _logger.LogInformation($"Found booking for test ID {bookedTestIdValue}: BookingId={specificBooking.Id}, BookingDate={specificBooking.BookingDate:yyyy-MM-dd}, StartTime={specificBooking.StartTime:HH:mm:ss}");
                            }
                            else
                            {
                                _logger.LogWarning($"No booking found for test ID {bookedTestIdValue} and user ID {candidateId}");

                                // If no booking found but we have booking details in TempData, create one
                                if (TempData.TryGetValue("BookedDate", out var bookedDateObj) &&
                                    TempData.TryGetValue("BookedStartTime", out var startTimeObj) &&
                                    TempData.TryGetValue("BookedEndTime", out var endTimeObj) &&
                                    TempData.TryGetValue("BookedSlotNumber", out var slotNumberObj))
                                {
                                    _logger.LogInformation("Found booking details in TempData, creating booking");

                                    try {
                                        // Parse booking details
                                        DateTime tempBookingDate = DateTime.Today;
                                        DateTime tempStartTime = DateTime.Now;
                                        DateTime tempEndTime = DateTime.Now.AddHours(2);
                                        int tempSlotNumber = 1;

                                        if (DateTime.TryParse(bookedDateObj.ToString(), out DateTime parsedDate))
                                        {
                                            tempBookingDate = parsedDate.Date;
                                        }

                                        if (DateTime.TryParse(startTimeObj.ToString(), out DateTime parsedStartTime))
                                        {
                                            tempStartTime = tempBookingDate.Date.Add(parsedStartTime.TimeOfDay);
                                        }

                                        if (DateTime.TryParse(endTimeObj.ToString(), out DateTime parsedEndTime))
                                        {
                                            tempEndTime = tempBookingDate.Date.Add(parsedEndTime.TimeOfDay);
                                        }

                                        if (int.TryParse(slotNumberObj.ToString(), out int parsedSlotNumber))
                                        {
                                            tempSlotNumber = parsedSlotNumber;
                                        }

                                        // Get the user's SAP ID
                                        string? userSapId = null;
                                        var user = await _context.Users.FindAsync(candidateId);
                                        if (user != null && !string.IsNullOrEmpty(user.SapId))
                                        {
                                            userSapId = user.SapId;
                                        }

                                        // Create a new booking
                                        var newBooking = new TestBooking
                                        {
                                            TestId = bookedTestIdValue.Value,
                                            UserId = candidateId,
                                            BookedAt = Utilities.TimeZoneHelper.GetCurrentIstTime(),
                                            BookingDate = tempBookingDate,
                                            StartTime = tempStartTime,
                                            EndTime = tempEndTime,
                                            SlotNumber = tempSlotNumber,
                                            UserSapId = userSapId ?? ""
                                        };

                                        _context.TestBookings.Add(newBooking);

                                        // Update the test user count
                                        var testEntity = await _context.Tests.FindAsync(bookedTestIdValue.Value);
                                        if (testEntity != null)
                                        {
                                            testEntity.CurrentUserCount++;
                                        }

                                        await _context.SaveChangesAsync();
                                        _logger.LogInformation($"Successfully created booking with ID {newBooking.Id} from TempData");
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Error creating booking from TempData");
                                    }
                                }
                            }
                        }
                    }
                }

                // Use a direct SQL query to ensure we get the latest data
                try {
                    // Try lowercase table name first (MySQL is case-sensitive)
                    var rawBookings = await _context.Database.ExecuteSqlRawAsync(
                        $"SELECT * FROM testbookings WHERE UserId = {candidateId}");
                    _logger.LogInformation($"Raw SQL query executed for user {candidateId}, affected {rawBookings} rows");
                } catch (Exception ex) {
                    // If that fails, try with uppercase first letter
                    try {
                        var rawBookings = await _context.Database.ExecuteSqlRawAsync(
                            $"SELECT * FROM TestBookings WHERE UserId = {candidateId}");
                        _logger.LogInformation($"Raw SQL query with uppercase table name executed for user {candidateId}, affected {rawBookings} rows");
                    } catch (Exception innerEx) {
                        _logger.LogWarning($"Both SQL queries failed: {ex.Message}, then {innerEx.Message}");
                        // Continue execution - we'll try the LINQ query next
                    }
                }

                // Now use LINQ to get the data with includes
                var bookings = await _context.TestBookings
                    .AsNoTracking() // Force a fresh query to the database
                    .Include(tb => tb.Test)
                    .Where(tb => tb.UserId == candidateId)
                    .OrderBy(tb => tb.BookingDate)
                    .ThenBy(tb => tb.StartTime)
                    .ToListAsync();

                _logger.LogInformation($"LINQ query found {bookings.Count} bookings for user {candidateId}");

                // If we have a specific testId parameter, log if we found a booking for it
                if (!string.IsNullOrEmpty(testId) && int.TryParse(testId, out int specificTestId))
                {
                    var specificBooking = bookings.FirstOrDefault(b => b.TestId == specificTestId);
                    if (specificBooking != null)
                    {
                        _logger.LogInformation($"Found booking for specified testId={specificTestId}: BookingId={specificBooking.Id}, BookingDate={specificBooking.BookingDate:yyyy-MM-dd}, StartTime={specificBooking.StartTime:HH:mm:ss}");

                        // If coming from payment, make sure this booking is marked as available
                        if (fromPayment)
                        {
                            _logger.LogInformation("Coming from payment, ensuring booking is marked as available");
                            specificBooking.CanStartTest = true;
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"No booking found for specified testId={specificTestId}");

                        // Try a direct database query as a last resort
                        var directBooking = await _context.TestBookings
                            .AsNoTracking()
                            .FirstOrDefaultAsync(tb => tb.TestId == specificTestId && tb.UserId == candidateId);

                        if (directBooking != null)
                        {
                            _logger.LogInformation($"Found booking with direct query for testId={specificTestId}: BookingId={directBooking.Id}");
                            // Add it to the list if it wasn't included before
                            if (!bookings.Any(b => b.Id == directBooking.Id))
                            {
                                // If coming from payment, mark this booking as available
                                if (fromPayment)
                                {
                                    _logger.LogInformation("Coming from payment, marking direct booking as available");
                                    directBooking.CanStartTest = true;
                                }

                                bookings.Add(directBooking);
                                _logger.LogInformation("Added the directly queried booking to the list");
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"No booking found with direct query for testId={specificTestId}");
                        }
                    }
                }

                _logger.LogInformation($"Found {bookings.Count} bookings for user ID: {candidateId}");
                foreach (var booking in bookings)
                {
                    _logger.LogInformation($"Booking ID: {booking.Id}, TestId: {booking.TestId}, BookingDate: {booking.BookingDate:yyyy-MM-dd}, StartTime: {booking.StartTime:HH:mm:ss}, EndTime: {booking.EndTime:HH:mm:ss}");
                }

                // Get the current date and time
                var now = DateTime.Now;
                _logger.LogInformation($"Current time: {now}");

                // Initialize lists for different booking categories
                var upcomingBookings = new List<TestBooking>();
                var currentBookings = new List<TestBooking>();
                var pastBookings = new List<TestBooking>();

                // Process each booking to determine its category
                foreach (var booking in bookings)
                {
                    // Log the booking details for debugging
                    _logger.LogInformation($"Processing booking: ID={booking.Id}, TestId={booking.TestId}, BookingDate={booking.BookingDate:yyyy-MM-dd}, StartTime={booking.StartTime:yyyy-MM-dd HH:mm:ss}, EndTime={booking.EndTime:yyyy-MM-dd HH:mm:ss}");

                    // Log the raw booking date from the database
                    _logger.LogInformation($"Raw booking date from database: {booking.BookingDate:yyyy-MM-dd}");

                    // CRITICAL FIX: Do NOT modify the booking date in any way
                    // Just log it for debugging purposes
                    _logger.LogInformation($"Booking date from database: {booking.BookingDate:yyyy-MM-dd}");

                    // Create proper DateTime objects for comparison using the actual slot times
                    // Get the time range for the selected slot based on SlotNumber
                    DateTime bookingStartDateTime, bookingEndDateTime;

                    switch (booking.SlotNumber)
                    {
                        case 1: // 9:00 AM - 11:00 AM
                            bookingStartDateTime = booking.BookingDate.AddHours(9);
                            bookingEndDateTime = booking.BookingDate.AddHours(11);
                            break;
                        case 2: // 12:00 PM - 2:00 PM
                            bookingStartDateTime = booking.BookingDate.AddHours(12);
                            bookingEndDateTime = booking.BookingDate.AddHours(14);
                            break;
                        case 3: // 3:00 PM - 5:00 PM
                            bookingStartDateTime = booking.BookingDate.AddHours(15);
                            bookingEndDateTime = booking.BookingDate.AddHours(17);
                            break;
                        case 4: // 6:00 PM - 8:00 PM
                            bookingStartDateTime = booking.BookingDate.AddHours(18);
                            bookingEndDateTime = booking.BookingDate.AddHours(20);
                            break;
                        default:
                            // If not using standard slots, use the stored StartTime and EndTime
                            // Fallback to using the stored times if slot number is invalid
                            bookingStartDateTime = booking.BookingDate.Add(booking.StartTime.TimeOfDay);
                            bookingEndDateTime = booking.BookingDate.Add(booking.EndTime.TimeOfDay);
                            break;
                    }

                    _logger.LogInformation($"Calculated times: StartDateTime={bookingStartDateTime:yyyy-MM-dd HH:mm:ss}, EndDateTime={bookingEndDateTime:yyyy-MM-dd HH:mm:ss}");
                    _logger.LogInformation($"Current time: {now:yyyy-MM-dd HH:mm:ss}");
                    _logger.LogInformation($"Is future test? {now < bookingStartDateTime}");
                    _logger.LogInformation($"Is current test? {now >= bookingStartDateTime && now <= bookingEndDateTime}");
                    _logger.LogInformation($"Is past test? {now > bookingEndDateTime}");

                    // Explicitly compare year, month, day, hour, minute, second for debugging
                    bool isFutureYear = bookingStartDateTime.Year > now.Year;
                    bool isSameYear = bookingStartDateTime.Year == now.Year;
                    bool isFutureMonth = isSameYear && bookingStartDateTime.Month > now.Month;
                    bool isSameMonth = isSameYear && bookingStartDateTime.Month == now.Month;
                    bool isFutureDay = isSameMonth && bookingStartDateTime.Day > now.Day;
                    bool isSameDay = isSameMonth && bookingStartDateTime.Day == now.Day;
                    bool isFutureHour = isSameDay && bookingStartDateTime.Hour > now.Hour;
                    bool isSameHour = isSameDay && bookingStartDateTime.Hour == now.Hour;
                    bool isFutureMinute = isSameHour && bookingStartDateTime.Minute > now.Minute;
                    bool isSameMinute = isSameHour && bookingStartDateTime.Minute == now.Minute;
                    bool isFutureSecond = isSameMinute && bookingStartDateTime.Second > now.Second;

                    _logger.LogInformation($"Detailed comparison: isFutureYear={isFutureYear}, isSameYear={isSameYear}, isFutureMonth={isFutureMonth}, isSameMonth={isSameMonth}, isFutureDay={isFutureDay}, isSameDay={isSameDay}, isFutureHour={isFutureHour}, isSameHour={isSameHour}, isFutureMinute={isFutureMinute}, isSameMinute={isSameMinute}, isFutureSecond={isFutureSecond}");

                    // Simplify the categorization using the DateTime objects we created
                    bool isUpcoming = now < bookingStartDateTime;
                    bool isPast = now > bookingEndDateTime;
                    bool isCurrent = !isUpcoming && !isPast;

                    _logger.LogInformation($"Booking {booking.Id} categorization: isUpcoming={isUpcoming}, isCurrent={isCurrent}, isPast={isPast}");
                    _logger.LogInformation($"Booking {booking.Id} times: StartDateTime={bookingStartDateTime:yyyy-MM-dd HH:mm:ss}, EndDateTime={bookingEndDateTime:yyyy-MM-dd HH:mm:ss}, Now={now:yyyy-MM-dd HH:mm:ss}");

                    _logger.LogInformation($"Final categorization: isUpcoming={isUpcoming}, isCurrent={isCurrent}, isPast={isPast}");

                    // Categorize the booking
                    if (isUpcoming)
                    {
                        // Upcoming: current time is before the start time
                        _logger.LogInformation($"Booking {booking.Id} categorized as UPCOMING");
                        booking.CanStartTest = false;
                        upcomingBookings.Add(booking);
                    }
                    else if (isCurrent)
                    {
                        // Current: current time is within the time slot
                        _logger.LogInformation($"Booking {booking.Id} categorized as CURRENT");
                        booking.CanStartTest = true;
                        currentBookings.Add(booking);
                    }
                    else
                    {
                        // Past: current time is after the end time
                        _logger.LogInformation($"Booking {booking.Id} categorized as PAST");
                        booking.CanStartTest = false;
                        pastBookings.Add(booking);
                    }
                }

                // Check if the user has already taken each test - use AsNoTracking for fresh data
                var username = User.Identity?.Name;
                var testResults = await _context.TestResults
                    .AsNoTracking()
                    .Where(tr => tr.Username == username)
                    .ToListAsync();

                // Copy payment failure error from TempData to ViewBag if present
                if (TempData["ErrorMessage"] != null)
                {
                    ViewBag.ErrorMessage = TempData["ErrorMessage"];
                }
                // Also copy error query parameter to ViewBag.ErrorMessage if present
                if (!string.IsNullOrEmpty(error))
                {
                    ViewBag.ErrorMessage = error;
                }
                // Pass the data to the view
                ViewBag.UpcomingBookings = upcomingBookings;
                // Remove duplicate TestIds, keep only the most recent (latest StartTime) booking for each TestId
ViewBag.CurrentBookings = currentBookings
    .GroupBy(b => b.TestId)
    .Select(g => g.OrderByDescending(b => b.StartTime).First())
    .ToList();
                ViewBag.PastBookings = pastBookings;
                ViewBag.TestResults = testResults;
                ViewBag.AllBookings = bookings; // Pass all bookings to the view

                // If coming from payment, add a special message
                if (fromPayment)
                {
                    ViewBag.SuccessMessage = message ?? "Payment successful! Your booking has been confirmed. You can now access your test during the scheduled time.";
                }

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in MyBookings action");
                return RedirectToAction("Index", new { error = "An error occurred while loading your bookings" });
            }
        }

        [HttpGet]
        [Route("Test/History")]
        [Authorize]
        public async Task<IActionResult> History()
        {
            try
            {
                var username = User.Identity?.Name;
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

                if (string.IsNullOrEmpty(username))
                {
                    return RedirectToAction("Login", "Auth");
                }

                // Only allow candidates to access test history
                if (userRole == "Admin" || userRole == "Organization")
                {
                    return RedirectToAction("Index");
                }

                // Get the user's SAP ID and other information
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                string sapId = "N/A";

                if (!string.IsNullOrEmpty(userId))
                {
                    var user = await _context.Users.FindAsync(int.Parse(userId));
                    if (user != null)
                    {
                        if (!string.IsNullOrEmpty(user.SapId))
                        {
                            sapId = user.SapId;
                        }
                    }
                }

                // Pass the SAP ID to the view
                ViewBag.UserSapId = sapId;

                // Candidates can only see their own test results
                var testResults = await _context.TestResults
                    .Include(r => r.Test)
                    .Where(r => r.Username == username)
                    .OrderByDescending(r => r.SubmittedAt)
                    .ToListAsync();

                return View(testResults);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving test history for user {Username}", User.Identity?.Name);
                return View(new List<TestResult>());
            }
        }

        private async Task RegenerateResultsInternal(int organizationId, List<TestResult> testResults)
        {
            try
            {
                _logger.LogInformation("Regenerating organization test results internally for organization ID: {OrganizationId}", organizationId);

                // Use raw SQL to delete existing organization test results
                try
                {
                    var deleteCommand = $"DELETE FROM organizationtestresult WHERE OrganizationId = {organizationId}";
                    await _context.Database.ExecuteSqlRawAsync(deleteCommand);
                    _logger.LogInformation("Existing organization test results removed successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error deleting organization test results. Continuing with insert...");
                }

                if (testResults.Count > 0)
                {
                    // Group results by test and username
                    var groupedResults = testResults
                        .GroupBy(r => new { r.TestId, r.Username })
                        .Select(g => new
                        {
                            OrganizationId = organizationId,
                            TestId = g.Key.TestId,
                            TestTitle = g.First().Test.Title,
                            Username = g.Key.Username,
                            TotalAttempts = g.Count(),
                            BestScore = g.Max(r => r.Score),
                            AverageScore = g.Average(r => r.Score),
                            LastAttemptDate = g.Max(r => r.SubmittedAt),
                            CreatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime()
                        })
                        .ToList();

                    _logger.LogInformation("Prepared {Count} new organization test results", groupedResults.Count);

                    // Use raw SQL to insert new organization test results
                    foreach (var result in groupedResults)
                    {
                        try
                        {
                            string insertCommand = @"INSERT INTO organizationtestresult
                                (OrganizationId, TestId, TestTitle, Username, TotalAttempts, BestScore, AverageScore, LastAttemptDate, CreatedAt)
                                VALUES
                                ({0}, {1}, '{2}', '{3}', {4}, {5}, {6}, '{7}', '{8}')";

                            await _context.Database.ExecuteSqlRawAsync(
                                insertCommand,
                                result.OrganizationId,
                                result.TestId,
                                result.TestTitle.Replace("'", "''"),
                                result.Username.Replace("'", "''"),
                                result.TotalAttempts,
                                result.BestScore,
                                result.AverageScore,
                                result.LastAttemptDate.ToString("yyyy-MM-dd HH:mm:ss"),
                                result.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                            _logger.LogInformation("Inserted result for test {TestId}, user {Username}", result.TestId, result.Username);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error inserting result for test {TestId}, user {Username}", result.TestId, result.Username);
                        }
                    }

                    _logger.LogInformation("Successfully regenerated organization test results internally");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in RegenerateResultsInternal");
            }
        }

        [HttpGet]
        [Route("Test/RegenerateResults")]
        [Authorize(Roles = "Organization")]
        public async Task<IActionResult> RegenerateResults()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userId == null)
                {
                    return RedirectToAction("Login", "Auth");
                }

                var organizationId = int.Parse(userId);

                _logger.LogInformation("Regenerating organization test results for organization ID: {OrganizationId}", organizationId);

                // Get all test results for this organization
                var testResults = await _context.TestResults
                    .Include(r => r.Test)
                    .Where(r => r.Test.CreatedBy == organizationId)
                    .OrderByDescending(r => r.SubmittedAt)
                    .ToListAsync();

                _logger.LogInformation("Found {Count} test results", testResults.Count);

                if (testResults.Count > 0)
                {
                    try
                    {
                        // Use raw SQL to delete existing organization test results
                        _logger.LogInformation("Removing existing organization test results for organization ID: {OrganizationId}", organizationId);

                        try
                        {
                            var deleteCommand = $"DELETE FROM organizationtestresult WHERE OrganizationId = {organizationId}";
                            await _context.Database.ExecuteSqlRawAsync(deleteCommand);
                            _logger.LogInformation("Existing organization test results removed successfully");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error deleting organization test results. Continuing with insert...");
                            // Continue with the process even if delete fails
                        }

                        // Get all users to retrieve their SAP IDs
                        var users = await _context.Users.ToDictionaryAsync(u => u.Username, u => u.SapId);

                        // Group results by test and username
                        var groupedResults = testResults
                            .GroupBy(r => new { r.TestId, r.Username })
                            .Select(g => new
                            {
                                g.Key.TestId,
                                g.Key.Username,
                                TestTitle = g.First().Test.Title,
                                TotalAttempts = g.Count(),
                                BestScore = g.Max(r => r.Score),
                                AverageScore = g.Average(r => r.Score),
                                LastAttemptDate = g.Max(r => r.SubmittedAt),
                                // First try to get SAP ID from test results, then fall back to users dictionary
                                UserSapId = g.FirstOrDefault(r => !string.IsNullOrEmpty(r.UserSapId))?.UserSapId ??
                                           (users.TryGetValue(g.Key.Username, out var sapId) ? sapId : null)
                            })
                            .ToList();

                        _logger.LogInformation("Prepared {Count} new organization test results", groupedResults.Count);

                        // Use raw SQL to insert new organization test results
                        foreach (var result in groupedResults)
                        {
                            try
                            {
                                // No need to declare insertCommand variable anymore

                                try
                                {
                                    // Try to include UserSapId column
                                    var sapIdValue = result.UserSapId != null ? $"'{result.UserSapId.Replace("'", "''")}'": "NULL";

                                    string sqlWithSapId = @"INSERT INTO organizationtestresult
                                        (OrganizationId, TestId, TestTitle, Username, UserSapId, TotalAttempts, BestScore, AverageScore, LastAttemptDate, CreatedAt)
                                        VALUES
                                        ({0}, {1}, '{2}', '{3}', {4}, {5}, {6}, {7}, '{8}', '{9}')";

                                    await _context.Database.ExecuteSqlRawAsync(
                                        sqlWithSapId,
                                        organizationId,
                                        result.TestId,
                                        result.TestTitle.Replace("'", "''"),
                                        result.Username.Replace("'", "''"),
                                        sapIdValue,
                                        result.TotalAttempts,
                                        result.BestScore,
                                        result.AverageScore,
                                        result.LastAttemptDate.ToString("yyyy-MM-dd HH:mm:ss"),
                                        DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
                                }
                                catch
                                {
                                    // If UserSapId column doesn't exist, use the version without it
                                    string sqlWithoutSapId = @"INSERT INTO organizationtestresult
                                        (OrganizationId, TestId, TestTitle, Username, TotalAttempts, BestScore, AverageScore, LastAttemptDate, CreatedAt)
                                        VALUES
                                        ({0}, {1}, '{2}', '{3}', {4}, {5}, {6}, '{7}', '{8}')";

                                    await _context.Database.ExecuteSqlRawAsync(
                                        sqlWithoutSapId,
                                        organizationId,
                                        result.TestId,
                                        result.TestTitle.Replace("'", "''"),
                                        result.Username.Replace("'", "''"),
                                        result.TotalAttempts,
                                        result.BestScore,
                                        result.AverageScore,
                                        result.LastAttemptDate.ToString("yyyy-MM-dd HH:mm:ss"),
                                        DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
                                }
                                _logger.LogInformation("Inserted result for test {TestId}, user {Username}", result.TestId, result.Username);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error inserting result for test {TestId}, user {Username}", result.TestId, result.Username);
                                // Continue with the next result
                            }
                        }

                        _logger.LogInformation("Successfully regenerated organization test results using raw SQL");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error executing raw SQL for organization test results");
                        throw;
                    }
                }

                return RedirectToAction("OrganizationResults", new { message = "Results regenerated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error regenerating organization test results");
                return RedirectToAction("OrganizationResults", new { error = "Error regenerating results: " + ex.Message });
            }
        }

        // Helper method to get the test time information for a user's test booking
        private async Task<(DateTime startTime, DateTime endTime)> GetUserTestTime(int testId, string username)
        {
            try
            {
                // First try to find the user by username
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
                if (user == null)
                {
                    _logger.LogWarning("User not found with username: {Username}", username);
                    return (DateTime.MinValue, DateTime.MinValue); // Return min values if user not found
                }

                // Then find the booking for this test and user
                var booking = await _context.TestBookings
                    .FirstOrDefaultAsync(tb => tb.TestId == testId && tb.UserId == user.Id);

                if (booking != null)
                {
                    // Since we're now storing times in IST, we can use them directly
                    var startDateTime = booking.StartTime;
                    var endDateTime = booking.EndTime;

                    _logger.LogInformation("Found booking with time {StartTime} - {EndTime} for test {TestId} and user {Username}",
                        startDateTime, endDateTime, testId, username);

                    return (startDateTime, endDateTime);
                }
                else
                {
                    _logger.LogWarning("No booking found for test {TestId} and user {Username}", testId, username);
                    return (DateTime.MinValue, DateTime.MinValue); // Return min values if no booking found
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting test time for test {TestId} and user {Username}", testId, username);
                return (DateTime.MinValue, DateTime.MinValue); // Return min values on error
            }
        }

        [HttpGet]
        [Route("Test/OrganizationResults")]
        [Authorize(Roles = "Organization")]
        public async Task<IActionResult> OrganizationResults(string? message = null, string? error = null)
        {

            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userId == null)
                {
                    return RedirectToAction("Login", "Auth");
                }

                var organizationId = int.Parse(userId);

                _logger.LogInformation("Retrieving organization test results for organization ID: {OrganizationId}", organizationId);

                // Get all individual test results for detailed view
                var testResults = await _context.TestResults
                    .Include(r => r.Test)
                    .Where(r => r.Test.CreatedBy == organizationId)
                    .OrderByDescending(r => r.SubmittedAt)
                    .ToListAsync();

                _logger.LogInformation("Found {Count} individual test results", testResults.Count);

                // Process test results directly
                _logger.LogInformation("Processing test results directly for display");

                // Get all users to retrieve their SAP IDs
                var users = await _context.Users.ToDictionaryAsync(u => u.Username, u => u.SapId);

                // Group test results by test and username
                var groupedResults = testResults
                    .GroupBy(r => new { r.TestId, r.Username })
                    .Select(g => new
                    {
                        g.Key.TestId,
                        g.Key.Username,
                        TestTitle = g.First().Test.Title,
                        Attempts = g.OrderByDescending(r => r.SubmittedAt).ToList(),
                        TotalAttempts = g.Count(),
                        BestScore = g.Max(r => r.Score),
                        AverageScore = g.Average(r => r.Score),
                        LastAttemptDate = g.Max(r => r.SubmittedAt),
                        // First try to get SAP ID from test results, then fall back to users dictionary
                        UserSapId = g.FirstOrDefault(r => !string.IsNullOrEmpty(r.UserSapId))?.UserSapId ??
                                   (users.TryGetValue(g.Key.Username, out var sapId) ? sapId : null)
                    })
                    .ToList();

                _logger.LogInformation("Grouped {ResultCount} test results into {GroupCount} groups", testResults.Count, groupedResults.Count);

                // Create summary objects for display
                var summaryResults = groupedResults.Select(group => new {
                    group.TestId,
                    group.TestTitle,
                    group.Username,
                    group.UserSapId,
                    group.TotalAttempts,
                    group.BestScore,
                    group.AverageScore,
                    group.LastAttemptDate,
                    StartTime = group.Attempts.FirstOrDefault()?.StartTime,
                    EndTime = group.Attempts.FirstOrDefault()?.EndTime
                }).ToList();

                _logger.LogInformation("Created {Count} summary results", summaryResults.Count);

                // Store both sets of results in ViewBag
                ViewBag.SummaryResults = summaryResults;
                ViewBag.DetailedResults = testResults;

                // Set message and error in ViewBag if provided
                if (!string.IsNullOrEmpty(message))
                {
                    ViewBag.SuccessMessage = message;
                }

                if (!string.IsNullOrEmpty(error))
                {
                    ViewBag.ErrorMessage = error;
                }

                return View(testResults);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving organization test results");
                return View(new List<TestResult>());
            }
        }

        [HttpGet]
        [Route("Test/ExportDailyResults")]
        [Authorize(Roles = "Organization")]
        public async Task<IActionResult> ExportDailyResults(string? date = null)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userId == null)
                {
                    return RedirectToAction("Login", "Auth");
                }

                var organizationId = int.Parse(userId);
                _logger.LogInformation("Exporting daily test results for organization ID: {OrganizationId}", organizationId);

                // Parse the date or use today's date if not provided
                DateTime targetDate;
                if (string.IsNullOrEmpty(date))
                {
                    targetDate = Utilities.TimeZoneHelper.GetCurrentIstTime().Date;
                }
                else
                {
                    if (!DateTime.TryParse(date, out targetDate))
                    {
                        targetDate = Utilities.TimeZoneHelper.GetCurrentIstTime().Date;
                    }
                    else
                    {
                        targetDate = targetDate.Date; // Ensure we're only looking at the date part
                    }
                }

                // Get all test results for this organization
                // First get all results for the organization within a reasonable date range
                // Calculate a date range (7 days before and after the target date)
                var startDateRange = targetDate.AddDays(-7);
                var endDateRange = targetDate.AddDays(7);

                var testResults = await _context.TestResults
                    .Include(r => r.Test)
                    .Where(r => r.Test.CreatedBy == organizationId &&
                           r.SubmittedAt >= startDateRange &&
                           r.SubmittedAt <= endDateRange)
                    .ToListAsync();

                // Then filter by date in memory
                testResults = testResults
                    .Where(r => Utilities.TimeZoneHelper.ToIst(r.SubmittedAt).Date == targetDate.Date)
                    .OrderByDescending(r => r.SubmittedAt)
                    .ToList();

                if (testResults.Count == 0)
                {
                    _logger.LogWarning("No test results found for organization ID: {OrganizationId} on date: {Date}", organizationId, targetDate.ToString("yyyy-MM-dd"));
                    return RedirectToAction("OrganizationResults", new { error = $"No test results found for {targetDate.ToString("yyyy-MM-dd")}" });
                }

                // Get all users to retrieve their SAP IDs
                var users = await _context.Users.ToDictionaryAsync(u => u.Username, u => u.SapId);

                // Group results by test and username for summary
                var summaryResults = testResults
                    .GroupBy(r => new { r.TestId, r.Username })
                    .Select(g => new
                    {
                        TestId = g.Key.TestId,
                        TestTitle = g.First().Test.Title,
                        Username = g.Key.Username,
                        UserSapId = g.FirstOrDefault(r => !string.IsNullOrEmpty(r.UserSapId))?.UserSapId ??
                                   (users.TryGetValue(g.Key.Username, out var sapId) ? sapId : "N/A"),
                        TotalAttempts = g.Count(),
                        BestScore = g.Max(r => r.Score),
                        AverageScore = g.Average(r => r.Score),
                        LastAttemptDate = g.Max(r => r.SubmittedAt),
                        TotalQuestions = g.First().TotalQuestions,
                        CorrectAnswers = g.Max(r => r.CorrectAnswers)
                    })
                    .ToList();

                // Create Excel package
                using (var package = new OfficeOpenXml.ExcelPackage())
                {
                    // Add a worksheet
                    var worksheet = package.Workbook.Worksheets.Add("Test Results");

                    // Add headers
                    worksheet.Cells[1, 1].Value = "Test Title";
                    worksheet.Cells[1, 2].Value = "Candidate";
                    worksheet.Cells[1, 3].Value = "SAP ID";
                    worksheet.Cells[1, 4].Value = "Test Date";
                    worksheet.Cells[1, 5].Value = "Score";
                    worksheet.Cells[1, 6].Value = "Correct Answers";
                    worksheet.Cells[1, 7].Value = "Total Questions";
                    worksheet.Cells[1, 8].Value = "Attempts";
                    worksheet.Cells[1, 9].Value = "Average Score";
                    worksheet.Cells[1, 10].Value = "Status";

                    // Style the header row
                    using (var range = worksheet.Cells[1, 1, 1, 10])
                    {
                        range.Style.Font.Bold = true;
                        range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                        range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                        range.Style.Font.Color.SetColor(System.Drawing.Color.Black);
                    }

                    // Add data
                    int row = 2;
                    foreach (var result in summaryResults)
                    {
                        double passThreshold = result.TotalQuestions * 0.7; // 70% passing threshold
                        bool isPassed = result.BestScore >= passThreshold;

                        worksheet.Cells[row, 1].Value = result.TestTitle;
                        worksheet.Cells[row, 2].Value = result.Username;
                        worksheet.Cells[row, 3].Value = result.UserSapId;
                        worksheet.Cells[row, 4].Value = Utilities.TimeZoneHelper.ToIst(result.LastAttemptDate);
                        worksheet.Cells[row, 5].Value = result.BestScore;
                        worksheet.Cells[row, 6].Value = result.CorrectAnswers;
                        worksheet.Cells[row, 7].Value = result.TotalQuestions;
                        worksheet.Cells[row, 8].Value = result.TotalAttempts;
                        worksheet.Cells[row, 9].Value = result.AverageScore;
                        worksheet.Cells[row, 10].Value = isPassed ? "PASSED" : "FAILED";

                        // Format the date column
                        worksheet.Cells[row, 4].Style.Numberformat.Format = "yyyy-mm-dd hh:mm";

                        // Format the score columns
                        worksheet.Cells[row, 5].Style.Numberformat.Format = "0.00";
                        worksheet.Cells[row, 9].Style.Numberformat.Format = "0.00";

                        // Color code the status column
                        if (isPassed)
                        {
                            worksheet.Cells[row, 10].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                            worksheet.Cells[row, 10].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGreen);
                        }
                        else
                        {
                            worksheet.Cells[row, 10].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                            worksheet.Cells[row, 10].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightPink);
                        }

                        row++;
                    }

                    // Auto-fit columns
                    worksheet.Cells.AutoFitColumns();

                    // Convert to byte array
                    var content = package.GetAsByteArray();

                    // Return as file
                    string fileName = $"TestResults_{targetDate:yyyy-MM-dd}.xlsx";
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting daily test results");
                return RedirectToAction("OrganizationResults", new { error = "Error exporting results: " + ex.Message });
            }
        }
    }
}
