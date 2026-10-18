using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinGuard.Data;
using FinGuard.Models;
using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Caching.Memory;
using FinGuard.Services;
using FinGuard.Services.Kyc;
using FinGuard.Utilities;
using Microsoft.AspNetCore.Authorization;

namespace FinGuard.Controllers
{
    public class CustomerController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly EmailService _emailService;
        private readonly IMemoryCache _cache;
        private readonly IKycVerificationService _kycService;

        public CustomerController(
            ApplicationDbContext context,
            EmailService emailService,
            IMemoryCache cache,
            IKycVerificationService kycService)
        {
            _context = context;
            _emailService = emailService;
            _cache = cache;
            _kycService = kycService;
        }

        public async Task<IActionResult> Index()
        {
            var customers = await _context.Customers
                .Include(c => c.Loans)
                .ToListAsync();

            bool hasChanges = false;
            foreach (var c in customers)
            {
                string expectedStatus;
                if (c.Loans != null && c.Loans.Any(l => (l.Status == "Active" || l.Status == "Approved") && l.RemainingBalance > 0))
                {
                    expectedStatus = "Active";
                }
                else if (c.Loans != null && c.Loans.Any(l => l.Status == "Pending"))
                {
                    expectedStatus = "Under Review";
                }
                else if (c.KycStatus == "Verified")
                {
                    expectedStatus = "Eligible";
                }
                else
                {
                    expectedStatus = "KYC Incomplete";
                }

                if (c.CreditStatus != expectedStatus)
                {
                    c.CreditStatus = expectedStatus;
                    _context.Customers.Update(c);
                    hasChanges = true;
                }
            }

            if (hasChanges)
            {
                await _context.SaveChangesAsync();
            }

            return View(customers);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Customer customer)
        {
            if (ModelState.IsValid)
            {
                // Prevent duplicate Customer ID
                bool customerExists = await _context.Customers.AnyAsync(c => c.CustomerId == customer.CustomerId);
                if (customerExists)
                {
                    ModelState.AddModelError("CustomerId", "This Customer ID is already allocated. Please choose a unique ID.");
                    return View(customer);
                }

                // Prevent duplicate registrations
                bool exists = await _context.Customers.AnyAsync(c =>
                    c.Email == customer.Email || c.Phone == customer.Phone);

                if (exists)
                {
                    ViewBag.InfoMessage = "A customer with this Phone number or Email already exists!";
                    return View(customer);
                }

                // Enforce UTC formatting for PostgreSQL compatibility
                if (customer.DateOfBirth.Kind == DateTimeKind.Unspecified)
                {
                    customer.DateOfBirth = DateTime.SpecifyKind(customer.DateOfBirth, DateTimeKind.Utc);
                }

                customer.CreditStatus = "Pending";
                customer.KycStatus = "Pending";
                customer.IsVerified = true;
                if (string.IsNullOrEmpty(customer.Password))
                {
                    customer.Password = BCrypt.Net.BCrypt.HashPassword("FinGuard@123");
                }

                _context.Add(customer);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"Customer #{customer.CustomerId} ({customer.Name}) onboarded successfully! Portal access credentials generated (Default Temporary Password: FinGuard@123). The customer must log in to the borrower portal to complete identity verification.";
                return RedirectToAction(nameof(Index));
            }
            return View(customer);
        }

        [HttpPost]
        public async Task<IActionResult> GenerateEmailOtp(string email)
        {
            if (string.IsNullOrEmpty(email)) return Json(new { success = false, message = "Email is required." });

            bool isDemoEmail = email == "test1@gmail.com" || email == "test2@gmail.com";
            if (isDemoEmail)
            {
                return Json(new { success = true, message = "Demo OTP sent: 000000" });
            }

            var otp = new Random().Next(100000, 999999).ToString();
            _cache.Set(email, otp, TimeSpan.FromMinutes(5));

            await _emailService.SendOtpEmailAsync(email, otp);

            return Json(new { success = true, message = "OTP sent successfully." });
        }

        [HttpPost]
        public IActionResult VerifyEmailOtp(string email, string otp)
        {
            bool isDemoEmail = email == "test1@gmail.com" || email == "test2@gmail.com";

            if (isDemoEmail && otp == "000000")
            {
                return Json(new { success = true, message = "OTP verified successfully." });
            }

            if (_cache.TryGetValue(email, out string? cachedOtp) && cachedOtp == otp)
            {
                return Json(new { success = true, message = "OTP verified successfully." });
            }

            return Json(new { success = false, message = "Invalid or expired OTP." });
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var customer = await _context.Customers.FindAsync(id);
            if (customer == null)
            {
                return NotFound();
            }
            return View(customer);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Customer customer)
        {
            if (id != customer.CustomerId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                var existingCustomer = await _context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.CustomerId == id);

                if (existingCustomer == null)
                {
                    return NotFound();
                }

                // Enforce UTC formatting for PostgreSQL compatibility
                if (customer.DateOfBirth.Kind == DateTimeKind.Unspecified)
                {
                    customer.DateOfBirth = DateTime.SpecifyKind(customer.DateOfBirth, DateTimeKind.Utc);
                }

                bool isChanged = existingCustomer.Name != customer.Name ||
                                 existingCustomer.Phone != customer.Phone ||
                                 existingCustomer.CountryCode != customer.CountryCode ||
                                 existingCustomer.Email != customer.Email ||
                                 existingCustomer.Address != customer.Address ||
                                 existingCustomer.PinCode != customer.PinCode ||
                                 existingCustomer.MonthlyIncome != customer.MonthlyIncome ||
                                 existingCustomer.DateOfBirth != customer.DateOfBirth;

                if (isChanged)
                {
                    customer.CreditStatus = existingCustomer.CreditStatus;

                    _context.Update(customer);
                    await _context.SaveChangesAsync();

                    ViewBag.SuccessMessage = "Customer details updated successfully!";
                }
                else
                {
                    ViewBag.InfoMessage = "No changes were made to the customer details.";
                }

                return View(customer);
            }
            return View(customer);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var customer = await _context.Customers
                .Include(c => c.Loans)
                .FirstOrDefaultAsync(c => c.CustomerId == id);

            if (customer == null)
            {
                return NotFound();
            }

            if (customer.Loans != null && customer.Loans.Any(l => l.Status == "Active"))
            {
                TempData["Error"] = "Cannot delete! This customer currently has an active loan.";
                return RedirectToAction(nameof(Index));
            }

            _context.Customers.Remove(customer);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Customer deleted successfully.";
            return RedirectToAction(nameof(Index));
        }
    }
}