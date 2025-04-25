using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OnlineAssessment.Web.Models;

namespace OnlineAssessment.Web.Controllers
{
    [Authorize(Roles = "Organization")]
    public class CategoryQuestionsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILogger<CategoryQuestionsController> _logger;

        public CategoryQuestionsController(AppDbContext context, ILogger<CategoryQuestionsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: CategoryQuestions
        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId == null)
            {
                return Unauthorized();
            }

            var organizationId = int.Parse(userId);

            // First, check if there's a user record for this organization
            var userRecord = await _context.Users.FirstOrDefaultAsync(u => u.Id == organizationId);

            // If no user record exists, we need to create one
            if (userRecord == null)
            {
                // Get the organization details
                var organization = await _context.Organizations.FirstOrDefaultAsync(o => o.Id == organizationId);
                if (organization == null)
                {
                    return BadRequest("Organization not found");
                }

                // Create a new user record for this organization
                userRecord = new User
                {
                    Id = organizationId, // Use the same ID as the organization
                    Username = organization.Username,
                    Email = organization.Email,
                    PasswordHash = organization.PasswordHash,
                    Role = UserRole.Organization
                };

                // Add the user record to the database
                _context.Users.Add(userRecord);
                await _context.SaveChangesAsync();
                _logger.LogInformation($"Created user record for organization {organizationId}");
            }

            // In all places where categories are referenced, use the static AllowedCategories from CategoryQuestions
            // Example: For dropdowns, validation, etc.
            // If there are hardcoded category lists, replace with CategoryQuestions.AllowedCategories
            // If you want to enforce only these categories, add validation in Create/Edit actions
            // For example, in POST Create/Edit, add:
            // if (!CategoryQuestions.AllowedCategories.Contains(categoryQuestions.Category))
            //     ModelState.AddModelError("Category", "Invalid category selected.");
            // (Apply similar logic in API endpoints)
            // You may also want to update seeding logic or admin tools to use only these categories.
            var categoryQuestions = await _context.CategoryQuestions
                .Where(cq => cq.CreatedBy == organizationId && CategoryQuestions.AllowedCategories.Contains(cq.Category))
                .ToListAsync();

            return View(categoryQuestions);
        }

        // GET: CategoryQuestions/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: CategoryQuestions/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CategoryQuestions categoryQuestions)
        {
            if (ModelState.IsValid)
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userId == null)
                {
                    return Unauthorized();
                }

                var organizationId = int.Parse(userId);

                // First, check if there's a user record for this organization
                var userRecord = await _context.Users.FirstOrDefaultAsync(u => u.Id == organizationId);

                // If no user record exists, we need to create one
                if (userRecord == null)
                {
                    // Get the organization details
                    var organization = await _context.Organizations.FirstOrDefaultAsync(o => o.Id == organizationId);
                    if (organization == null)
                    {
                        ModelState.AddModelError("", "Organization not found");
                        return View(categoryQuestions);
                    }

                    // Create a new user record for this organization
                    userRecord = new User
                    {
                        Id = organizationId, // Use the same ID as the organization
                        Username = organization.Username,
                        Email = organization.Email,
                        PasswordHash = organization.PasswordHash,
                        Role = UserRole.Organization
                    };

                    // Add the user record to the database
                    _context.Users.Add(userRecord);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"Created user record for organization {organizationId}");
                }

                // Validate category
                if (!CategoryQuestions.AllowedCategories.Contains(categoryQuestions.Category))
                {
                    ModelState.AddModelError("Category", "Invalid category selected.");
                }

                if (ModelState.IsValid)
                {
                    categoryQuestions.CreatedBy = organizationId;
                    categoryQuestions.CreatedAt = DateTime.UtcNow;

                    _context.Add(categoryQuestions);
                    await _context.SaveChangesAsync();
                    return RedirectToAction(nameof(Index));
                }
            }
            return View(categoryQuestions);
        }

        // API endpoint for uploading category questions
        [HttpPost]
        [Route("api/CategoryQuestions/Upload")]
        public async Task<IActionResult> UploadQuestions([FromBody] CategoryQuestionsUploadDto uploadDto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(uploadDto.Category))
                {
                    return BadRequest(new { message = "Category is required" });
                }

                // Validate category
                if (!CategoryQuestions.AllowedCategories.Contains(uploadDto.Category))
                {
                    return BadRequest(new { message = "Invalid category selected." });
                }

                if (uploadDto.Questions == null || !uploadDto.Questions.Any())
                {
                    return BadRequest(new { message = "Questions are required" });
                }

                // Validate that there are at least 60 questions
                if (uploadDto.Questions.Count < 60)
                {
                    return BadRequest(new { message = "At least 60 questions are required in the question set" });
                }

                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userId == null)
                {
                    return Unauthorized();
                }

                var organizationId = int.Parse(userId);

                // First, check if there's a user record for this organization
                var userRecord = await _context.Users.FirstOrDefaultAsync(u => u.Id == organizationId);

                // If no user record exists, we need to create one
                if (userRecord == null)
                {
                    // Get the organization details
                    var organization = await _context.Organizations.FirstOrDefaultAsync(o => o.Id == organizationId);
                    if (organization == null)
                    {
                        return BadRequest(new { message = "Organization not found" });
                    }

                    // Create a new user record for this organization
                    userRecord = new User
                    {
                        Id = organizationId, // Use the same ID as the organization
                        Username = organization.Username,
                        Email = organization.Email,
                        PasswordHash = organization.PasswordHash,
                        Role = UserRole.Organization
                    };

                    // Add the user record to the database
                    _context.Users.Add(userRecord);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"Created user record for organization {organizationId}");
                }

                // Check if category questions already exist for this organization
                var existingCategoryQuestions = await _context.CategoryQuestions
                    .FirstOrDefaultAsync(cq => cq.Category == uploadDto.Category && cq.CreatedBy == organizationId);

                if (existingCategoryQuestions != null)
                {
                    // Update existing category questions
                    var options = new JsonSerializerOptions {
                        ReferenceHandler = ReferenceHandler.Preserve,
                        MaxDepth = 64
                    };
                    existingCategoryQuestions.QuestionsJson = JsonSerializer.Serialize(uploadDto.Questions, options);
                    _context.Update(existingCategoryQuestions);
                }
                else
                {
                    // Create new category questions
                    var categoryQuestions = new CategoryQuestions
                    {
                        Category = uploadDto.Category,
                        QuestionsJson = JsonSerializer.Serialize(uploadDto.Questions, new JsonSerializerOptions {
                            ReferenceHandler = ReferenceHandler.Preserve,
                            MaxDepth = 64
                        }),
                        CreatedBy = organizationId,
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.Add(categoryQuestions);
                }

                await _context.SaveChangesAsync();

                return Ok(new { message = "Questions uploaded successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading category questions");
                return StatusCode(500, new { message = "Error uploading questions: " + ex.Message });
            }
        }

        // API endpoint for getting category questions
        [HttpGet]
        [Route("api/CategoryQuestions/GetByCategory")]
        public async Task<IActionResult> GetQuestionsByCategory(string category)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(category))
                {
                    return BadRequest(new { message = "Category is required" });
                }

                // Validate category
                if (!CategoryQuestions.AllowedCategories.Contains(category))
                {
                    return BadRequest(new { message = "Invalid category selected." });
                }

                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userId == null)
                {
                    return Unauthorized();
                }

                var organizationId = int.Parse(userId);

                // First, check if there's a user record for this organization
                var userRecord = await _context.Users.FirstOrDefaultAsync(u => u.Id == organizationId);

                // If no user record exists, we need to create one
                if (userRecord == null)
                {
                    // Get the organization details
                    var organization = await _context.Organizations.FirstOrDefaultAsync(o => o.Id == organizationId);
                    if (organization == null)
                    {
                        return BadRequest(new { message = "Organization not found" });
                    }

                    // Create a new user record for this organization
                    userRecord = new User
                    {
                        Id = organizationId, // Use the same ID as the organization
                        Username = organization.Username,
                        Email = organization.Email,
                        PasswordHash = organization.PasswordHash,
                        Role = UserRole.Organization
                    };

                    // Add the user record to the database
                    _context.Users.Add(userRecord);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"Created user record for organization {organizationId}");
                }

                var categoryQuestions = await _context.CategoryQuestions
                    .FirstOrDefaultAsync(cq => cq.Category == category && cq.CreatedBy == organizationId);

                if (categoryQuestions == null)
                {
                    return NotFound(new { message = "No questions found for this category" });
                }

                var options = new JsonSerializerOptions {
                    ReferenceHandler = ReferenceHandler.Preserve,
                    MaxDepth = 64
                };
                var questions = JsonSerializer.Deserialize<List<QuestionDto>>(categoryQuestions.QuestionsJson, options);

                return Ok(new { questions });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting category questions");
                return StatusCode(500, new { message = "Error getting questions: " + ex.Message });
            }
        }

        // API endpoint for getting all categories
        [HttpGet]
        [Route("api/CategoryQuestions/GetCategories")]
        public async Task<IActionResult> GetCategories()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userId == null)
                {
                    return Unauthorized();
                }

                var organizationId = int.Parse(userId);

                // First, check if there's a user record for this organization
                var userRecord = await _context.Users.FirstOrDefaultAsync(u => u.Id == organizationId);

                // If no user record exists, we need to create one
                if (userRecord == null)
                {
                    // Get the organization details
                    var organization = await _context.Organizations.FirstOrDefaultAsync(o => o.Id == organizationId);
                    if (organization == null)
                    {
                        return BadRequest(new { message = "Organization not found" });
                    }

                    // Create a new user record for this organization
                    userRecord = new User
                    {
                        Id = organizationId, // Use the same ID as the organization
                        Username = organization.Username,
                        Email = organization.Email,
                        PasswordHash = organization.PasswordHash,
                        Role = UserRole.Organization
                    };

                    // Add the user record to the database
                    _context.Users.Add(userRecord);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"Created user record for organization {organizationId}");
                }

                var categories = await _context.CategoryQuestions
                    .Where(cq => cq.CreatedBy == organizationId && CategoryQuestions.AllowedCategories.Contains(cq.Category))
                    .Select(cq => cq.Category)
                    .ToListAsync();

                return Ok(new { categories });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting categories");
                return StatusCode(500, new { message = "Error getting categories: " + ex.Message });
            }
        }

        // DELETE: CategoryQuestions/Delete/5
        [HttpDelete]
        [Route("CategoryQuestions/Delete/{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userId == null)
                {
                    return Json(new { success = false, message = "Unauthorized: Organization access required." });
                }

                var organizationId = int.Parse(userId);

                // First, check if there's a user record for this organization
                var userRecord = await _context.Users.FirstOrDefaultAsync(u => u.Id == organizationId);

                // If no user record exists, we need to create one
                if (userRecord == null)
                {
                    // Get the organization details
                    var organization = await _context.Organizations.FirstOrDefaultAsync(o => o.Id == organizationId);
                    if (organization == null)
                    {
                        return Json(new { success = false, message = "Organization not found" });
                    }

                    // Create a new user record for this organization
                    userRecord = new User
                    {
                        Id = organizationId, // Use the same ID as the organization
                        Username = organization.Username,
                        Email = organization.Email,
                        PasswordHash = organization.PasswordHash,
                        Role = UserRole.Organization
                    };

                    // Add the user record to the database
                    _context.Users.Add(userRecord);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"Created user record for organization {organizationId}");
                }

                var categoryQuestions = await _context.CategoryQuestions.FindAsync(id);

                if (categoryQuestions == null)
                {
                    return Json(new { success = false, message = "Question set not found." });
                }

                // Verify ownership
                if (categoryQuestions.CreatedBy != organizationId)
                {
                    return Json(new { success = false, message = "You can only delete your own question sets." });
                }

                _context.CategoryQuestions.Remove(categoryQuestions);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Question set deleted successfully!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in Delete action for category questions {id}");
                return Json(new { success = false, message = "An unexpected error occurred while deleting the question set." });
            }
        }
    }
}
