using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinGuard.Data;
using FinGuard.Models;
using FinGuard.Services;

namespace FinGuard.Controllers
{
    public class BorrowerAuthController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly EmailService _emailService;

        public const string BorrowerAuthScheme = "BorrowerAuth";

        public BorrowerAuthController(ApplicationDbContext context, EmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;
            string initialEmail = "";
            if (TempData["TemporaryPassword"] != null)
            {
                TempData.Keep("TemporaryPassword");
                if (TempData["TempEmail"] != null)
                {
                    TempData.Keep("TempEmail");
                    initialEmail = TempData["TempEmail"]?.ToString() ?? "";
                }
            }
            return View(new BorrowerLoginViewModel { ReturnUrl = returnUrl, Email = initialEmail });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(BorrowerLoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            string normalizedEmail = model.Email.Trim().ToLowerInvariant();

            // 1. Check if user is an Administrator in AdminUsers table
            var adminUser = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail && (u.Role == "Admin" || u.Role == "SuperAdmin"));
            if (adminUser != null)
            {
                if (string.IsNullOrEmpty(adminUser.Password))
                {
                    ViewBag.Error = "First-time administrator setup required. Please verify OTP to set up your password.";
                    return View(model);
                }

                bool adminPassValid = false;
                try
                {
                    adminPassValid = BCrypt.Net.BCrypt.Verify(model.Password, adminUser.Password);
                }
                catch
                {
                    adminPassValid = false;
                }

                if (!adminPassValid)
                {
                    ViewBag.Error = "Invalid credentials. Please verify your password and try again.";
                    return View(model);
                }

                bool forceAdminReset = adminUser.OtpCode == "FORCE_PASSWORD_RESET";
                await HttpContext.SignOutAsync(BorrowerAuthScheme);
                await SignInAdminAsync(adminUser.Username, adminUser.Email, adminUser.Role, model.RememberMe);

                if (forceAdminReset)
                {
                    TempData.Remove("TemporaryPassword");
                    TempData.Remove("TempEmail");
                    TempData["ResetEmail"] = adminUser.Email;
                    TempData["Info"] = "Please create a new permanent password to secure your administrator account.";
                    return RedirectToAction("SetNewPassword");
                }

                if (!string.IsNullOrEmpty(model.ReturnUrl) 
                    && Url.IsLocalUrl(model.ReturnUrl) 
                    && !model.ReturnUrl.StartsWith("/Portal", StringComparison.OrdinalIgnoreCase)
                    && !model.ReturnUrl.StartsWith("/BorrowerAuth", StringComparison.OrdinalIgnoreCase)
                    && !model.ReturnUrl.StartsWith("/Auth", StringComparison.OrdinalIgnoreCase)
                    && !model.ReturnUrl.Contains("login", StringComparison.OrdinalIgnoreCase))
                {
                    return Redirect(model.ReturnUrl);
                }

                return RedirectToAction("Index", "Home");
            }

            // 2. Otherwise, check Customers table for Borrower account
            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Email.ToLower() == normalizedEmail);
            if (customer == null)
            {
                ViewBag.Error = "No registered borrower account found with this email. Please check your credentials or register a borrower account.";
                return View(model);
            }

            if (string.IsNullOrEmpty(customer.Password))
            {
                ViewBag.Error = "Your borrower profile exists, but you haven't set an online password yet. Please register or reset password to activate your credentials.";
                return View(model);
            }

            bool passwordValid = false;
            try
            {
                passwordValid = BCrypt.Net.BCrypt.Verify(model.Password, customer.Password);
            }
            catch
            {
                passwordValid = false;
            }

            if (!passwordValid)
            {
                ViewBag.Error = "Invalid credentials. Please verify your password and try again.";
                return View(model);
            }

            if (!customer.IsVerified)
            {
                string otp = customer.OtpCode ?? System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
                customer.OtpCode = otp;
                customer.OtpExpiry = DateTime.UtcNow.AddMinutes(5);
                await _context.SaveChangesAsync();

                bool sent = await _emailService.SendOtpEmailAsync(customer.Email, otp, customer.Name);
                if (!sent)
                {
                    TempData["DevOtp"] = otp;
                }

                TempData["Error"] = "Your email address is not verified yet. A 6-digit verification code has been dispatched to your email. Please verify to access your portal.";
                return RedirectToAction("VerifyOtp", new { email = customer.Email });
            }

            bool forcePasswordReset = customer.OtpCode == "FORCE_PASSWORD_RESET";

            // Clear any admin session and sign into Borrower scheme
            await HttpContext.SignOutAsync(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
            await SignInBorrowerAsync(customer, model.RememberMe);

            if (forcePasswordReset)
            {
                TempData.Remove("TemporaryPassword");
                TempData.Remove("TempEmail");
                TempData["ResetEmail"] = customer.Email;
                TempData["Info"] = "Please create a new permanent password to secure your borrower account.";
                return RedirectToAction("SetNewPassword");
            }

            if (!string.IsNullOrEmpty(model.ReturnUrl) 
                && Url.IsLocalUrl(model.ReturnUrl) 
                && !model.ReturnUrl.StartsWith("/Home", StringComparison.OrdinalIgnoreCase)
                && !model.ReturnUrl.StartsWith("/BorrowerAuth", StringComparison.OrdinalIgnoreCase)
                && !model.ReturnUrl.StartsWith("/Auth", StringComparison.OrdinalIgnoreCase)
                && !model.ReturnUrl.Contains("login", StringComparison.OrdinalIgnoreCase))
            {
                return Redirect(model.ReturnUrl);
            }

            return RedirectToAction("Dashboard", "Portal");
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View(new BorrowerRegisterViewModel
            {
                DateOfBirth = DateTime.Today.AddYears(-25)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(BorrowerRegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Prevent age < 18
            if (model.DateOfBirth.AddYears(18) > DateTime.Today)
            {
                ModelState.AddModelError("DateOfBirth", "Borrowers must be at least 18 years of age to apply for microfinance lending.");
                return View(model);
            }

            string normalizedEmail = model.Email.Trim().ToLowerInvariant();
            string cleanPhone = model.Phone.Trim();

            // Guard against admin email overlap
            var existingAdmin = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail && (u.Role == "Admin" || u.Role == "SuperAdmin"));
            if (existingAdmin != null)
            {
                ViewBag.Error = "This email is registered for system administration. Please use the administrative portal.";
                return View(model);
            }

            var existingPhoneCust = await _context.Customers.FirstOrDefaultAsync(c => c.Phone == cleanPhone && c.Email.ToLower() != normalizedEmail);
            if (existingPhoneCust != null)
            {
                ModelState.AddModelError("Phone", "This mobile number is already associated with another borrower account.");
                return View(model);
            }

            var existingCustomer = await _context.Customers.FirstOrDefaultAsync(c => c.Email.ToLower() == normalizedEmail);
            if (existingCustomer != null && existingCustomer.IsVerified && !string.IsNullOrEmpty(existingCustomer.Password))
            {
                ViewBag.Error = "An account with this email is already registered and verified. Please sign in with your password.";
                return View(model);
            }

            Customer customer;
            DateTime dobUtc = DateTime.SpecifyKind(model.DateOfBirth, DateTimeKind.Utc);
            string otp = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(model.Password);

            if (existingCustomer != null)
            {
                // Profile exists, update details & credentials
                customer = existingCustomer;
                customer.Name = model.Name.Trim();
                customer.CountryCode = "+91";
                customer.Phone = cleanPhone;
                customer.DateOfBirth = dobUtc;
                customer.MonthlyIncome = model.MonthlyIncome ?? 0;
                customer.Address = model.Address.Trim();
                customer.PinCode = model.PinCode.Trim();
                customer.Password = hashedPassword;
                customer.IsVerified = false;
                customer.OtpCode = otp;
                customer.OtpExpiry = DateTime.UtcNow.AddMinutes(5);
            }
            else
            {
                int nextId = (await _context.Customers.MaxAsync(c => (int?)c.CustomerId) ?? 100) + 1;

                customer = new Customer
                {
                    CustomerId = nextId,
                    Name = model.Name.Trim(),
                    Email = normalizedEmail,
                    CountryCode = "+91",
                    Phone = cleanPhone,
                    DateOfBirth = dobUtc,
                    MonthlyIncome = model.MonthlyIncome ?? 0,
                    Address = model.Address.Trim(),
                    PinCode = model.PinCode.Trim(),
                    CreditStatus = "Pending",
                    KycStatus = "Pending",
                    Password = hashedPassword,
                    IsVerified = false,
                    OtpCode = otp,
                    OtpExpiry = DateTime.UtcNow.AddMinutes(5)
                };

                _context.Customers.Add(customer);
            }

            await _context.SaveChangesAsync();

            // Send 6-digit OTP email
            bool emailSent = await _emailService.SendOtpEmailAsync(normalizedEmail, otp, model.Name.Trim());
            if (!emailSent)
            {
                TempData["DevOtp"] = otp;
            }

            TempData["Success"] = "A 6-digit verification code has been dispatched to your email address.";
            return RedirectToAction("VerifyOtp", new { email = normalizedEmail });
        }

        [HttpGet]
        public async Task<IActionResult> VerifyOtp(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction("Register");
            }

            string normalizedEmail = email.Trim().ToLowerInvariant();
            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Email.ToLower() == normalizedEmail);
            var adminUser = customer == null ? await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail) : null;

            if (customer == null && adminUser == null)
            {
                return RedirectToAction("Register");
            }

            bool isVerified = customer != null ? customer.IsVerified : (adminUser?.IsVerified ?? false);
            string? activeOtp = customer != null ? customer.OtpCode : adminUser?.OtpCode;

            if (isVerified)
            {
                TempData["Success"] = "Your email is already verified. Please sign in.";
                return RedirectToAction("Login");
            }

            var model = new BorrowerVerifyOtpViewModel
            {
                Email = normalizedEmail
            };

            ViewBag.Email = normalizedEmail;
            if (TempData["DevOtp"] == null && !string.IsNullOrEmpty(activeOtp))
            {
                ViewBag.DevOtp = activeOtp;
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyOtp(BorrowerVerifyOtpViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Email = model.Email;
                return View(model);
            }

            string normalizedEmail = model.Email.Trim().ToLowerInvariant();
            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Email.ToLower() == normalizedEmail);
            var adminUser = customer == null ? await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail) : null;

            if (customer == null && adminUser == null)
            {
                ViewBag.Error = "Account record not found for this email address. Please register again.";
                return View(model);
            }

            bool isVerified = customer != null ? customer.IsVerified : (adminUser?.IsVerified ?? false);
            if (isVerified)
            {
                TempData["Success"] = "Your email is already verified. Please sign in.";
                return RedirectToAction("Login");
            }

            string? activeOtp = customer != null ? customer.OtpCode : adminUser?.OtpCode;
            DateTime? otpExpiry = customer != null ? customer.OtpExpiry : adminUser?.OtpExpiry;

            int attempts = TempData["OtpAttempts"] != null ? Convert.ToInt32(TempData["OtpAttempts"]) : 0;
            if (attempts >= 5)
            {
                if (customer != null) { customer.OtpCode = null; customer.OtpExpiry = null; }
                if (adminUser != null) { adminUser.OtpCode = null; adminUser.OtpExpiry = null; }
                await _context.SaveChangesAsync();
                TempData.Remove("OtpAttempts");

                ViewBag.Email = normalizedEmail;
                ViewBag.Error = "Maximum verification attempts exceeded. For your security, this code has been invalidated. Please click 'Resend Code' to request a new code.";
                return View(model);
            }

            if (string.IsNullOrEmpty(activeOtp) || otpExpiry == null || otpExpiry < DateTime.UtcNow)
            {
                ViewBag.Email = normalizedEmail;
                ViewBag.Error = "The verification code has expired (5-minute limit). Please request a new code using the 'Resend Code' button below.";
                return View(model);
            }

            if (activeOtp.Trim() != model.Otp.Trim())
            {
                TempData["OtpAttempts"] = attempts + 1;
                int remaining = 5 - (attempts + 1);
                ViewBag.Email = normalizedEmail;
                ViewBag.Error = $"Invalid verification code. {remaining} attempt(s) remaining before code expires.";
                return View(model);
            }

            // Mark user verified and clear OTP
            if (customer != null)
            {
                customer.IsVerified = true;
                customer.OtpCode = null;
                customer.OtpExpiry = null;
            }
            if (adminUser != null)
            {
                adminUser.IsVerified = true;
                adminUser.OtpCode = null;
                adminUser.OtpExpiry = null;
            }
            await _context.SaveChangesAsync();
            TempData.Remove("OtpAttempts");

            if (adminUser != null)
            {
                await HttpContext.SignOutAsync(BorrowerAuthScheme);
                await SignInAdminAsync(adminUser.Username, adminUser.Email, adminUser.Role);
                TempData["Success"] = "Email verified successfully! Welcome to FinGuard Operations.";
                return RedirectToAction("Index", "Home");
            }
            else
            {
                // Authenticate borrower session
                await SignInBorrowerAsync(customer!);
                TempData["Success"] = "Email verified successfully! Welcome to FinGuard Microfinance Portal.";
                return RedirectToAction("Dashboard", "Portal");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendOtp(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction("Register");
            }

            string normalizedEmail = email.Trim().ToLowerInvariant();
            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Email.ToLower() == normalizedEmail);
            var adminUser = customer == null ? await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail) : null;

            if (customer == null && adminUser == null)
            {
                TempData["Error"] = "Account record not found. Please register first.";
                return RedirectToAction("Register");
            }

            bool isVerified = customer != null ? customer.IsVerified : (adminUser?.IsVerified ?? false);
            if (isVerified)
            {
                TempData["Success"] = "Your email is already verified. Please sign in.";
                return RedirectToAction("Login");
            }

            string newOtp = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            string recipientName = customer?.Name ?? adminUser?.FullName ?? "User";

            if (customer != null)
            {
                customer.OtpCode = newOtp;
                customer.OtpExpiry = DateTime.UtcNow.AddMinutes(5);
            }
            if (adminUser != null)
            {
                adminUser.OtpCode = newOtp;
                adminUser.OtpExpiry = DateTime.UtcNow.AddMinutes(5);
            }
            await _context.SaveChangesAsync();
            TempData["OtpAttempts"] = 0;

            bool sent = await _emailService.SendOtpEmailAsync(normalizedEmail, newOtp, recipientName);
            if (!sent)
            {
                TempData["DevOtp"] = newOtp;
            }

            TempData["Success"] = "A fresh 6-digit verification code has been dispatched to your email.";
            return RedirectToAction("VerifyOtp", new { email = normalizedEmail });
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
            if (string.IsNullOrWhiteSpace(email))
            {
                ViewBag.Error = "Please enter your registered email address.";
                return View();
            }

            string normalizedEmail = email.Trim().ToLowerInvariant();
            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Email.ToLower() == normalizedEmail);
            var adminUser = customer == null ? await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail) : null;

            if (customer == null && adminUser == null)
            {
                ViewBag.Error = "No account found registered with this email address. Please check your email or create a new account.";
                return View();
            }

            // Generate temporary password (e.g. FG#123456)
            string tempPassword = "FG#" + System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 999999).ToString();
            string hashedTempPassword = BCrypt.Net.BCrypt.HashPassword(tempPassword);
            string recipientName = customer?.Name ?? adminUser?.FullName ?? adminUser?.Username ?? "User";
            string recipientEmail = customer?.Email ?? adminUser!.Email;

            if (customer != null)
            {
                customer.Password = hashedTempPassword;
                customer.OtpCode = "FORCE_PASSWORD_RESET";
                customer.OtpExpiry = DateTime.UtcNow.AddHours(24);
            }
            if (adminUser != null)
            {
                adminUser.Password = hashedTempPassword;
                adminUser.OtpCode = "FORCE_PASSWORD_RESET";
                adminUser.OtpExpiry = DateTime.UtcNow.AddHours(24);
            }
            await _context.SaveChangesAsync();

            try
            {
                await _emailService.SendOtpEmailAsync(recipientEmail, tempPassword, recipientName);
            }
            catch
            {
                // Fallback to on-screen presentation
            }

            TempData["TemporaryPassword"] = tempPassword;
            TempData["TempEmail"] = recipientEmail;
            TempData["Success"] = "A temporary password has been generated! Use it to sign in below, and you will set your new permanent password immediately.";

            return RedirectToAction("Login");
        }

        [HttpGet]
        public async Task<IActionResult> SetNewPassword()
        {
            var email = User.FindFirst(ClaimTypes.Email)?.Value 
                        ?? (TempData["ResetEmail"] as string)
                        ?? (TempData["TempEmail"] as string);

            if (string.IsNullOrEmpty(email))
            {
                return RedirectToAction("Login");
            }

            TempData.Keep("ResetEmail");
            return View(new SetNewPasswordViewModel { Email = email });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetNewPassword(SetNewPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            string normalizedEmail = model.Email.Trim().ToLowerInvariant();
            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Email.ToLower() == normalizedEmail);
            var adminUser = customer == null ? await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail) : null;

            if (customer == null && adminUser == null)
            {
                ViewBag.Error = "Account record not found. Please sign in again.";
                return RedirectToAction("Login");
            }

            string newHashed = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
            if (customer != null)
            {
                customer.Password = newHashed;
                customer.OtpCode = null;
                customer.OtpExpiry = null;
            }
            if (adminUser != null)
            {
                adminUser.Password = newHashed;
                adminUser.OtpCode = null;
                adminUser.OtpExpiry = null;
            }
            await _context.SaveChangesAsync();

            TempData.Remove("TemporaryPassword");
            TempData.Remove("TempEmail");
            TempData.Remove("ResetEmail");
            TempData["Success"] = "Your permanent password has been updated successfully! Welcome to your dashboard.";

            if (adminUser != null)
            {
                return RedirectToAction("Index", "Home");
            }
            else
            {
                return RedirectToAction("Dashboard", "Portal");
            }
        }

        [HttpGet]
        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(BorrowerAuthScheme);
            await HttpContext.SignOutAsync(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        private async Task SignInAdminAsync(string username, string email, string role = "Admin", bool rememberMe = false)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Role, role)
            };
            var claimsIdentity = new ClaimsIdentity(claims, Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2)
            };
            await HttpContext.SignInAsync(
                Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme,
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

            var claimsIdentity = new ClaimsIdentity(claims, BorrowerAuthScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(4)
            };

            await HttpContext.SignInAsync(
                BorrowerAuthScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);
        }
    }
}
