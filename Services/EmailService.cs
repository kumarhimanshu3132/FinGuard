using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FinGuard.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration config, ILogger<EmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task<bool> SendOtpEmailAsync(string toEmail, string otpCode, string? recipientName = null)
        {
            try
            {
                var emailSettings = _config.GetSection("EmailSettings");
                var senderEmail = emailSettings["SenderEmail"];
                var appPassword = emailSettings["AppPassword"];
                var smtpServer = emailSettings["SmtpServer"] ?? "smtp.gmail.com";
                var portStr = emailSettings["SmtpPort"] ?? "587";
                var senderName = emailSettings["SenderName"] ?? "FinGuard Security";

                if (string.IsNullOrWhiteSpace(senderEmail) || string.IsNullOrWhiteSpace(appPassword))
                {
                    _logger.LogWarning("Email settings incomplete or AppPassword is missing. Verification OTP for {Email} is: {Otp}", toEmail, otpCode);
                    return false;
                }

                using var client = new SmtpClient(smtpServer, int.Parse(portStr))
                {
                    EnableSsl = true,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(senderEmail, appPassword),
                    Timeout = 10000
                };

                string greeting = string.IsNullOrWhiteSpace(recipientName) ? "Hello," : $"Hello <strong>{WebUtility.HtmlEncode(recipientName)}</strong>,";

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(senderEmail, senderName),
                    Subject = "FinGuard Verification - 6-Digit OTP Code",
                    Body = $@"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'/>
<style>
  body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f8fafc; margin: 0; padding: 24px; color: #1e293b; }}
  .container {{ max-width: 520px; margin: 0 auto; background: #ffffff; border: 1px solid #e2e8f0; border-radius: 16px; padding: 32px; box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.05); }}
  .header {{ text-align: center; border-bottom: 1px solid #f1f5f9; padding-bottom: 20px; }}
  .brand {{ font-size: 22px; font-weight: 700; color: #4338ca; margin: 0; letter-spacing: -0.5px; }}
  .tagline {{ font-size: 13px; color: #64748b; margin-top: 4px; }}
  .content {{ padding: 24px 0; text-align: center; }}
  .otp-box {{ display: inline-block; background: #eef2ff; border: 2px dashed #6366f1; border-radius: 12px; padding: 16px 36px; margin: 20px 0; }}
  .otp-code {{ font-size: 36px; font-weight: 800; letter-spacing: 10px; color: #3730a3; font-family: 'Courier New', Courier, monospace; }}
  .alert-box {{ background: #fffbeb; border: 1px solid #fef3c7; border-radius: 8px; padding: 12px; margin-top: 16px; font-size: 13px; color: #92400e; }}
  .footer {{ border-top: 1px solid #f1f5f9; padding-top: 20px; text-align: center; font-size: 12px; color: #94a3b8; }}
</style>
</head>
<body>
  <div class='container'>
    <div class='header'>
      <div class='brand'>FinGuard</div>
      <div class='tagline'>Digital Microfinance & Credit Services</div>
    </div>
    <div class='content'>
      <p style='font-size: 15px; margin-bottom: 16px; text-align: left;'>{greeting}</p>
      <p style='font-size: 14px; color: #475569; text-align: left;'>Please enter the following 6-digit verification code to verify your email address and continue with your borrower account setup:</p>
      <div class='otp-box'>
        <div class='otp-code'>{otpCode}</div>
      </div>
      <p style='font-size: 13px; color: #64748b; margin-top: 8px;'>This code is valid for <strong>10 minutes</strong>.</p>
      <div class='alert-box'>
        <strong>Security Notice:</strong> Never share this OTP with anyone, including FinGuard field officers or support representatives.
      </div>
    </div>
    <div class='footer'>
      <p style='margin: 0;'>If you did not request this verification code, you can safely ignore this email.</p>
      <p style='margin: 6px 0 0 0;'>&copy; 2026 FinGuard Institutional Microfinance. All rights reserved.</p>
    </div>
  </div>
</body>
</html>",
                    IsBodyHtml = true
                };

                mailMessage.To.Add(toEmail);
                await client.SendMailAsync(mailMessage);
                _logger.LogInformation("Successfully dispatched OTP email to {Email}", toEmail);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending OTP email to {Email}", toEmail);
                return false;
            }
        }

        public async Task<bool> SendPasswordResetEmailAsync(string toEmail, string resetLink)
        {
            try
            {
                var emailSettings = _config.GetSection("EmailSettings");
                var senderEmail = emailSettings["SenderEmail"];
                var appPassword = emailSettings["AppPassword"];
                var smtpServer = emailSettings["SmtpServer"] ?? "smtp.gmail.com";
                var portStr = emailSettings["SmtpPort"] ?? "587";
                var senderName = emailSettings["SenderName"] ?? "FinGuard Security";

                if (string.IsNullOrWhiteSpace(senderEmail) || string.IsNullOrWhiteSpace(appPassword))
                {
                    _logger.LogWarning("Email settings incomplete. Password reset link for {Email} is: {Link}", toEmail, resetLink);
                    return false;
                }

                using var client = new SmtpClient(smtpServer, int.Parse(portStr))
                {
                    EnableSsl = true,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(senderEmail, appPassword),
                    Timeout = 10000
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(senderEmail, senderName),
                    Subject = "FinGuard - Password Reset Request",
                    Body = $@"<h2>FinGuard Password Reset</h2><p>You requested a password reset. Click the link below to securely reset your password:</p><p><a href='{resetLink}'>Reset Password</a></p><p>This secure link is valid for 15 minutes. If you did not request this, please ignore this email.</p>",
                    IsBodyHtml = true
                };

                mailMessage.To.Add(toEmail);
                await client.SendMailAsync(mailMessage);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending password reset email to {Email}", toEmail);
                return false;
            }
        }
    }
}