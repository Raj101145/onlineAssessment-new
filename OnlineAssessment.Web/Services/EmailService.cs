using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace OnlineAssessment.Web.Services
{
    public interface IEmailService
    {
        Task SendOtpEmailAsync(string email, string otp);
        Task SendPasswordResetEmailAsync(string email, string resetToken, string resetUrl);
        Task SendPaymentReceiptEmailAsync(string email, string name, decimal amount, string testName, string bookingDate, string startTime, string endTime, string transactionId);
    }

    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;
        private readonly bool _isDevelopment;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger, IWebHostEnvironment env)
        {
            _configuration = configuration;
            _logger = logger;
            _isDevelopment = env.IsDevelopment();
        }

        private SmtpClient GetSmtpClient()
        {
            try
            {
                var smtpSettings = _configuration.GetSection("SmtpSettings");
                var smtpServer = smtpSettings["Server"];
                var smtpPortStr = smtpSettings["Port"];
                var smtpUsername = smtpSettings["Username"];
                var smtpPassword = smtpSettings["Password"];
                var enableSslStr = smtpSettings["EnableSsl"];

                // Check for missing settings
                if (string.IsNullOrEmpty(smtpServer) || string.IsNullOrEmpty(smtpPortStr) ||
                    string.IsNullOrEmpty(smtpUsername) || string.IsNullOrEmpty(smtpPassword))
                {
                    _logger.LogError("SMTP settings are incomplete. Check your configuration.");
                    throw new InvalidOperationException("SMTP settings are incomplete");
                }

                // Parse settings with error handling
                if (!int.TryParse(smtpPortStr, out int smtpPort))
                {
                    _logger.LogError($"Invalid SMTP port: {smtpPortStr}");
                    throw new InvalidOperationException("Invalid SMTP port");
                }

                bool enableSsl = true; // Default to true for security
                if (!string.IsNullOrEmpty(enableSslStr))
                {
                    if (!bool.TryParse(enableSslStr, out enableSsl))
                    {
                        _logger.LogWarning($"Invalid EnableSsl value: {enableSslStr}, defaulting to true");
                    }
                }

                var client = new SmtpClient(smtpServer, smtpPort)
                {
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(smtpUsername, smtpPassword),
                    EnableSsl = enableSsl
                };

                return client;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error creating SMTP client: {ex.Message}");
                throw;
            }
        }

        private MailAddress GetSenderAddress()
        {
            try
            {
                var smtpSettings = _configuration.GetSection("SmtpSettings");
                var senderEmail = smtpSettings["SenderEmail"];
                var senderName = smtpSettings["SenderName"];

                // Check for missing settings
                if (string.IsNullOrEmpty(senderEmail))
                {
                    _logger.LogError("Sender email is missing in SMTP settings");
                    throw new InvalidOperationException("Sender email is missing in SMTP settings");
                }

                // Use a default sender name if not specified
                if (string.IsNullOrEmpty(senderName))
                {
                    senderName = "Online Assessment";
                }

                return new MailAddress(senderEmail, senderName);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error creating sender address: {ex.Message}");
                throw;
            }
        }

        public async Task SendOtpEmailAsync(string email, string otp)
        {
            try
            {
                // Always log the OTP in development mode for debugging
                if (_isDevelopment)
                {
                    _logger.LogInformation($"[DEV MODE] OTP for {email}: {otp}");
                    // Continue to send email even in development mode
                }

                // Check if SMTP settings are configured
                var smtpSettings = _configuration.GetSection("SmtpSettings");
                if (string.IsNullOrEmpty(smtpSettings["Server"]) ||
                    string.IsNullOrEmpty(smtpSettings["Username"]) ||
                    string.IsNullOrEmpty(smtpSettings["Password"]))
                {
                    _logger.LogWarning("SMTP settings are not properly configured. Email sending skipped.");
                    return;
                }

                using (var client = GetSmtpClient())
                {
                    var mailMessage = new MailMessage
                    {
                        From = GetSenderAddress(),
                        Subject = "Your OTP Code for Online Assessment",
                        IsBodyHtml = true
                    };

                    // Create a professional HTML email template
                    string htmlBody = $@"<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #3498db; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .otp-code {{ font-size: 24px; font-weight: bold; text-align: center; padding: 15px; background-color: #eee; margin: 20px 0; letter-spacing: 5px; }}
        .footer {{ text-align: center; padding: 20px; font-size: 12px; color: #777; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>Online Assessment</h1>
        </div>
        <div class='content'>
            <p>Hello,</p>
            <p>You have requested to log in using a one-time password (OTP). Please use the following code to complete your login:</p>
            <div class='otp-code'>{otp}</div>
            <p>This code will expire in 10 minutes.</p>
            <p>If you did not request this code, please ignore this email.</p>
        </div>
        <div class='footer'>
            <p>This is an automated message, please do not reply to this email.</p>
            <p>&copy; {DateTime.Now.Year} Online Assessment. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";

                    mailMessage.Body = htmlBody;
                    mailMessage.To.Add(email);
                    await client.SendMailAsync(mailMessage);

                    _logger.LogInformation($"OTP email sent successfully to {email}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send OTP email: {ex.Message}");
                _logger.LogError($"Exception details: {ex.ToString()}");

                // Log SMTP settings (without password) for debugging
                var smtpSettings = _configuration.GetSection("SmtpSettings");
                _logger.LogError($"SMTP Server: {smtpSettings["Server"]}");
                _logger.LogError($"SMTP Port: {smtpSettings["Port"]}");
                _logger.LogError($"SMTP Username: {smtpSettings["Username"]}");
                _logger.LogError($"SMTP SenderEmail: {smtpSettings["SenderEmail"]}");

                throw;
            }
        }

        public async Task SendPasswordResetEmailAsync(string email, string resetToken, string resetUrl)
        {
            try
            {
                // Always log the reset token in development mode for debugging
                if (_isDevelopment)
                {
                    _logger.LogInformation($"[DEV MODE] Password Reset Token for {email}: {resetToken}");
                    _logger.LogInformation($"[DEV MODE] Reset URL: {resetUrl}");
                    // Continue to send email even in development mode
                }

                // Check if SMTP settings are configured
                var smtpSettings = _configuration.GetSection("SmtpSettings");
                if (string.IsNullOrEmpty(smtpSettings["Server"]) ||
                    string.IsNullOrEmpty(smtpSettings["Username"]) ||
                    string.IsNullOrEmpty(smtpSettings["Password"]))
                {
                    _logger.LogWarning("SMTP settings are not properly configured. Email sending skipped.");
                    return;
                }

                using (var client = GetSmtpClient())
                {
                    var mailMessage = new MailMessage
                    {
                        From = GetSenderAddress(),
                        Subject = "Reset Your Password - Online Assessment",
                        IsBodyHtml = true
                    };

                    // Create a professional HTML email template for password reset
                    string htmlBody = $@"<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #3498db; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .button {{ display: inline-block; padding: 10px 20px; background-color: #3498db; color: white; text-decoration: none; border-radius: 4px; font-weight: bold; margin: 20px 0; }}
        .footer {{ text-align: center; padding: 20px; font-size: 12px; color: #777; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>Online Assessment</h1>
        </div>
        <div class='content'>
            <p>Hello,</p>
            <p>You have requested to reset your password. Please click the button below to set a new password:</p>
            <p style='text-align: center;'>
                <a href='{resetUrl}' class='button'>Reset Password</a>
            </p>
            <p>This link will expire in 1 hour.</p>
            <p>If you did not request a password reset, please ignore this email or contact support if you have concerns.</p>
        </div>
        <div class='footer'>
            <p>This is an automated message, please do not reply to this email.</p>
            <p>&copy; {DateTime.Now.Year} Online Assessment. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";

                    mailMessage.Body = htmlBody;
                    mailMessage.To.Add(email);
                    await client.SendMailAsync(mailMessage);

                    _logger.LogInformation($"Password reset email sent successfully to {email}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send password reset email: {ex.Message}");
                _logger.LogError($"Exception details: {ex.ToString()}");

                // Log SMTP settings (without password) for debugging
                var smtpSettings = _configuration.GetSection("SmtpSettings");
                _logger.LogError($"SMTP Server: {smtpSettings["Server"]}");
                _logger.LogError($"SMTP Port: {smtpSettings["Port"]}");
                _logger.LogError($"SMTP Username: {smtpSettings["Username"]}");
                _logger.LogError($"SMTP SenderEmail: {smtpSettings["SenderEmail"]}");

                throw;
            }
        }

        public async Task SendPaymentReceiptEmailAsync(string email, string name, decimal amount, string testName, string bookingDate, string startTime, string endTime, string transactionId)
        {
            try
            {
                // Log payment details in development mode for debugging
                if (_isDevelopment)
                {
                    _logger.LogInformation($"[DEV MODE] Sending payment receipt to {email}");
                    _logger.LogInformation($"[DEV MODE] Payment details: Amount: {amount}, Test: {testName}, Date: {bookingDate}, Time: {startTime}-{endTime}");
                }

                // Check if SMTP settings are configured
                var smtpSettings = _configuration.GetSection("SmtpSettings");
                if (string.IsNullOrEmpty(smtpSettings["Server"]) ||
                    string.IsNullOrEmpty(smtpSettings["Username"]) ||
                    string.IsNullOrEmpty(smtpSettings["Password"]))
                {
                    _logger.LogWarning("SMTP settings are not properly configured. Payment receipt email sending skipped.");
                    return;
                }

                using (var client = GetSmtpClient())
                {
                    var mailMessage = new MailMessage
                    {
                        From = GetSenderAddress(),
                        Subject = "Payment Receipt - Online Assessment Booking",
                        IsBodyHtml = true
                    };

                    // Create a professional HTML email template for payment receipt
                    string htmlBody = $@"<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #3498db; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .receipt {{ border: 1px solid #ddd; padding: 15px; margin: 20px 0; background-color: white; }}
        .receipt-header {{ border-bottom: 1px solid #ddd; padding-bottom: 10px; margin-bottom: 15px; }}
        .receipt-row {{ display: flex; justify-content: space-between; margin-bottom: 10px; }}
        .receipt-total {{ font-weight: bold; border-top: 1px solid #ddd; padding-top: 10px; margin-top: 10px; }}
        .button {{ display: inline-block; padding: 10px 20px; background-color: #3498db; color: white; text-decoration: none; border-radius: 4px; font-weight: bold; margin: 20px 0; }}
        .footer {{ text-align: center; padding: 20px; font-size: 12px; color: #777; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>Online Assessment</h1>
        </div>
        <div class='content'>
            <p>Hello {name},</p>
            <p>Thank you for your payment. Your test booking has been confirmed!</p>

            <div class='receipt'>
                <div class='receipt-header'>
                    <h2>Payment Receipt</h2>
                    <p>Transaction ID: {transactionId}</p>
                    <p>Date: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}</p>
                </div>

                <div class='receipt-row'>
                    <span>Test:</span>
                    <span>{testName}</span>
                </div>
                <div class='receipt-row'>
                    <span>Booking Date:</span>
                    <span>{bookingDate}</span>
                </div>
                <div class='receipt-row'>
                    <span>Time Slot:</span>
                    <span>{startTime} - {endTime}</span>
                </div>
                <div class='receipt-total'>
                    <div class='receipt-row'>
                        <span>Total Amount:</span>
                        <span>₹{amount.ToString("0.00")}</span>
                    </div>
                </div>
            </div>

            <p>You can access your test during the scheduled time slot. Please log in to your account to view your scheduled test.</p>
            <p style='text-align: center;'>
                <a href='{_configuration["ApplicationUrl"] ?? "https://localhost:7240"}/Test/ScheduledTest' class='button'>View Scheduled Test</a>
            </p>
        </div>
        <div class='footer'>
            <p>This is an automated message, please do not reply to this email.</p>
            <p>&copy; {DateTime.Now.Year} Online Assessment. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";

                    mailMessage.Body = htmlBody;
                    mailMessage.To.Add(email);
                    await client.SendMailAsync(mailMessage);

                    _logger.LogInformation($"Payment receipt email sent successfully to {email}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send payment receipt email: {ex.Message}");
                _logger.LogError($"Exception details: {ex.ToString()}");

                // Log SMTP settings (without password) for debugging
                var smtpSettings = _configuration.GetSection("SmtpSettings");
                _logger.LogError($"SMTP Server: {smtpSettings["Server"]}");
                _logger.LogError($"SMTP Port: {smtpSettings["Port"]}");
                _logger.LogError($"SMTP Username: {smtpSettings["Username"]}");
                _logger.LogError($"SMTP SenderEmail: {smtpSettings["SenderEmail"]}");

                // Don't throw the exception - we don't want to fail the payment process if email sending fails
                // Just log the error and continue
            }
        }
    }
}
