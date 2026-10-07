using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;

namespace FinGuard.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        public async Task SendOtpEmailAsync(string toEmail, string otpCode)
        {
            var emailSettings = _config.GetSection("EmailSettings");
            var senderEmail = emailSettings["SenderEmail"];
            var appPassword = emailSettings["AppPassword"];

            var client = new SmtpClient(emailSettings["SmtpServer"], int.Parse(emailSettings["SmtpPort"]!))
            {
                EnableSsl = true,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(senderEmail, appPassword)
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(senderEmail!, emailSettings["SenderName"]),
                Subject = "FinGuard Verification - OTP",
                Body = $"<h2>FinGuard Secure Login</h2><p>Your Verification OTP is: <strong>{otpCode}</strong></p><p>This OTP is valid for 10 minutes. Do not share it with anyone.</p>",
                IsBodyHtml = true
            };

            mailMessage.To.Add(toEmail);
            await client.SendMailAsync(mailMessage);
        }

        public async Task SendPasswordResetEmailAsync(string toEmail, string resetLink)
        {
            var emailSettings = _config.GetSection("EmailSettings");
            var senderEmail = emailSettings["SenderEmail"];
            var appPassword = emailSettings["AppPassword"];

            var client = new SmtpClient(emailSettings["SmtpServer"], int.Parse(emailSettings["SmtpPort"]!))
            {
                EnableSsl = true,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(senderEmail, appPassword)
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(senderEmail!, emailSettings["SenderName"]),
                Subject = "FinGuard - Password Reset Request",
                Body = $"<h2>FinGuard Password Reset</h2><p>You requested a password reset. Click the link below to securely reset your password:</p><p><a href='{resetLink}'>Reset Password</a></p><p>This secure link is valid for 15 minutes. If you did not request this, please ignore this email.</p>",
                IsBodyHtml = true
            };

            mailMessage.To.Add(toEmail);
            await client.SendMailAsync(mailMessage);
        }
    }
}