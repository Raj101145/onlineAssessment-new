using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OnlineAssessment.Web.Models;
using OnlineAssessment.Web.Services;
// OnlineAssessment.Web.Data namespace removed as it's no longer needed
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.FileProviders;
using OnlineAssessment.Web.Helpers;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to use only port 5058
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    try
    {
        // Use the port from configuration or default to 5058
        var kestrelUrl = builder.Configuration["Kestrel:Endpoints:Http:Url"];
        if (!string.IsNullOrEmpty(kestrelUrl))
        {
            // Extract port from URL if available
            var uri = new Uri(kestrelUrl);
            serverOptions.ListenAnyIP(uri.Port);
            Console.WriteLine($"Server bound to port {uri.Port} from configuration");
        }
        else
        {
            // Default to port 5058
            serverOptions.ListenAnyIP(5058);
            Console.WriteLine("Server bound to default port 5058");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error binding to port: {ex.Message}");
        Console.WriteLine("Please make sure port 5058 is available or update the configuration.");
        throw;
    }
});

// ✅ Load Configuration explicitly
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
var configuration = builder.Configuration;

// Configure EPPlus license
OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

// ✅ Ensure JWT Secret is Valid
var jwtSecret = configuration["JWT:Secret"];
if (string.IsNullOrEmpty(jwtSecret) || jwtSecret.Length < 16)
{
    throw new Exception("JWT Secret Key is invalid! Ensure it is at least 16 characters long.");
}

// ✅ Add Database Context (MySQL)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(configuration.GetConnectionString("DefaultConnection"),
        new MySqlServerVersion(new Version(8, 0, 32))));

// ✅ Configure CORS Policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy => policy
            .SetIsOriginAllowed(_ => true)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

// ✅ Add Authentication with JWT and Cookies
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.LoginPath = "/Auth/Login";
    options.LogoutPath = "/Auth/Logout";
    options.AccessDeniedPath = "/Auth/AccessDenied";

    // Read cookie settings from configuration if available
    var sameSiteSetting = builder.Configuration["Cookie:SameSite"];
    if (!string.IsNullOrEmpty(sameSiteSetting) && Enum.TryParse<SameSiteMode>(sameSiteSetting, true, out var sameSiteMode))
    {
        options.Cookie.SameSite = sameSiteMode;
    }
    else
    {
        // Default to Lax SameSite mode to allow redirects from payment gateway
        options.Cookie.SameSite = SameSiteMode.Lax;
    }

    // Read secure policy from configuration if available
    var securePolicy = builder.Configuration["Cookie:SecurePolicy"];
    if (!string.IsNullOrEmpty(securePolicy))
    {
        if (securePolicy.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.None;
        }
        else if (securePolicy.Equals("Always", StringComparison.OrdinalIgnoreCase))
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        }
        else if (securePolicy.Equals("SameAsRequest", StringComparison.OrdinalIgnoreCase))
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        }
    }
    else
    {
        // Only use secure cookies in production with HTTPS
        if (builder.Environment.IsProduction() && builder.Configuration["UseHttps"] == "true")
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        }
        else
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.None;
        }
    }
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidIssuer = configuration["JWT:Issuer"],
        ValidAudience = configuration["JWT:Audience"]
    };
});

// ✅ Add Authorization
builder.Services.AddAuthorization();

// ✅ Add Controllers with JSON options
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options => {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve;
        options.JsonSerializerOptions.MaxDepth = 64;
    });

// ✅ Add Session support
builder.Services.AddSession(options => {
    options.IdleTimeout = TimeSpan.FromMinutes(60); // Increased timeout for payment flow
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;

    // Read cookie settings from configuration if available
    var sameSiteSetting = builder.Configuration["Cookie:SameSite"];
    if (!string.IsNullOrEmpty(sameSiteSetting) && Enum.TryParse<SameSiteMode>(sameSiteSetting, true, out var sameSiteMode))
    {
        options.Cookie.SameSite = sameSiteMode;
    }
    else
    {
        // Default to Lax SameSite mode to allow redirects from payment gateway
        options.Cookie.SameSite = SameSiteMode.Lax;
    }

    // Read secure policy from configuration if available
    var securePolicy = builder.Configuration["Cookie:SecurePolicy"];
    if (!string.IsNullOrEmpty(securePolicy))
    {
        if (securePolicy.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.None;
        }
        else if (securePolicy.Equals("Always", StringComparison.OrdinalIgnoreCase))
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        }
        else if (securePolicy.Equals("SameAsRequest", StringComparison.OrdinalIgnoreCase))
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        }
    }
    else
    {
        // Only use secure cookies in production with HTTPS
        if (builder.Environment.IsProduction() && builder.Configuration["UseHttps"] == "true")
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        }
        else
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.None;
        }
    }
});

// Register OTP, Email, Password Reset, Rate Limiting, and PayU services
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
builder.Services.AddSingleton<IRateLimitingService, RateLimitingService>();
builder.Services.AddScoped<ISapIdGeneratorService, SapIdGeneratorService>();
builder.Services.AddScoped<PayUService>(); // <--- Register PayUService for DI

// Initialize PayUHelper with configuration from appsettings.json
PayUHelper.Initialize(builder.Configuration);

// SuperOrganizationSeeder removed as it's no longer needed

// Register background services
builder.Services.AddHostedService<ExpiredTestCleanupService>();

// ✅ Configure Swagger with JWT Support
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "OnlineAssessment API", Version = "v1" });

    // 🔹 Add JWT Authorization to Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter 'Bearer' followed by a space and your JWT token."
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            new string[] { }
        }
    });
});

var app = builder.Build();

// ✅ Configure Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "OnlineAssessment API v1"));
}

// Only use HTTPS redirection if UseHttps is true
if (builder.Configuration["UseHttps"] == "true")
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseCors("AllowAll");  // Enable CORS globally
app.UseSession();         // Enable Session Middleware
app.UseAuthentication();  // Enable Authentication Middleware
app.UseAuthorization();   // Enable Authorization Middleware

// Configure routes
app.MapControllerRoute(
    name: "test",
    pattern: "Test/{action=Index}/{id?}",
    defaults: new { controller = "Test" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Auth}/{action=Register}/{id?}");

app.MapControllerRoute(
    name: "payment",
    pattern: "Payment/{action}/{id?}",
    defaults: new { controller = "Payment" });

app.MapControllerRoute(
    name: "categoryQuestions",
    pattern: "CategoryQuestions/{action=Index}/{id?}",
    defaults: new { controller = "CategoryQuestions" });

app.MapControllers();

// Ensure uploads directory exists
var uploadsPath = Path.Combine(builder.Environment.WebRootPath, "uploads");
if (!Directory.Exists(uploadsPath))
{
    Directory.CreateDirectory(uploadsPath);
}

// Ensure profiles directory exists
var profilesPath = Path.Combine(uploadsPath, "profiles");
if (!Directory.Exists(profilesPath))
{
    Directory.CreateDirectory(profilesPath);
    Console.WriteLine("Created profiles directory: " + profilesPath);
}

// Add static file serving for uploads
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});

// Note: Super organization functionality is now handled through the IsSuperOrganization flag in the organizations table

app.Run();
