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
            return View(new BorrowerLoginViewModel { ReturnUrl = returnUrl });
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
            var authUser = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

            if (authUser == null)
            {
                var custCheck = await _context.Customers.FirstOrDefaultAsync(c => c.Email.ToLower() == normalizedEmail);
                if (custCheck != null)
                {
                    ViewBag.Error = "Your borrower profile exists, but you have not configured an online password yet. Please create an account or verify your credentials.";
                    return View(model);
                }

                ViewBag.Error = "No registered account found with this email. Please check your credentials or register a borrower account.";
                return View(model);
            }

            if (string.IsNullOrEmpty(authUser.Password))
            {
                if (authUser.Role == "Admin" || authUser.Role == "SuperAdmin")
                {
                    ViewBag.Error = "First-time administrator setup required. Please verify OTP to set up your password.";
                    return View(model);
                }
                else
                {
                    ViewBag.Error = "Your borrower profile exists in the system, but you haven't set a portal password yet. Please register to activate your credentials.";
                    return View(model);
                }
            }

            bool passwordValid = false;
            try
            {
                passwordValid = BCrypt.Net.BCrypt.Verify(model.Password, authUser.Password);
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

            // Smart dynamic routing based on verified role
            if (authUser.Role == "Admin" || authUser.Role == "SuperAdmin")
            {
                // Clear any borrower session and sign into Admin scheme
                await HttpContext.SignOutAsync(BorrowerAuthScheme);
                await SignInAdminAsync(authUser.Username, authUser.Email, authUser.Role, model.RememberMe);

                if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl) && !model.ReturnUrl.StartsWith("/Portal", StringComparison.OrdinalIgnoreCase))
                {
                    return Redirect(model.ReturnUrl);
                }

                return RedirectToAction("Index", "Home");
            }
            else
            {
                // Borrower account
                if (!authUser.IsVerified)
                {
                    string otp = authUser.OtpCode ?? System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
                    authUser.OtpCode = otp;
                    authUser.OtpExpiry = DateTime.UtcNow.AddMinutes(5);
                    await _context.SaveChangesAsync();

                    bool sent = await _emailService.SendOtpEmailAsync(authUser.Email, otp, authUser.FullName);
                    if (!sent)
                    {
                        TempData["DevOtp"] = otp;
                    }

                    TempData["Error"] = "Your email address is not verified yet. A 6-digit verification code has been dispatched to your email. Please verify to access your portal.";
                    return RedirectToAction("VerifyOtp", new { email = authUser.Email });
                }

                var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Email.ToLower() == normalizedEmail);
                if (customer == null)
                {
                    int nextId = (await _context.Customers.MaxAsync(c => (int?)c.CustomerId) ?? 100) + 1;
                    customer = new Customer
                    {
                        CustomerId = nextId,
                        Name = authUser.FullName ?? authUser.Username,
                        Email = authUser.Email,
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
                await HttpContext.SignOutAsync(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
                await SignInBorrowerAsync(customer, model.RememberMe);

                if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl) && !model.ReturnUrl.StartsWith("/Home", StringComparison.OrdinalIgnoreCase))
                {
                    return Redirect(model.ReturnUrl);
                }

                return RedirectToAction("Dashboard", "Portal");
            }
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

            var existingAuth = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);
            if (existingAuth != null && existingAuth.IsVerified && !string.IsNullOrEmpty(existingAuth.Password))
            {
                ViewBag.Error = "An account with this email is already registered and verified. Please sign in with your password.";
                return View(model);
            }

            var existingPhoneCust = await _context.Customers.FirstOrDefaultAsync(c => c.Phone == cleanPhone && c.Email.ToLower() != normalizedEmail);
            if (existingPhoneCust != null)
            {
                ModelState.AddModelError("Phone", "This mobile number is already associated with another borrower account.");
                return View(model);
            }

            var existingCustomer = await _context.Customers.FirstOrDefaultAsync(c => c.Email.ToLower() == normalizedEmail);
            Customer customer;
            DateTime dobUtc = DateTime.SpecifyKind(model.DateOfBirth, DateTimeKind.Utc);

            if (existingCustomer != null)
            {
                // Profile exists, update details
                customer = existingCustomer;
                customer.Name = model.Name.Trim();
                customer.CountryCode = "+91";
                customer.Phone = cleanPhone;
                customer.DateOfBirth = dobUtc;
                customer.MonthlyIncome = model.MonthlyIncome;
                customer.Address = model.Address.Trim();
                customer.PinCode = model.PinCode.Trim();
            }
            else
            {
                // Generate next integer CustomerId
                int nextId = (await _context.Customers.MaxAsync(c => (int?)c.CustomerId) ?? 100) + 1;

                customer = new Customer
                {
                    CustomerId = nextId,
                    Name = model.Name.Trim(),
                    Email = normalizedEmail,
                    CountryCode = "+91",
                    Phone = cleanPhone,
                    DateOfBirth = dobUtc,
                    MonthlyIncome = model.MonthlyIncome,
                    Address = model.Address.Trim(),
                    PinCode = model.PinCode.Trim(),
                    CreditStatus = "Pending",
                    KycStatus = "Pending"
                };

                _context.Customers.Add(customer);
            }

            // Generate 6-digit OTP
            string otp = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(model.Password);

            if (existingAuth != null)
            {
                existingAuth.Password = hashedPassword;
                existingAuth.FullName = model.Name.Trim();
                existingAuth.Role = "Borrower";
                existingAuth.IsVerified = false; // Mark unverified until OTP verification
                existingAuth.OtpCode = otp;
                existingAuth.OtpExpiry = DateTime.UtcNow.AddMinutes(5);
            }
            else
            {
                var newAuth = new AdminUser
                {
                    FullName = model.Name.Trim(),
                    Username = normalizedEmail,
                    Email = normalizedEmail,
                    Password = hashedPassword,
                    Role = "Borrower",
                    IsVerified = false, // Mark unverified until OTP verification
                    OtpCode = otp,
                    OtpExpiry = DateTime.UtcNow.AddMinutes(5)
                };
                _context.AdminUsers.Add(newAuth);
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
            var authUser = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

            if (authUser == null)
            {
                return RedirectToAction("Register");
            }

            if (authUser.IsVerified)
            {
                TempData["Success"] = "Your email is already verified. Please sign in.";
                return RedirectToAction("Login");
            }

            var model = new BorrowerVerifyOtpViewModel
            {
                Email = normalizedEmail
            };

            ViewBag.Email = normalizedEmail;
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
            var authUser = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

            if (authUser == null)
            {
                ViewBag.Error = "Account record not found for this email address. Please register again.";
                return View(model);
            }

            if (authUser.IsVerified)
            {
                TempData["Success"] = "Your email is already verified. Please sign in.";
                return RedirectToAction("Login");
            }

            int attempts = TempData["OtpAttempts"] != null ? Convert.ToInt32(TempData["OtpAttempts"]) : 0;
            if (attempts >= 5)
            {
                authUser.OtpCode = null;
                authUser.OtpExpiry = null;
                await _context.SaveChangesAsync();
                TempData.Remove("OtpAttempts");

                ViewBag.Email = normalizedEmail;
                ViewBag.Error = "Maximum verification attempts exceeded. For your security, this code has been invalidated. Please click 'Resend Code' to request a new code.";
                return View(model);
            }

            if (string.IsNullOrEmpty(authUser.OtpCode) || authUser.OtpExpiry == null || authUser.OtpExpiry < DateTime.UtcNow)
            {
                ViewBag.Email = normalizedEmail;
                ViewBag.Error = "The verification code has expired (5-minute limit). Please request a new code using the 'Resend Code' button below.";
                return View(model);
            }

            if (authUser.OtpCode.Trim() != model.Otp.Trim())
            {
                TempData["OtpAttempts"] = attempts + 1;
                int remaining = 5 - (attempts + 1);
                ViewBag.Email = normalizedEmail;
                ViewBag.Error = $"Invalid verification code. {remaining} attempt(s) remaining before code expires.";
                return View(model);
            }

            // Mark user verified and clear OTP
            authUser.IsVerified = true;
            authUser.OtpCode = null;
            authUser.OtpExpiry = null;
            await _context.SaveChangesAsync();
            TempData.Remove("OtpAttempts");

            // Find customer record and authenticate session
            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Email.ToLower() == normalizedEmail);
            if (customer != null)
            {
                await SignInBorrowerAsync(customer);
            }

            TempData["Success"] = "Email verified successfully! Welcome to FinGuard Microfinance Portal.";
            return RedirectToAction("Dashboard", "Portal");
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
            var authUser = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

            if (authUser == null)
            {
                TempData["Error"] = "Account record not found. Please register first.";
                return RedirectToAction("Register");
            }

            if (authUser.IsVerified)
            {
                TempData["Success"] = "Your email is already verified. Please sign in.";
                return RedirectToAction("Login");
            }

            string newOtp = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            authUser.OtpCode = newOtp;
            authUser.OtpExpiry = DateTime.UtcNow.AddMinutes(5);
            await _context.SaveChangesAsync();
            TempData["OtpAttempts"] = 0;

            bool sent = await _emailService.SendOtpEmailAsync(normalizedEmail, newOtp, authUser.FullName);
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

            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Email == email);
            if (customer != null)
            {
                ViewBag.Success = $"If an active borrower account exists for {email}, password recovery instructions have been dispatched. For immediate field assistance, visit your local Self-Help Group branch.";
            }
            else
            {
                ViewBag.Success = "If an active borrower account exists for this email, password recovery instructions have been dispatched.";
            }

            return View();
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
