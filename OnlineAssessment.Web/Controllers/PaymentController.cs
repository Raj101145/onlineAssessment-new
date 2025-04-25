using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OnlineAssessment.Web.Models;
using OnlineAssessment.Web.Services;
using OnlineAssessment.Web.Helpers;
using System.Security.Claims;
using System.Data.Common;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Newtonsoft.Json;

namespace OnlineAssessment.Web.Controllers
{
    [Authorize]
    public class PaymentController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILogger<PaymentController> _logger;
        private readonly Services.PayUService _payUService;
        private readonly IEmailService _emailService;

        public PaymentController(AppDbContext context, ILogger<PaymentController> logger, Services.PayUService payUService, IEmailService emailService)
        {
            _context = context;
            _logger = logger;
            _payUService = payUService;
            _emailService = emailService;
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Index(int? testId = null, string? date = null, string? startTime = null, string? endTime = null, int? slotNumber = null, string? userSapId = null, bool isReattempt = false)
        {
            // Check if user is authenticated
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId) || !int.TryParse(userId, out int userIdInt))
            {
                return RedirectToAction("Login", "Auth");
            }

            var user = await _context.Users.FindAsync(userIdInt);
            if (user == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            // Store the booking details in Session for use after payment
            if (testId.HasValue)
            {
                HttpContext.Session.SetInt32("PendingTestId", testId.Value);
                _logger.LogInformation($"[SessionSet] PendingTestId: {testId.Value}");
            }
            if (!string.IsNullOrEmpty(date)) {
                HttpContext.Session.SetString("PendingDate", date);
                _logger.LogInformation($"[SessionSet] PendingDate: {date}");
            }
            if (!string.IsNullOrEmpty(startTime)) {
                HttpContext.Session.SetString("PendingStartTime", startTime);
                _logger.LogInformation($"[SessionSet] PendingStartTime: {startTime}");
            }
            if (!string.IsNullOrEmpty(endTime)) {
                HttpContext.Session.SetString("PendingEndTime", endTime);
                _logger.LogInformation($"[SessionSet] PendingEndTime: {endTime}");
            }

            // Store the user ID in the session
            HttpContext.Session.SetInt32("PendingUserId", userIdInt);
            _logger.LogInformation($"[SessionSet] PendingUserId: {userIdInt}");
            if (slotNumber.HasValue) {
                HttpContext.Session.SetInt32("PendingSlotNumber", slotNumber.Value);
                _logger.LogInformation($"[SessionSet] PendingSlotNumber: {slotNumber.Value}");
            }
            if (!string.IsNullOrEmpty(userSapId)) {
                HttpContext.Session.SetString("PendingUserSapId", userSapId);
                _logger.LogInformation($"[SessionSet] PendingUserSapId: {userSapId}");
            }
            if (isReattempt) {
                HttpContext.Session.SetString("IsReattempt", "true");
                _logger.LogInformation("[SessionSet] IsReattempt: true");
            }
            // Log all session values after setting
            _logger.LogInformation($"[SessionSet] Summary: PendingTestId={HttpContext.Session.GetInt32("PendingTestId")}, PendingDate={HttpContext.Session.GetString("PendingDate")}, PendingStartTime={HttpContext.Session.GetString("PendingStartTime")}, PendingEndTime={HttpContext.Session.GetString("PendingEndTime")}, PendingSlotNumber={HttpContext.Session.GetInt32("PendingSlotNumber")}, PendingUserSapId={HttpContext.Session.GetString("PendingUserSapId")}, IsReattempt={HttpContext.Session.GetString("IsReattempt")}");


            // No need to keep Session values as they persist until cleared or expired.

            // Get the specific test if provided, otherwise get the first test
            Test test;
            if (testId.HasValue)
            {
                test = await _context.Tests.FindAsync(testId.Value);
            }
            else
            {
                test = await _context.Tests.FirstOrDefaultAsync();
            }

            if (test == null)
            {
                // Create a default test if none exists
                test = new Test
                {
                    Title = "General Assessment Test",
                    Description = "This is a general assessment test for candidates.",
                    DurationMinutes = 60,
                    MaxAttempts = 1
                };
            }

            ViewBag.Test = test;
            ViewBag.Amount = 1.00M; // Fixed amount of 1 rupee
            ViewBag.UserName = user.FirstName + " " + user.LastName;
            ViewBag.IsAdditionalSlot = true; // This is for booking an additional slot

            return View();
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> PayUInitiate()
        {
            // Defensive: Check for all required Session values
            int? testId = HttpContext.Session.GetInt32("PendingTestId");
            string pendingDate = HttpContext.Session.GetString("PendingDate");
            string pendingStartTime = HttpContext.Session.GetString("PendingStartTime");
            string pendingEndTime = HttpContext.Session.GetString("PendingEndTime");
            int? slotNumber = HttpContext.Session.GetInt32("PendingSlotNumber");
            string userSapId = HttpContext.Session.GetString("PendingUserSapId");
            string isReattempt = HttpContext.Session.GetString("IsReattempt");
            _logger.LogInformation($"[SessionGet] PendingTestId: {testId}, PendingDate: {pendingDate}, PendingStartTime: {pendingStartTime}, PendingEndTime: {pendingEndTime}, PendingSlotNumber: {slotNumber}, PendingUserSapId: {userSapId}, IsReattempt: {isReattempt}");
            if (!testId.HasValue)
            {
                _logger.LogWarning("[SessionError] Missing PendingTestId in session");
                TempData["PaymentError"] = "Session expired or invalid. Please try booking again.";
                return RedirectToAction("Index", "Test", new { error = TempData["PaymentError"] });
            }
            if (string.IsNullOrEmpty(pendingDate))
            {
                _logger.LogWarning("[SessionError] Missing PendingDate in session");
                TempData["PaymentError"] = "Session expired or invalid date. Please try booking again.";
                return RedirectToAction("Index", "Test", new { error = TempData["PaymentError"] });
            }
            if (string.IsNullOrEmpty(pendingStartTime))
            {
                _logger.LogWarning("[SessionError] Missing PendingStartTime in session");
                TempData["PaymentError"] = "Session expired or invalid start time. Please try booking again.";
                return RedirectToAction("Index", "Test", new { error = TempData["PaymentError"] });
            }
            if (string.IsNullOrEmpty(pendingEndTime))
            {
                _logger.LogWarning("[SessionError] Missing PendingEndTime in session");
                TempData["PaymentError"] = "Session expired or invalid end time. Please try booking again.";
                return RedirectToAction("Index", "Test", new { error = TempData["PaymentError"] });
            }

            // Use fixed amount of 1 rupee for payment
            string amount = "1.00";
            string productinfo = $"TestBooking_{testId}";
            string firstname = User.Identity?.Name ?? "User";
            string email = User.FindFirst(ClaimTypes.Email)?.Value ?? "test@example.com";
            string phone = "9999999999";

            // Use the helper method to generate a transaction ID
            string txnid = _payUService.GenerateTransactionId();

            // Extract testId from productinfo for the success URL
            string testIdStr = testId.ToString();

            // Use the service to prepare the PayU request with testId
            var payUParams = _payUService.PreparePayURequest(txnid, amount, productinfo, firstname, email, phone, testIdStr);
            // PayU base URL is already included in the parameters by the helper

            // Create a model for the view
            var model = new PayURequestModel
            {
                Parameters = payUParams
            };

            // Log the payment request for debugging
            _logger.LogInformation($"Initiating PayU payment: TxnID={txnid}, Amount={amount}, Product={productinfo}");

            return View("PayUInitiate", model);
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Process()
        {
            // For testing purposes, we'll simulate a successful payment
            // In a real implementation, this would be handled by the Success action
            return await ProcessSuccessfulPayment();
        }

        [AcceptVerbs("GET", "POST")]
        [Route("Payment/Success")]
        [AllowAnonymous] // Allow anonymous access for PayU callbacks and redirects
        public async Task<IActionResult> Success(string txnid = null, string status = null)
        {
            _logger.LogInformation("Payment success callback received with txnid: {TxnId}, status: {Status}", txnid, status);

            // Log all request parameters for debugging
            _logger.LogInformation("Request method: {Method}", HttpContext.Request.Method);
            foreach (var key in HttpContext.Request.Query.Keys)
            {
                _logger.LogInformation("Query param: {Key}={Value}", key, HttpContext.Request.Query[key]);
            }

            if (HttpContext.Request.Method == "POST")
            {
                _logger.LogInformation("Payment success callback received from PayU via POST");
                // Process the payment for POST requests
                return await ProcessSuccessfulPayment();
            }
            else
            {
                _logger.LogInformation("Payment success callback received from PayU via GET");

                // For GET requests, show the success page which will automatically redirect
                TempData["SuccessMessage"] = "Payment successful! You can now access your scheduled test.";

                // Transfer from Session to TempData for downstream logic
                int? pendingTestId = HttpContext.Session.GetInt32("PendingTestId");
                string pendingDate = HttpContext.Session.GetString("PendingDate");
                string pendingStartTime = HttpContext.Session.GetString("PendingStartTime");
                string pendingEndTime = HttpContext.Session.GetString("PendingEndTime");
                int? pendingSlotNumber = HttpContext.Session.GetInt32("PendingSlotNumber");
                string pendingUserSapId = HttpContext.Session.GetString("PendingUserSapId");

                // Try to recover testId from query parameters if session is lost
                if (!pendingTestId.HasValue)
                {
                    var queryTestId = HttpContext.Request.Query["testId"].ToString();
                    if (!string.IsNullOrEmpty(queryTestId) && int.TryParse(queryTestId, out int recoveredTestId))
                    {
                        _logger.LogInformation("Recovered testId {TestId} from query parameters in Success method", recoveredTestId);
                        pendingTestId = recoveredTestId;
                    }
                }

                // Get the user ID from the session or claims
                int? userId = HttpContext.Session.GetInt32("PendingUserId");
                if (userId == null && User.Identity.IsAuthenticated)
                {
                    var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (!string.IsNullOrEmpty(userIdStr) && int.TryParse(userIdStr, out int userIdInt))
                    {
                        userId = userIdInt;
                    }
                }

                // Create a payment record if we have a valid user ID
                if (userId.HasValue && pendingTestId.HasValue)
                {
                    try
                    {
                        var payment = new Payment
                        {
                            UserId = userId.Value,
                            Amount = 1.00M,
                            Currency = "INR",
                            Status = "Completed",
                            CreatedAt = DateTime.UtcNow,
                            PaidAt = DateTime.UtcNow
                        };
                        _context.Payments.Add(payment);

                        // Find or create booking
                        var existingBooking = await _context.TestBookings
                            .FirstOrDefaultAsync(tb => tb.TestId == pendingTestId.Value && tb.UserId == userId.Value);

                        if (existingBooking == null && !string.IsNullOrEmpty(pendingDate) &&
                            !string.IsNullOrEmpty(pendingStartTime) && !string.IsNullOrEmpty(pendingEndTime) &&
                            pendingSlotNumber.HasValue)
                        {
                            // Create a new booking if none exists
                            var booking = new TestBooking
                            {
                                TestId = pendingTestId.Value,
                                UserId = userId.Value,
                                BookingDate = DateTime.Parse(pendingDate),
                                StartTime = DateTime.Parse(pendingDate).Date.Add(TimeSpan.Parse(pendingStartTime)),
                                EndTime = DateTime.Parse(pendingDate).Date.Add(TimeSpan.Parse(pendingEndTime)),
                                SlotNumber = pendingSlotNumber.Value,
                                UserSapId = pendingUserSapId,
                                BookedAt = DateTime.UtcNow
                            };
                            _context.TestBookings.Add(booking);
                            await _context.SaveChangesAsync();

                            // Send payment receipt email
                            await SendPaymentReceiptEmail(userId.Value, pendingTestId.Value, booking, payment);
                        }
                        else if (existingBooking != null)
                        {
                            await _context.SaveChangesAsync();
                            // Send payment receipt email for existing booking
                            await SendPaymentReceiptEmail(userId.Value, pendingTestId.Value, existingBooking, payment);
                        }
                        else
                        {
                            await _context.SaveChangesAsync();
                            _logger.LogWarning("Could not create booking or find existing booking for payment receipt email");
                        }

                        _logger.LogInformation("[TRACE] Payment record created for user {UserId} via GET success callback", userId.Value);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error creating payment record in Success GET handler for user {UserId}", userId);
                    }
                }

                if (pendingTestId.HasValue)
                {
                    TempData["BookedTestId"] = pendingTestId.Value;
                    _logger.LogInformation("Stored BookedTestId in TempData: {TestId}", pendingTestId);
                    ViewBag.TestId = pendingTestId.Value;
                }
                if (!string.IsNullOrEmpty(pendingDate))
                {
                    TempData["BookedDate"] = pendingDate;
                    _logger.LogInformation("Stored BookedDate in TempData: {Date}", pendingDate);
                }
                if (!string.IsNullOrEmpty(pendingStartTime))
                {
                    TempData["BookedStartTime"] = pendingStartTime;
                    _logger.LogInformation("Stored BookedStartTime in TempData: {StartTime}", pendingStartTime);
                }
                if (!string.IsNullOrEmpty(pendingEndTime))
                {
                    TempData["BookedEndTime"] = pendingEndTime;
                    _logger.LogInformation("Stored BookedEndTime in TempData: {EndTime}", pendingEndTime);
                }
                if (pendingSlotNumber.HasValue)
                {
                    TempData["BookedSlotNumber"] = pendingSlotNumber.Value;
                    _logger.LogInformation("Stored BookedSlotNumber in TempData: {SlotNumber}", pendingSlotNumber.Value);
                }
                if (!string.IsNullOrEmpty(pendingUserSapId))
                {
                    TempData["BookedUserSapId"] = pendingUserSapId;
                    _logger.LogInformation("Stored BookedUserSapId in TempData: {UserSapId}", pendingUserSapId);
                }

                // Set TempData.JustPaid to true to indicate a successful payment
                TempData["JustPaid"] = true;

                // Set ViewBag.RedirectUrl for the Success view to redirect to MyBookings with fromPayment=true
                if (pendingTestId != null)
                {
                    ViewBag.RedirectUrl = Url.Action("MyBookings", "Test", new { testId = pendingTestId, fromPayment = true });
                    _logger.LogInformation($"Set redirect URL to: {ViewBag.RedirectUrl}");
                }
                else
                {
                    ViewBag.RedirectUrl = Url.Action("MyBookings", "Test", new { fromPayment = true });
                    _logger.LogInformation($"Set redirect URL to: {ViewBag.RedirectUrl}");
                }

                // Clean up payment session values
                HttpContext.Session.Remove("PendingTestId");
                HttpContext.Session.Remove("PendingDate");
                HttpContext.Session.Remove("PendingStartTime");
                HttpContext.Session.Remove("PendingEndTime");
                HttpContext.Session.Remove("PendingSlotNumber");
                HttpContext.Session.Remove("PendingUserSapId");
                HttpContext.Session.Remove("IsReattempt");
                _logger.LogInformation("[SessionCleanup] Cleared all payment-related session values after payment success GET.");

                return View("Success");
            }
        }

        [AcceptVerbs("GET", "POST")]
        [Route("Payment/Failure")]
        [AllowAnonymous] // Allow anonymous access for PayU callbacks and redirects
        public IActionResult Failure()
        {
            if (HttpContext.Request.Method == "POST")
            {
                _logger.LogWarning("Payment failure callback received from PayU via POST");
                TempData["ErrorMessage"] = "Payment was not successful. Please try booking a slot again.";
                return View("Failure");
            }
            else
            {
                _logger.LogWarning("Payment failure callback received from PayU via GET");
                TempData["ErrorMessage"] = "Payment was not successful. Please try booking a slot again.";
                return View("Failure");
            }
        }

        public async Task<IActionResult> ProcessSuccessfulPayment()
        {
            _logger.LogInformation("[TRACE] Entered ProcessSuccessfulPayment");
            try
            {
                // Retrieve userId from Session instead of TempData
                int? userIdIntNullable = HttpContext.Session.GetInt32("PendingUserId");
                _logger.LogInformation("[TRACE] Retrieved userId from Session: {UserId}", userIdIntNullable);
                if (!userIdIntNullable.HasValue)
                {
                    _logger.LogInformation("[TRACE] userId is null or empty, or not a valid integer (from Session)");
                }
                // Retrieve all session values
                int? testId = HttpContext.Session.GetInt32("PendingTestId");
                string pendingDate = HttpContext.Session.GetString("PendingDate");
                string pendingStartTime = HttpContext.Session.GetString("PendingStartTime");
                string pendingEndTime = HttpContext.Session.GetString("PendingEndTime");
                int? slotNumber = HttpContext.Session.GetInt32("PendingSlotNumber");
                string userSapId = HttpContext.Session.GetString("PendingUserSapId");
                int? userId = HttpContext.Session.GetInt32("PendingUserId");

                // Validate all values
                if (!testId.HasValue || string.IsNullOrEmpty(pendingDate) || string.IsNullOrEmpty(pendingStartTime) ||
                    string.IsNullOrEmpty(pendingEndTime) || !slotNumber.HasValue || !userId.HasValue)
                {
                    _logger.LogWarning("Session data missing in ProcessSuccessfulPayment. Checking for query parameters.");

                    // Try to recover from query parameters if available
                    var queryTestId = HttpContext.Request.Query["testId"].ToString();
                    if (!string.IsNullOrEmpty(queryTestId) && int.TryParse(queryTestId, out int recoveredTestId))
                    {
                        _logger.LogInformation("Recovered testId {TestId} from query parameters", recoveredTestId);

                        // Get the user ID from claims as a fallback
                        var userIdFromClaims = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                        if (!string.IsNullOrEmpty(userIdFromClaims) && int.TryParse(userIdFromClaims, out int userIdInt))
                        {
                            _logger.LogInformation("Using user ID {UserId} from claims", userIdInt);

                            // Create a minimal success response
                            TempData["SuccessMessage"] = "Payment successful! Your booking has been confirmed.";
                            return RedirectToAction("MyBookings", "Test", new { testId = recoveredTestId, fromPayment = true });
                        }
                    }

                    // If recovery failed, show the error
                    TempData["ErrorMessage"] = "Session expired or invalid. Please try booking again.";
                    return RedirectToAction("Index", "Test", new { error = TempData["ErrorMessage"] });
                }

                // Check if this is a reattempt booking (retake)
                bool isReattempt = false;
                var isReattemptSession = HttpContext.Session.GetString("IsReattempt");
                if (!string.IsNullOrEmpty(isReattemptSession) && isReattemptSession.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    isReattempt = true;
                }

                // Check if booking already exists
                var existingBooking = await _context.TestBookings
                    .FirstOrDefaultAsync(tb => tb.TestId == testId.Value && tb.UserId == userId.Value);

                if (existingBooking == null || isReattempt)
                {
                    // Always create a new booking if this is a reattempt (retake), even if an existing booking exists
                    var booking = new TestBooking
                    {
                        TestId = testId.Value,
                        UserId = userId.Value,
                        BookingDate = DateTime.Parse(pendingDate),
                        StartTime = DateTime.Parse(pendingDate).Date.Add(TimeSpan.Parse(pendingStartTime)),
                        EndTime = DateTime.Parse(pendingDate).Date.Add(TimeSpan.Parse(pendingEndTime)),
                        SlotNumber = slotNumber.Value,
                        UserSapId = userSapId,
                        BookedAt = DateTime.UtcNow
                    };
                    _context.TestBookings.Add(booking);

                    // Create a payment record
                    var payment = new Payment
                    {
                        UserId = userId.Value,
                        Amount = 1.00M, // Use the same amount as in the Index action
                        Currency = "INR",
                        Status = "Completed",
                        CreatedAt = DateTime.UtcNow,
                        PaidAt = DateTime.UtcNow
                    };
                    _context.Payments.Add(payment);

                    await _context.SaveChangesAsync();
                    _logger.LogInformation("[TRACE] Booking and payment created successfully for user {UserId}, test {TestId} (isReattempt={IsReattempt})", userId.Value, testId.Value, isReattempt);

                    // Send payment receipt email
                    await SendPaymentReceiptEmail(userId.Value, testId.Value, booking, payment);
                }
                else
                {
                    // Even if booking exists, still create a payment record for this transaction
                    var payment = new Payment
                    {
                        UserId = userId.Value,
                        Amount = 1.00M,
                        Currency = "INR",
                        Status = "Completed",
                        CreatedAt = DateTime.UtcNow,
                        PaidAt = DateTime.UtcNow
                    };
                    _context.Payments.Add(payment);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation("[TRACE] Booking already exists for user {UserId}, test {TestId} (not a reattempt), but payment record created", userId.Value, testId.Value);

                    // Send payment receipt email for existing booking
                    await SendPaymentReceiptEmail(userId.Value, testId.Value, existingBooking, payment);
                }

                TempData["SuccessMessage"] = "Payment successful! Your slot is booked.";
                return RedirectToAction("MyBookings", "Test", new { fromPayment = true });
            }
            finally
            {
                // Clean up all payment-related session values after processing
                HttpContext.Session.Remove("PendingTestId");
                HttpContext.Session.Remove("PendingDate");
                HttpContext.Session.Remove("PendingStartTime");
                HttpContext.Session.Remove("PendingEndTime");
                HttpContext.Session.Remove("PendingSlotNumber");
                HttpContext.Session.Remove("PendingUserSapId");
                HttpContext.Session.Remove("IsReattempt");
                _logger.LogInformation("[SessionCleanup] Cleared all payment-related session values after payment processing.");
            }
            // ... (rest of your logic)
        }

        [HttpGet]
        [Route("Payment/TestPayU")]
        [AllowAnonymous] // Allow anonymous access for testing
        public IActionResult TestPayU()
        {
            try
            {
                // Generate test data
                string txnid = _payUService.GenerateTransactionId();
                string amount = "1.00";
                string testId = "123"; // Test ID for testing
                string productinfo = $"TestBooking_{testId}";
                string firstname = "Test User";
                string email = "test@example.com";
                string phone = "9999999999";

                // Use the helper to prepare the request with testId
                var payUParams = _payUService.PreparePayURequest(txnid, amount, productinfo, firstname, email, phone, testId);

                // Create a model for the view
                var model = new PayURequestModel
                {
                    Parameters = payUParams
                };

                // Log the test payment request
                _logger.LogInformation($"TEST PayU payment: TxnID={txnid}, Amount={amount}, Product={productinfo}");

                // Get the current configuration for debugging
                var config = PayUHelper.GetCurrentConfiguration();

                // Return the parameters as JSON for testing
                return Json(new {
                    success = true,
                    message = "PayU test successful",
                    payUParams = payUParams,
                    baseUrl = payUParams["payuUrl"],
                    successUrl = payUParams["surl"],
                    failureUrl = payUParams["furl"],
                    configuration = config
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing PayU integration");
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        private async Task SendPaymentReceiptEmail(int userId, int testId, TestBooking booking, Payment payment)
        {
            try
            {
                // Get user details
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning("Cannot send payment receipt email: User not found with ID {UserId}", userId);
                    return;
                }

                // Get test details
                var test = await _context.Tests.FindAsync(testId);
                if (test == null)
                {
                    _logger.LogWarning("Cannot send payment receipt email: Test not found with ID {TestId}", testId);
                    return;
                }

                // Format dates and times
                string bookingDate = booking.BookingDate.ToString("dddd, MMMM d, yyyy");
                string startTime = booking.StartTime.ToString("h:mm tt");
                string endTime = booking.EndTime.ToString("h:mm tt");

                // Generate a transaction ID using the helper method
                string transactionId = $"TXN-{payment.Id}-{_payUService.GenerateTransactionId().Substring(0, 10)}";

                // Send the email
                await _emailService.SendPaymentReceiptEmailAsync(
                    user.Email,
                    user.Username,
                    payment.Amount,
                    test.Title,
                    bookingDate,
                    startTime,
                    endTime,
                    transactionId
                );

                _logger.LogInformation("Payment receipt email sent to {Email} for test {TestId}", user.Email, testId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending payment receipt email for user {UserId} and test {TestId}", userId, testId);
            }
        }
    }
}