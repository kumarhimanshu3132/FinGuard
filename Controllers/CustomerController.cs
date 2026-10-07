using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinGuard.Data;
using FinGuard.Models;
using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Caching.Memory;
using FinGuard.Services;
using FinGuard.Utilities;
using Microsoft.AspNetCore.Authorization;

namespace FinGuard.Controllers
{
    public class CustomerController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly EmailService _emailService;
        private readonly IMemoryCache _cache;

        public CustomerController(ApplicationDbContext context, EmailService emailService, IMemoryCache cache)
        {
            _context = context;
            _emailService = emailService;
            _cache = cache;
        }

        public async Task<IActionResult> Index()
        {
            var customers = await _context.Customers.ToListAsync();
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
                _context.Add(customer);
                await _context.SaveChangesAsync();

                TempData["Success"] = "Customer onboarded successfully!";
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

        [HttpGet]
        public async Task<IActionResult> VerifyIdentity(int id)
        {
            var customer = await _context.Customers.FindAsync(id);
            if (customer == null) return NotFound();

            return View(customer);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyIdentity(int id, string pan, string idDocument, string otp)
        {
            var customer = await _context.Customers.FindAsync(id);
            if (customer == null) return NotFound();

            bool isDemoEmail = customer.Email == "test1@gmail.com" || customer.Email == "test2@gmail.com";
            bool isDemoVerified = isDemoEmail && (otp == "000000" || otp == "123456");
            bool isCacheVerified = _cache.TryGetValue(customer.Email, out string? cachedOtp) && cachedOtp == otp;

            if (!isDemoVerified && !isCacheVerified)
            {
                ViewBag.Error = "Invalid or expired OTP.";
                return View(customer);
            }

            string documentName = "";
            DateTime documentDob = DateTime.MinValue;
            string documentPinCode = "";

            // If it's a real email, strictly enforce format checks
            if (!isDemoEmail)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(pan, @"^[A-Z]{5}[0-9]{4}[A-Z]{1}$"))
                {
                    ModelState.AddModelError("pan", "Invalid PAN format. Must match ABCDE1234F.");
                    return View(customer);
                }
                if (string.IsNullOrWhiteSpace(idDocument) || idDocument.Length < 6)
                {
                    ModelState.AddModelError("idDocument", "Invalid Identity Document.");
                    return View(customer);
                }

                // TODO: Call real 3rd party verification service here and populate:
                // documentName = "Name from API";
                // documentDob = DateTime.Parse("DOB from API");
                // documentPinCode = "PIN from API";
                
                // For development fallback if no API exists:
                documentName = customer.Name;
                documentDob = customer.DateOfBirth;
                documentPinCode = customer.PinCode;
            }
            else
            {
                // Demo Identity Record
                documentName = "Test User";
                documentDob = new DateTime(2000, 1, 1);
                documentPinCode = "110001";
            }

            // Demographic Verification Matching
            bool nameMatches = customer.Name.Trim().Equals(documentName.Trim(), StringComparison.OrdinalIgnoreCase);
            bool dobMatches = customer.DateOfBirth.Date == documentDob.Date;
            bool pinMatches = customer.PinCode.Trim() == documentPinCode.Trim();

            if (!nameMatches || !dobMatches || !pinMatches)
            {
                customer.KycStatus = "Failed";
                await _context.SaveChangesAsync();
                
                string errorMsg = "Identity details mismatch. ";
                if (!nameMatches) errorMsg += "Name mismatch. ";
                if (!dobMatches) errorMsg += "DOB mismatch. ";
                if (!pinMatches) errorMsg += "PIN code mismatch. ";
                
                TempData["ErrorMessage"] = errorMsg.Trim();
                return RedirectToAction(nameof(Index));
            }

            customer.KycStatus = "Verified";
            customer.EncryptedPan = EncryptionHelper.Encrypt(pan);
            customer.EncryptedIdDocument = EncryptionHelper.Encrypt(idDocument);
            
            await _context.SaveChangesAsync();

            TempData["Success"] = "Identity verified successfully.";
            return RedirectToAction(nameof(Index));
        }
    }
}