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
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password, bool rememberMe = false, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "Please enter both your email address and password.";
                return View();
            }

            string normalizedEmail = email.Trim().ToLowerInvariant();

            // 1. Check if user is an Administrator in AdminUsers table
            var adminUser = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail && (u.Role == "Admin" || u.Role == "SuperAdmin"));

            if (adminUser != null)
            {
                if (string.IsNullOrEmpty(adminUser.Password))
                {
                    ViewBag.Error = "First-time administrator setup required. Please click 'Verify OTP & Setup' below to create your password.";
                    return View();
                }

                bool passwordValid = false;
                try
                {
                    passwordValid = BCrypt.Net.BCrypt.Verify(password, adminUser.Password);
                }
                catch
                {
                    passwordValid = false;
                }

                if (!passwordValid)
                {
                    ViewBag.Error = "Invalid credentials. Please verify your password and try again.";
                    return View();
                }

                bool forcePasswordReset = adminUser.OtpCode == "FORCE_PASSWORD_RESET";

                // Clear any borrower session and sign into Admin scheme
                await HttpContext.SignOutAsync(BorrowerAuthController.BorrowerAuthScheme);
                await SignInUser(adminUser.Username, adminUser.Email, adminUser.Role, rememberMe);

                if (forcePasswordReset)
                {
                    TempData.Remove("TemporaryPassword");
                    TempData.Remove("TempEmail");
                    TempData["ResetEmail"] = adminUser.Email;
                    TempData["Info"] = "Please create a new permanent password to secure your administrator account.";
                    return RedirectToAction("SetNewPassword", "BorrowerAuth");
                }

                if (!string.IsNullOrEmpty(returnUrl) 
                    && Url.IsLocalUrl(returnUrl) 
                    && !returnUrl.StartsWith("/Portal", StringComparison.OrdinalIgnoreCase)
                    && !returnUrl.StartsWith("/BorrowerAuth", StringComparison.OrdinalIgnoreCase)
                    && !returnUrl.StartsWith("/Auth", StringComparison.OrdinalIgnoreCase)
                    && !returnUrl.Contains("login", StringComparison.OrdinalIgnoreCase))
                {
                    return Redirect(returnUrl);
                }

                return RedirectToAction("Index", "Home");
            }

            // 2. Otherwise, check Customers table for Borrower account
            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Email.ToLower() == normalizedEmail);
            if (customer == null)
            {
                ViewBag.Error = "No registered account found with this email. Please check your credentials or register a borrower account.";
                return View();
            }

            if (string.IsNullOrEmpty(customer.Password))
            {
                ViewBag.Error = "Your borrower profile exists, but you have not configured an online password yet. Please create an account or verify your credentials.";
                return View();
            }

            bool custPasswordValid = false;
            try
            {
                custPasswordValid = BCrypt.Net.BCrypt.Verify(password, customer.Password);
            }
            catch
            {
                custPasswordValid = false;
            }

            if (!custPasswordValid)
            {
                ViewBag.Error = "Invalid credentials. Please verify your password and try again.";
                return View();
            }

            if (!customer.IsVerified)
            {
                TempData["TempEmail"] = customer.Email;
                TempData["Info"] = "Please verify your email address to activate your borrower portal account.";
                return RedirectToAction("VerifyOtp", "BorrowerAuth");
            }

            bool custForcePasswordReset = customer.OtpCode == "FORCE_PASSWORD_RESET";

            // Clear any admin session and sign into Borrower scheme
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await SignInBorrowerAsync(customer, rememberMe);

            if (custForcePasswordReset)
            {
                TempData.Remove("TemporaryPassword");
                TempData.Remove("TempEmail");
                TempData["ResetEmail"] = customer.Email;
                TempData["Info"] = "Please create a new permanent password to secure your borrower account.";
                return RedirectToAction("SetNewPassword", "BorrowerAuth");
            }

            if (!string.IsNullOrEmpty(returnUrl) 
                && Url.IsLocalUrl(returnUrl) 
                && !returnUrl.StartsWith("/Home", StringComparison.OrdinalIgnoreCase)
                && !returnUrl.StartsWith("/BorrowerAuth", StringComparison.OrdinalIgnoreCase)
                && !returnUrl.StartsWith("/Auth", StringComparison.OrdinalIgnoreCase)
                && !returnUrl.Contains("login", StringComparison.OrdinalIgnoreCase))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Dashboard", "Portal");
        }

        [HttpGet]
        public IActionResult Register()
        {
            return RedirectToAction("Register", "BorrowerAuth");
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

            string normalizedEmail = Email.Trim().ToLowerInvariant();
            var adminUser = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail && (u.Role == "Admin" || u.Role == "SuperAdmin"));

            if (adminUser != null)
            {
                if (!string.IsNullOrEmpty(adminUser.Password))
                {
                    ViewBag.Error = "Administrator account already registered. Please sign in.";
                    return View();
                }

                adminUser.FullName = FullName;
                adminUser.Password = BCrypt.Net.BCrypt.HashPassword(Password);
                adminUser.IsVerified = true;
                await _context.SaveChangesAsync();

                TempData["Success"] = "Administrator account successfully configured! You can now log in.";
                return RedirectToAction("Login");
            }

            // Borrower accounts are registered in Customers table via BorrowerAuth
            TempData["Info"] = "Borrower registration is handled via our Borrower Portal. Please complete your registration here.";
            return RedirectToAction("Register", "BorrowerAuth", new { email = normalizedEmail });
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
            string normalized = (email ?? "").Trim().ToLowerInvariant();
            var admin = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == normalized && (u.Role == "Admin" || u.Role == "SuperAdmin"));
            if (admin == null)
            {
                ViewBag.Error = "Access Denied: Email not found in our secure administrator database.";
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

            string normalized = (email ?? "").Trim().ToLowerInvariant();
            var admin = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == normalized && (u.Role == "Admin" || u.Role == "SuperAdmin"));

            if (admin != null && admin.OtpCode == otp && admin.OtpExpiry > DateTime.UtcNow)
            {
                admin.Password = BCrypt.Net.BCrypt.HashPassword(newPassword);
                admin.IsVerified = true;
                admin.OtpCode = null;
                await _context.SaveChangesAsync();

                await SignInUser(admin.Username, admin.Email, admin.Role);
                return RedirectToAction("Index", "Home");
            }

            ViewBag.Error = "Invalid or Expired OTP. Please try again.";
            ViewBag.Email = email;
            return View("VerifyOtp");
        }

        private async Task SignInUser(string username, string email, string role = "Admin", bool rememberMe = false)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Role, role)
            };
            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2)
            };
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);
        }

        private async Task SignInBorrowerAsync(Customer customer, bool rememberMe = false)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, customer.Name),
                new Claim(ClaimTypes.Email, customer.Email),
                new Claim(ClaimTypes.NameIdentifier, customer.CustomerId.ToString()),
                new Claim("CustomerId", customer.CustomerId.ToString()),
                new Claim(ClaimTypes.Role, "Borrower")
            };

            var claimsIdentity = new ClaimsIdentity(claims, BorrowerAuthController.BorrowerAuthScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(4)
            };

            await HttpContext.SignInAsync(
                BorrowerAuthController.BorrowerAuthScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);
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
            string normalized = (email ?? "").Trim().ToLowerInvariant();
            var admin = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == normalized && (u.Role == "Admin" || u.Role == "SuperAdmin"));
            if (admin != null)
            {
                string resetToken = Guid.NewGuid().ToString();
                admin.OtpCode = resetToken;
                admin.OtpExpiry = DateTime.UtcNow.AddMinutes(15);
                await _context.SaveChangesAsync();

                var resetLink = Url.Action("ResetPassword", "Auth", new { token = resetToken, email = admin.Email }, Request.Scheme);
                await _emailService.SendPasswordResetEmailAsync(admin.Email, resetLink ?? string.Empty);

                ViewBag.Success = "If your email exists in our system, you will receive a password reset link shortly.";
                return View();
            }

            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Email.ToLower() == normalized);
            if (customer != null && !string.IsNullOrEmpty(customer.Email))
            {
                string resetToken = Guid.NewGuid().ToString();
                customer.OtpCode = resetToken;
                customer.OtpExpiry = DateTime.UtcNow.AddMinutes(15);
                await _context.SaveChangesAsync();

                var resetLink = Url.Action("ResetPassword", "BorrowerAuth", new { token = resetToken, email = customer.Email }, Request.Scheme);
                await _emailService.SendPasswordResetEmailAsync(customer.Email, resetLink ?? string.Empty);

                ViewBag.Success = "If your email exists in our system, you will receive a password reset link shortly.";
                return View();
            }

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
            
            string normalized = email.Trim().ToLowerInvariant();
            var admin = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == normalized && u.OtpCode == token && (u.Role == "Admin" || u.Role == "SuperAdmin"));
            if (admin == null || admin.OtpExpiry <= DateTime.UtcNow)
            {
                var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Email.ToLower() == normalized && c.OtpCode == token);
                if (customer == null || customer.OtpExpiry <= DateTime.UtcNow)
                {
                    ViewBag.Error = "Your password reset link is invalid or has expired.";
                    return View("Login");
                }
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

            string normalized = (email ?? "").Trim().ToLowerInvariant();
            var admin = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == normalized && u.OtpCode == token && (u.Role == "Admin" || u.Role == "SuperAdmin"));
            if (admin != null && admin.OtpExpiry > DateTime.UtcNow)
            {
                admin.Password = BCrypt.Net.BCrypt.HashPassword(newPassword);
                admin.OtpCode = null;
                await _context.SaveChangesAsync();

                ViewBag.Success = "Your password has been successfully reset. You can now securely log in.";
                return View("Login");
            }

            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Email.ToLower() == normalized && c.OtpCode == token);
            if (customer != null && customer.OtpExpiry > DateTime.UtcNow)
            {
                customer.Password = BCrypt.Net.BCrypt.HashPassword(newPassword);
                customer.OtpCode = null;
                await _context.SaveChangesAsync();

                ViewBag.Success = "Your password has been successfully reset. You can now securely log in.";
                return View("Login");
            }

            ViewBag.Error = "Your password reset link is invalid or has expired.";
            return View("Login");
        }

        [HttpGet]
        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignOutAsync(BorrowerAuthController.BorrowerAuthScheme);
            return RedirectToAction("Login");
        }
    }
}