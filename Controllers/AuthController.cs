using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using FinGuard.Data;
using FinGuard.Services;
using FinGuard.Models;
using Microsoft.EntityFrameworkCore;

namespace FinGuard.Controllers
{
    public class AuthController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly EmailService _emailService;

        public AuthController(ApplicationDbContext context, EmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity!.IsAuthenticated) return RedirectToAction("Index", "Home");

            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password, string? returnUrl = null)
        {
            var user = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email == email);

            if (user == null)
            {
                ViewBag.Error = "Security Alert: Unregistered Email Address.";
                return View();
            }

            if (user.Password == null)
            {
                ViewBag.Error = "First time user? Please click on 'Setup Account' below to verify OTP.";
                return View();
            }

            if (!BCrypt.Net.BCrypt.Verify(password, user.Password))
            {
                ViewBag.Error = "Invalid Password.";
                return View();
            }

            await SignInUser(user.Username, user.Email);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(string FullName, string Email, string Password)
        {
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
            {
                ViewBag.Error = "Email and Password are required.";
                return View();
            }

            var existingUser = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email == Email);

            if (existingUser != null)
            {
                if (!string.IsNullOrEmpty(existingUser.Password))
                {
                    ViewBag.Error = "Email already registered.";
                    return View();
                }

                existingUser.FullName = FullName;
                existingUser.Password = BCrypt.Net.BCrypt.HashPassword(Password);
                existingUser.IsVerified = true;
            }
            else
            {
                var newUser = new AdminUser
                {
                    FullName = FullName,
                    Username = Email, // Fallback since Username is required
                    Email = Email,
                    Password = BCrypt.Net.BCrypt.HashPassword(Password),
                    Role = "Admin",
                    IsVerified = true
                };

                _context.AdminUsers.Add(newUser);
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = "Admin account successfully created! You can now log in.";
            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult Setup()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendOtp(string email)
        {
            var admin = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email == email);
            if (admin == null)
            {
                ViewBag.Error = "Access Denied: Email not found in our secure database.";
                return View("Setup");
            }

            string otp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();

            admin.OtpCode = otp;
            admin.OtpExpiry = DateTime.UtcNow.AddMinutes(10);
            await _context.SaveChangesAsync();

            await _emailService.SendOtpEmailAsync(admin.Email, otp);

            ViewBag.Email = email;
            ViewBag.Success = "Secure OTP has been sent to your email!";
            return View("VerifyOtp");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]

        public async Task<IActionResult> SetNewPassword(string email, string otp, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            {
                ViewBag.Error = "Security Alert: Password must be at least 8 characters long.";
                ViewBag.Email = email;
                return View("VerifyOtp");
            }

            var admin = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email == email);

            if (admin != null && admin.OtpCode == otp && admin.OtpExpiry > DateTime.UtcNow)
            {
                admin.Password = BCrypt.Net.BCrypt.HashPassword(newPassword);
                admin.IsVerified = true;
                admin.OtpCode = null;
                await _context.SaveChangesAsync();

                await SignInUser(admin.Username, admin.Email);
                return RedirectToAction("Index", "Home");
            }

            ViewBag.Error = "Invalid or Expired OTP. Please try again.";
            ViewBag.Email = email;
            return View("VerifyOtp");
        }

        private async Task SignInUser(string username, string email)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Email, email)
            };
            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));
        }

        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            var admin = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email == email);
            if (admin == null)
            {
                ViewBag.Success = "If your email exists in our system, you will receive a password reset link shortly.";
                return View();
            }

            string resetToken = Guid.NewGuid().ToString();
            admin.OtpCode = resetToken;
            admin.OtpExpiry = DateTime.UtcNow.AddMinutes(15);
            await _context.SaveChangesAsync();

            var resetLink = Url.Action("ResetPassword", "Auth", new { token = resetToken, email = email }, Request.Scheme);
            await _emailService.SendPasswordResetEmailAsync(email, resetLink!);

            ViewBag.Success = "If your email exists in our system, you will receive a password reset link shortly.";
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> ResetPassword(string token, string email)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(email))
            {
                ViewBag.Error = "Invalid password reset link.";
                return View("Login");
            }
            
            var admin = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email == email && u.OtpCode == token);
            if (admin == null || admin.OtpExpiry <= DateTime.UtcNow)
            {
                ViewBag.Error = "Your password reset link is invalid or has expired.";
                return View("Login");
            }

            ViewData["Token"] = token;
            ViewData["Email"] = email;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(string email, string token, string newPassword, string confirmPassword)
        {
            ViewData["Token"] = token;
            ViewData["Email"] = email;

            if (newPassword != confirmPassword)
            {
                ViewBag.Error = "Passwords do not match.";
                return View();
            }

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            {
                ViewBag.Error = "Password must be at least 8 characters long.";
                return View();
            }

            var admin = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email == email && u.OtpCode == token);
            if (admin == null || admin.OtpExpiry <= DateTime.UtcNow)
            {
                ViewBag.Error = "Your password reset link is invalid or has expired.";
                return View();
            }

            admin.Password = BCrypt.Net.BCrypt.HashPassword(newPassword);
            admin.OtpCode = null;
            await _context.SaveChangesAsync();

            ViewBag.Success = "Your password has been successfully reset. You can now securely log in.";
            return View("Login");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }
    }
}