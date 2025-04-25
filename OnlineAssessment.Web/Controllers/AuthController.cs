using BCrypt.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using OnlineAssessment.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using OnlineAssessment.Web.Services;

namespace OnlineAssessment.Web.Controllers
{
    public class AuthController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;
        private readonly IOtpService _otpService;
        private readonly IEmailService _emailService;
        private readonly IRateLimitingService _rateLimitingService;
        private readonly IPasswordResetService _passwordResetService;
        private readonly ISapIdGeneratorService _sapIdGeneratorService;

        public AuthController(AppDbContext context, IConfiguration config, IOtpService otpService,
            IEmailService emailService, IRateLimitingService rateLimitingService, IPasswordResetService passwordResetService,
            ISapIdGeneratorService sapIdGeneratorService)
        {
            _context = context;
            _config = config;
            _otpService = otpService;
            _emailService = emailService;
            _rateLimitingService = rateLimitingService;
            _passwordResetService = passwordResetService;
            _sapIdGeneratorService = sapIdGeneratorService;
        }

        // View action for registration page (candidates only)
        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        // View action for organization registration page
        [HttpGet]
        public IActionResult OrganizationRegister()
        {
            return View();
        }

        // View action for login page - redirects to OTP login for candidates
        [HttpGet]
        public IActionResult Login()
        {
            return RedirectToAction("OtpLogin");
        }

        // View action for organization login page
        [HttpGet]
        public IActionResult OrganizationLogin()
        {
            return View();
        }

        // View action for OTP login page
        [HttpGet]
        public IActionResult OtpLogin()
        {
            return View();
        }

        // Test email view
        [HttpGet]
        [Route("Auth/test-email-page")]
        public IActionResult TestEmailPage()
        {
            return View("TestEmail");
        }

        // Test endpoint for email sending
        [HttpGet]
        [Route("Auth/test-email")]
        public async Task<IActionResult> TestEmail(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return BadRequest("Email is required");
            }

            try
            {
                // Generate a test OTP
                string testOtp = "123456";

                // Send test email
                await _emailService.SendOtpEmailAsync(email, testOtp);

                return Ok($"Test email sent to {email} with OTP: {testOtp}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Failed to send email: {ex.Message}");
            }
        }

        // API endpoint for registration
        [HttpPost]
        [Route("Auth/api/register")]
        public async Task<IActionResult> RegisterApi([FromBody] RegisterRequest request)
        {
            return await RegisterUser(request);
        }

        // API endpoint for registration with file upload
        [HttpPost]
        [Route("Auth/api/register-with-file")]
        public async Task<IActionResult> RegisterWithFile([FromForm] RegisterRequest request, IFormFile? profilePicture)
        {
            // Handle profile picture upload if provided
            if (profilePicture != null && profilePicture.Length > 0)
            {
                // Save the file to wwwroot/uploads/profiles
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "profiles");

                // Create directory if it doesn't exist
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                // Generate unique filename
                var uniqueFileName = Guid.NewGuid().ToString() + "_" + profilePicture.FileName;
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await profilePicture.CopyToAsync(fileStream);
                }

                // Set the profile picture URL
                request.ProfilePicture = "/uploads/profiles/" + uniqueFileName;
            }

            return await RegisterUser(request);
        }

        // Common method for user registration
        private async Task<IActionResult> RegisterUser(RegisterRequest request)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.Username) ||
                string.IsNullOrWhiteSpace(request.Email))
            {
                return BadRequest(new { message = "Username and Email are required." });
            }

            if (await _context.Users.AnyAsync(u => u.Username.ToLower() == request.Username.ToLower()))
            {
                return BadRequest(new { message = "Username already exists. Please choose a different one." });
            }

            // Set default values for password and role if not provided
            if (string.IsNullOrWhiteSpace(request.Password))
            {
                // Generate a random password for candidates
                request.Password = Guid.NewGuid().ToString("N").Substring(0, 12);
            }

            if (string.IsNullOrWhiteSpace(request.Role))
            {
                // Default to Candidate if role is not specified
                request.Role = "Candidate";
            }

            // Validate role and convert to Enum
            if (!Enum.TryParse<UserRole>(request.Role, true, out var userRole) || userRole.ToString() == "Admin")
            {
                return BadRequest(new { message = "Invalid role provided. Allowed values: Organization, Candidate." });
            }

            // Hash the password securely
            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);

            // Parse enums if provided
            EmploymentStatus? employment = null;
            if (!string.IsNullOrWhiteSpace(request.Employment) &&
                Enum.TryParse<EmploymentStatus>(request.Employment, true, out var employmentStatus))
            {
                employment = employmentStatus;
            }

            EducationLevel? education = null;
            if (!string.IsNullOrWhiteSpace(request.Education) &&
                Enum.TryParse<EducationLevel>(request.Education, true, out var educationLevel))
            {
                education = educationLevel;
            }

            // Handle registration based on role
            User? user = null;
            Organization? organization = null;

            if (userRole == UserRole.Candidate)
            {
                // Generate a unique SAP ID for the user
                string sapId = await _sapIdGeneratorService.GenerateUniqueIdAsync();

                // For candidates, create a User record
                user = new User
                {
                    Username = request.Username,
                    Email = request.Email,
                    PasswordHash = hashedPassword,
                    Role = userRole,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    MobileNumber = request.MobileNumber,
                    PhotoUrl = request.ProfilePicture,
                    KeySkills = request.KeySkills,
                    Employment = employment,
                    Education = education,
                    Category = request.Category
                };

                // Try to set the SAP ID if the column exists
                try
                {
                    user.SapId = sapId;
                }
                catch
                {
                    // SapId column might not exist yet, ignore the error
                }

                _context.Users.Add(user);
                await _context.SaveChangesAsync();
            }
            else // Organization
            {
                // Generate a unique SAP ID for the organization
                string sapId = await _sapIdGeneratorService.GenerateUniqueIdAsync();

                // For organizations, create only an Organization record (no User record)
                organization = new Organization
                {
                    Username = request.Username,
                    PasswordHash = hashedPassword,
                    Name = request.OrganizationName ?? request.Username,
                    Email = request.Email,
                    ContactPerson = request.ContactPerson ?? request.Username,
                    PhoneNumber = request.MobileNumber,
                    Address = request.Address,
                    Website = request.Website,
                    Description = request.Description,
                    // LogoUrl field removed from organization registration
                    CreatedAt = DateTime.UtcNow,
                    UserId = null // Explicitly set UserId to null
                };

                // Try to set the SAP ID if the column exists
                try
                {
                    organization.SapId = sapId;
                }
                catch
                {
                    // SapId column might not exist yet, ignore the error
                }

                _context.Organizations.Add(organization);
                await _context.SaveChangesAsync();
            }

            // Generate JWT token for automatic login
            var jwtSecret = _config["JWT:Secret"];
            if (string.IsNullOrEmpty(jwtSecret) || Encoding.UTF8.GetBytes(jwtSecret).Length < 32)
            {
                return StatusCode(500, new { message = "JWT Secret is invalid or too short." });
            }

            // For candidates, we'll redirect to the payment page
            bool isCandidate = userRole == UserRole.Candidate;

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            // Create claims based on whether this is a user or organization
            Claim[] claims;
            string username;
            string email;
            string id;

            if (isCandidate && user != null)
            {
                // User claims for candidates
                username = user.Username;
                email = user.Email;
                id = user.Id.ToString();
                claims = new[]
                {
                    new Claim(ClaimTypes.Name, username),
                    new Claim(ClaimTypes.Role, "Candidate"),
                    new Claim(ClaimTypes.NameIdentifier, id),
                    new Claim(ClaimTypes.Email, email)
                };
            }
            else if (organization != null)
            {
                // Organization claims
                username = organization.Username;
                email = organization.Email;
                id = organization.Id.ToString();
                claims = new[]
                {
                    new Claim(ClaimTypes.Name, username),
                    new Claim(ClaimTypes.Role, "Organization"),
                    new Claim(ClaimTypes.NameIdentifier, id),
                    new Claim(ClaimTypes.Email, email),
                    new Claim("OrganizationName", organization.Name)
                };
            }
            else
            {
                return StatusCode(500, new { message = "Error creating authentication token." });
            }

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(1),
                Issuer = _config["JWT:Issuer"],
                Audience = _config["JWT:Audience"],
                SigningCredentials = credentials
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            var tokenString = tokenHandler.WriteToken(token);

            // Set authentication cookie
            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            if (isCandidate)
            {
                return Ok(new {
                    message = "User registered and logged in successfully.",
                    token = tokenString,
                    redirectUrl = "/Test"
                });
            }
            else
            {
                return Ok(new {
                    message = "Organization registered and logged in successfully.",
                    token = tokenString,
                    redirectUrl = "/Test"
                });
            }
        }

        // API endpoint for login
        [HttpPost]
        [Route("Auth/login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { message = "Email and password are required." });
            }

            try
            {
                // First check if this is a candidate user
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower());
                if (user != null)
                {
                    // For candidates, redirect to OTP login
                    if (user.Role == UserRole.Candidate)
                    {
                        return BadRequest(new { message = "Candidates must use OTP login.", redirectUrl = "/Auth/OtpLogin" });
                    }
                }

                // If not a candidate or user not found, check for organization
                var organization = await _context.Organizations.FirstOrDefaultAsync(o => o.Email.ToLower() == request.Email.ToLower());
                if (organization == null)
                {
                    // Neither user nor organization found with this email
                    return BadRequest(new { message = "Invalid email or password." });
                }

                // Verify password for organization
                bool isPasswordValid = BCrypt.Net.BCrypt.Verify(request.Password, organization.PasswordHash);
                if (!isPasswordValid)
                {
                    return BadRequest(new { message = "Invalid email or password." });
                }

                // Generate JWT token
                var jwtSecret = _config["JWT:Secret"];
                if (string.IsNullOrEmpty(jwtSecret) || Encoding.UTF8.GetBytes(jwtSecret).Length < 32)
                {
                    return StatusCode(500, new { message = "JWT Secret is invalid or too short." });
                }

                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
                var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                var claims = new[]
                {
                    new Claim(ClaimTypes.Name, organization.Username),
                    new Claim(ClaimTypes.Role, "Organization"),
                    new Claim(ClaimTypes.NameIdentifier, organization.Id.ToString()),
                    new Claim(ClaimTypes.Email, organization.Email),
                    new Claim("OrganizationName", organization.Name)
                };

                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(claims),
                    Expires = DateTime.UtcNow.AddHours(1),
                    Issuer = _config["JWT:Issuer"],
                    Audience = _config["JWT:Audience"],
                    SigningCredentials = credentials
                };

                var tokenHandler = new JwtSecurityTokenHandler();
                var token = tokenHandler.CreateToken(tokenDescriptor);
                var tokenString = tokenHandler.WriteToken(token);

                // Set authentication cookie
                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
                };

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties);

                // Update last login time for organization
                organization.LastLoginAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return Ok(new { token = tokenString, redirectUrl = "/Test" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred during login." });
            }
        }

        [HttpPost]
        [Route("Auth/logout")]
        public async Task<IActionResult> Logout()
        {
            try
            {
                // Get the current user role before logout
                bool isSuperAdmin = User.IsInRole("Admin");
                bool isOrganization = User.IsInRole("Organization");

                // Clear all authentication cookies
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                // Clear any existing cookies
                foreach (var cookie in Request.Cookies.Keys)
                {
                    Response.Cookies.Delete(cookie);
                }

                // Return success response with redirect URL based on user role
                if (isSuperAdmin)
                {
                    return Ok(new { message = "Logged out successfully", redirectUrl = "/SuperAdmin/Login" });
                }
                else if (isOrganization)
                {
                    return Ok(new { message = "Logged out successfully", redirectUrl = "/Auth/OrganizationLogin" });
                }
                else
                {
                    return Ok(new { message = "Logged out successfully", redirectUrl = "/Auth/Register" });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred during logout" });
            }
        }

        // Super Admin Login endpoint
        [HttpPost]
        [Route("Auth/SuperAdminLogin")]
        public async Task<IActionResult> SuperAdminLogin([FromBody] LoginRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { success = false, message = "Email and password are required." });
            }

            try
            {
                // Check if this is the super admin account
                if (request.Email.ToLower() == "admin@system.com" && request.Password == "admin123")
                {
                    // Generate JWT token
                    var jwtSecret = _config["JWT:Secret"];
                    if (string.IsNullOrEmpty(jwtSecret) || Encoding.UTF8.GetBytes(jwtSecret).Length < 32)
                    {
                        return StatusCode(500, new { success = false, message = "JWT Secret is invalid or too short." });
                    }

                    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
                    var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                    var claims = new[]
                    {
                        new Claim(ClaimTypes.Name, "Admin"),
                        new Claim(ClaimTypes.Role, "Admin"),
                        new Claim(ClaimTypes.Email, request.Email)
                    };

                    var tokenDescriptor = new SecurityTokenDescriptor
                    {
                        Subject = new ClaimsIdentity(claims),
                        Expires = DateTime.UtcNow.AddHours(1),
                        Issuer = _config["JWT:Issuer"],
                        Audience = _config["JWT:Audience"],
                        SigningCredentials = credentials
                    };

                    var tokenHandler = new JwtSecurityTokenHandler();
                    var token = tokenHandler.CreateToken(tokenDescriptor);
                    var tokenString = tokenHandler.WriteToken(token);

                    // Set authentication cookie
                    var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    var authProperties = new AuthenticationProperties
                    {
                        IsPersistent = true,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
                    };

                    await HttpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        new ClaimsPrincipal(claimsIdentity),
                        authProperties);

                    return Ok(new { success = true, token = tokenString, redirectUrl = "/SuperAdmin/Dashboard" });
                }
                else
                {
                    return BadRequest(new { success = false, message = "Invalid email or password." });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "An error occurred during login." });
            }
        }



        // OTP Login - Request OTP
        [HttpPost]
        [Route("Auth/api/request-otp")]
        public async Task<IActionResult> RequestOtp([FromBody] OtpRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email))
            {
                return BadRequest(new { message = "Email is required." });
            }

            try
            {
                // Get client IP address
                string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                // Check rate limiting
                if (!_rateLimitingService.IsAllowed(request.Email, ipAddress))
                {
                    return StatusCode(429, new { message = "Too many requests. Please try again later." });
                }

                // Check if account exists (either user or organization)
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower());
                var organization = await _context.Organizations.FirstOrDefaultAsync(o => o.Email.ToLower() == request.Email.ToLower());

                if (user == null && organization == null)
                {
                    // Don't reveal that the account doesn't exist for security reasons
                    return Ok(new { message = "If your email is registered, you will receive an OTP shortly." });
                }

                // Record this attempt for rate limiting
                _rateLimitingService.RecordAttempt(request.Email, ipAddress);

                try
                {
                    // Generate OTP
                    var otp = await _otpService.GenerateOtpAsync(request.Email);

                    // Send OTP via email
                    await _emailService.SendOtpEmailAsync(request.Email, otp);

                    return Ok(new { message = "OTP has been sent to your email." });
                }
                catch (Exception ex)
                {
                    // Log the error
                    Console.Error.WriteLine($"OTP Generation Error: {ex.Message}");
                    Console.Error.WriteLine($"Stack Trace: {ex.StackTrace}");

                    // In development mode, return detailed error information
                    if (HttpContext.RequestServices.GetService<IWebHostEnvironment>().IsDevelopment())
                    {
                        return StatusCode(500, new { message = $"Error: {ex.Message}", stackTrace = ex.StackTrace });
                    }

                    return StatusCode(500, new { message = "An error occurred while generating OTP." });
                }
            }
            catch (Exception ex)
            {
                // Log the error
                Console.Error.WriteLine($"OTP Request Error: {ex.Message}");
                Console.Error.WriteLine($"Stack Trace: {ex.StackTrace}");

                // In development mode, return detailed error information
                if (HttpContext.RequestServices.GetService<IWebHostEnvironment>().IsDevelopment())
                {
                    return StatusCode(500, new { message = $"Error: {ex.Message}", stackTrace = ex.StackTrace });
                }

                return StatusCode(500, new { message = "An error occurred while processing your request." });
            }
        }

        // OTP Login - Verify OTP
        [HttpPost]
        [Route("Auth/api/verify-otp")]
        public async Task<IActionResult> VerifyOtp([FromBody] OtpVerificationRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.OtpCode))
            {
                return BadRequest(new { message = "Email and OTP code are required." });
            }

            try
            {
                try
                {
                    // Validate OTP
                    bool isValid = await _otpService.ValidateOtpAsync(request.Email, request.OtpCode);
                    if (!isValid)
                    {
                        return BadRequest(new { message = "Invalid or expired OTP." });
                    }
                }
                catch (Exception ex)
                {
                    // Log the error
                    Console.Error.WriteLine($"OTP Validation Error: {ex.Message}");
                    Console.Error.WriteLine($"Stack Trace: {ex.StackTrace}");

                    // In development mode, return detailed error information
                    if (HttpContext.RequestServices.GetService<IWebHostEnvironment>().IsDevelopment())
                    {
                        return StatusCode(500, new { message = $"Error: {ex.Message}", stackTrace = ex.StackTrace });
                    }

                    return StatusCode(500, new { message = "An error occurred while validating OTP." });
                }

                // Check if this is a user or organization
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower());
                var organization = await _context.Organizations.FirstOrDefaultAsync(o => o.Email.ToLower() == request.Email.ToLower());

                if (user == null && organization == null)
                {
                    return BadRequest(new { message = "Account not found." });
                }

                // Generate JWT token
                var jwtSecret = _config["JWT:Secret"];
                if (string.IsNullOrEmpty(jwtSecret) || Encoding.UTF8.GetBytes(jwtSecret).Length < 32)
                {
                    return StatusCode(500, new { message = "JWT Secret is invalid or too short." });
                }

                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
                var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                Claim[] claims;

                if (user != null) // This is a candidate user
                {
                    claims = new[]
                    {
                        new Claim(ClaimTypes.Name, user.Username),
                        new Claim(ClaimTypes.Role, user.Role.ToString()),
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                        new Claim(ClaimTypes.Email, user.Email)
                    };
                }
                else // This is an organization
                {
                    claims = new[]
                    {
                        new Claim(ClaimTypes.Name, organization.Username),
                        new Claim(ClaimTypes.Role, "Organization"),
                        new Claim(ClaimTypes.NameIdentifier, organization.Id.ToString()),
                        new Claim(ClaimTypes.Email, organization.Email),
                        new Claim("OrganizationName", organization.Name)
                    };

                    // Update last login time for organization
                    organization.LastLoginAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }

                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(claims),
                    Expires = DateTime.UtcNow.AddHours(1),
                    Issuer = _config["JWT:Issuer"],
                    Audience = _config["JWT:Audience"],
                    SigningCredentials = credentials
                };

                var tokenHandler = new JwtSecurityTokenHandler();
                var token = tokenHandler.CreateToken(tokenDescriptor);
                var tokenString = tokenHandler.WriteToken(token);

                // Set authentication cookie
                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
                };

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties);

                return Ok(new { token = tokenString, redirectUrl = "/Test" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while processing your request." });
            }
        }

        // View action for forgot password page
        [HttpGet]
        [Route("Auth/ForgotPassword")]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        // View action for reset password page
        [HttpGet]
        [Route("Auth/ResetPassword")]
        public IActionResult ResetPassword(string email, string token)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
            {
                return RedirectToAction("ForgotPassword");
            }

            ViewBag.Email = email;
            ViewBag.Token = token;
            return View();
        }

        // API endpoint for requesting password reset
        [HttpPost]
        [Route("Auth/api/forgot-password")]
        public async Task<IActionResult> ForgotPasswordApi([FromBody] ForgotPasswordRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email))
            {
                return BadRequest(new { message = "Email is required." });
            }

            try
            {
                // Get client IP address for rate limiting
                string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                // Check rate limiting
                if (!_rateLimitingService.IsAllowed(request.Email, ipAddress))
                {
                    return StatusCode(429, new { message = "Too many requests. Please try again later." });
                }

                // Check if user exists and is an organization
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower());
                if (user == null || user.Role != UserRole.Organization)
                {
                    // Don't reveal that the user doesn't exist for security reasons
                    return Ok(new { message = "If your email is registered, you will receive a password reset link shortly." });
                }

                // Record this attempt for rate limiting
                _rateLimitingService.RecordAttempt(request.Email, ipAddress);

                // Generate password reset token
                var token = await _passwordResetService.GeneratePasswordResetTokenAsync(request.Email);

                // Create reset URL
                var resetUrl = $"{Request.Scheme}://{Request.Host}/Auth/ResetPassword?email={Uri.EscapeDataString(request.Email)}&token={Uri.EscapeDataString(token)}";

                // Send password reset email
                await _emailService.SendPasswordResetEmailAsync(request.Email, token, resetUrl);

                return Ok(new { message = "Password reset instructions have been sent to your email." });
            }
            catch (Exception ex)
            {
                // Log the error
                Console.Error.WriteLine($"Password Reset Request Error: {ex.Message}");
                Console.Error.WriteLine($"Stack Trace: {ex.StackTrace}");

                // In development mode, return detailed error information
                if (HttpContext.RequestServices.GetService<IWebHostEnvironment>().IsDevelopment())
                {
                    return StatusCode(500, new { message = $"Error: {ex.Message}", stackTrace = ex.StackTrace });
                }

                return StatusCode(500, new { message = "An error occurred while processing your request." });
            }
        }

        // API endpoint for resetting password
        [HttpPost]
        [Route("Auth/api/reset-password")]
        public async Task<IActionResult> ResetPasswordApi([FromBody] ResetPasswordRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.NewPassword))
            {
                return BadRequest(new { message = "Email, token, and new password are required." });
            }

            try
            {
                // Validate password strength
                if (request.NewPassword.Length < 8)
                {
                    return BadRequest(new { message = "Password must be at least 8 characters long." });
                }

                // Reset the password
                bool success = await _passwordResetService.ResetPasswordAsync(request.Email, request.Token, request.NewPassword);
                if (!success)
                {
                    return BadRequest(new { message = "Invalid or expired token." });
                }

                return Ok(new { message = "Password has been reset successfully. You can now log in with your new password." });
            }
            catch (Exception ex)
            {
                // Log the error
                Console.Error.WriteLine($"Password Reset Error: {ex.Message}");
                Console.Error.WriteLine($"Stack Trace: {ex.StackTrace}");

                // In development mode, return detailed error information
                if (HttpContext.RequestServices.GetService<IWebHostEnvironment>().IsDevelopment())
                {
                    return StatusCode(500, new { message = $"Error: {ex.Message}", stackTrace = ex.StackTrace });
                }

                return StatusCode(500, new { message = "An error occurred while processing your request." });
            }
        }
    }

    // Request models
    public class ForgotPasswordRequest
    {
        public string Email { get; set; }
    }

    public class ResetPasswordRequest
    {
        public string Email { get; set; }
        public string Token { get; set; }
        public string NewPassword { get; set; }
    }
}
