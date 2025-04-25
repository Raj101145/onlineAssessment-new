using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OnlineAssessment.Web.Models;
using System.Security.Claims;

namespace OnlineAssessment.Web.Controllers
{
    [Authorize]
    public class UserProfileController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILogger<UserProfileController> _logger;

        public UserProfileController(AppDbContext context, ILogger<UserProfileController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

                if (string.IsNullOrEmpty(userId) || !int.TryParse(userId, out int userIdInt) || string.IsNullOrEmpty(userRole))
                {
                    return RedirectToAction("Login", "Auth");
                }

                // Handle differently based on role
                if (userRole == "Candidate")
                {
                    var user = await _context.Users.FindAsync(userIdInt);
                    if (user == null)
                    {
                        return NotFound();
                    }

                    return View(user);
                }
                else if (userRole == "Organization")
                {
                    var organization = await _context.Organizations.FindAsync(userIdInt);
                    if (organization == null)
                    {
                        return NotFound();
                    }

                    // Pass organization data to the view
                    ViewBag.Organization = organization;

                    // Create a minimal user object just for the view
                    var userViewModel = new User
                    {
                        Id = organization.Id,
                        Username = organization.Username,
                        Email = organization.Email,
                        Role = UserRole.Organization
                    };

                    return View(userViewModel);
                }

                return RedirectToAction("Login", "Auth");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user profile");
                return RedirectToAction("Index", "Home");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetUserInfo()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

                if (string.IsNullOrEmpty(userId) || !int.TryParse(userId, out int userIdInt) || string.IsNullOrEmpty(userRole))
                {
                    return Json(new { success = false, message = "User not authenticated" });
                }

                // Handle differently based on role
                if (userRole == "Candidate")
                {
                    var user = await _context.Users.FindAsync(userIdInt);
                    if (user == null)
                    {
                        return Json(new { success = false, message = "User not found" });
                    }

                    // For candidate users, include candidate-specific fields
                    return Json(new
                    {
                        success = true,
                        user = new
                        {
                            username = user.Username,
                            email = user.Email,
                            firstName = user.FirstName,
                            lastName = user.LastName,
                            photoUrl = user.PhotoUrl,
                            role = user.Role.ToString(),
                            keySkills = user.KeySkills,
                            employment = user.Employment,
                            education = user.Education,
                            category = user.Category
                        }
                    });
                }
                else if (userRole == "Organization")
                {
                    var organization = await _context.Organizations.FindAsync(userIdInt);
                    if (organization == null)
                    {
                        return Json(new { success = false, message = "Organization not found" });
                    }

                    // For organizations, return organization data
                    return Json(new
                    {
                        success = true,
                        user = new
                        {
                            username = organization.Username,
                            email = organization.Email,
                            role = "Organization"
                        },
                        organization = new
                        {
                            name = organization.Name,
                            contactPerson = organization.ContactPerson,
                            email = organization.Email,
                            phoneNumber = organization.PhoneNumber,
                            address = organization.Address,
                            website = organization.Website,
                            description = organization.Description,
                            logoUrl = organization.LogoUrl
                        }
                    });
                }

                // Fallback response
                return Json(new { success = false, message = "Invalid user role" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user info");
                return Json(new { success = false, message = "An error occurred" });
            }
        }
    }
}
