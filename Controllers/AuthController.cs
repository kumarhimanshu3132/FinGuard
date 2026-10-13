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
            var user = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

            if (user == null)
            {
                var custCheck = await _context.Customers.FirstOrDefaultAsync(c => c.Email.ToLower() == normalizedEmail);
                if (custCheck != null)
                {
                    ViewBag.Error = "Your borrower profile exists, but you have not configured an online password yet. Please create an account or verify your credentials.";
                    return View();
                }

                ViewBag.Error = "No registered account found with this email. Please check your credentials or register a borrower account.";
                return View();
            }

            if (string.IsNullOrEmpty(user.Password))
            {
                if (user.Role == "Admin" || user.Role == "SuperAdmin")
                {
                    ViewBag.Error = "First-time administrator setup required. Please click 'Verify OTP & Setup' below to create your password.";
                    return View();
                }
                else
                {
                    ViewBag.Error = "First time borrower setup required. Please register your account to activate your portal credentials.";
                    return View();
                }
            }

            bool passwordValid = false;
            try
            {
                passwordValid = BCrypt.Net.BCrypt.Verify(password, user.Password);
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

            // Smart dynamic routing based on verified role
            if (user.Role == "Admin" || user.Role == "SuperAdmin")
            {
                // Clear any borrower session and sign into Admin scheme
                await HttpContext.SignOutAsync(BorrowerAuthController.BorrowerAuthScheme);
                await SignInUser(user.Username, user.Email, user.Role, rememberMe);

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) && !returnUrl.StartsWith("/Portal", StringComparison.OrdinalIgnoreCase))
                {
                    return Redirect(returnUrl);
                }

                return RedirectToAction("Index", "Home");
            }
            else
            {
                // Borrower account
                var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Email.ToLower() == normalizedEmail);
                if (customer == null)
                {
                    int nextId = (await _context.Customers.MaxAsync(c => (int?)c.CustomerId) ?? 100) + 1;
                    customer = new Customer
                    {
                        CustomerId = nextId,
                        Name = user.FullName ?? user.Username,
                        Email = user.Email,
                        CountryCode = "+91",
                        Phone = "9999999999",
                        CreditStatus = "Pending",
                        KycStatus = "Pending",
                        MonthlyIncome = 25000,
                        Address = "Field Center Onboarding",
                        PinCode = "110001"
                    };
                    _context.Customers.Add(customer);
                    await _context.SaveChangesAsync();
                }

                // Clear any admin session and sign into Borrower scheme
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                await SignInBorrowerAsync(customer, rememberMe);

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) && !returnUrl.StartsWith("/Home", StringComparison.OrdinalIgnoreCase))
                {
                    return Redirect(returnUrl);
                }

                return RedirectToAction("Dashboard", "Portal");
            }
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