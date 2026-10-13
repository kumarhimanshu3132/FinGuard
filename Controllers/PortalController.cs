using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinGuard.Data;
using FinGuard.Models;
using FinGuard.Services;

namespace FinGuard.Controllers
{
    [Authorize(AuthenticationSchemes = BorrowerAuthController.BorrowerAuthScheme)]
    public class PortalController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly EmailService _emailService;
        private readonly IBankAccountService _bankAccountService;
        private readonly ISimulatedDisbursementService _disbursementService;
        private readonly IRazorpayPaymentService _razorpayService;

        public PortalController(
            ApplicationDbContext context,
            EmailService emailService,
            IBankAccountService bankAccountService,
            ISimulatedDisbursementService disbursementService,
            IRazorpayPaymentService razorpayService)
        {
            _context = context;
            _emailService = emailService;
            _bankAccountService = bankAccountService;
            _disbursementService = disbursementService;
            _razorpayService = razorpayService;
        }

        private int GetCurrentCustomerId()
        {
            var claimVal = User.FindFirst("CustomerId")?.Value
                           ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (int.TryParse(claimVal, out int id))
            {
                return id;
            }

            // Fallback: look up by authenticated email
            var email = User.FindFirst(ClaimTypes.Email)?.Value;
            if (!string.IsNullOrEmpty(email))
            {
                var cust = _context.Customers.FirstOrDefault(c => c.Email == email);
                if (cust != null) return cust.CustomerId;
            }

            return 0;
        }

        [AllowAnonymous]
        public IActionResult Index()
        {
            return RedirectToAction(nameof(Landing));
        }

        [AllowAnonymous]
        public IActionResult Landing()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            int customerId = GetCurrentCustomerId();
            var customer = await _context.Customers
                .Include(c => c.Loans)
                .FirstOrDefaultAsync(c => c.CustomerId == customerId);

            if (customer == null)
            {
                return RedirectToAction("Login", "BorrowerAuth");
            }

            var allLoans = await _context.Loans
                .Where(l => l.CustomerId == customerId)
                .OrderByDescending(l => l.LoanId)
                .ToListAsync();

            var loanIds = allLoans.Select(l => l.LoanId).ToList();
            var repayments = await _context.Repayments
                .Where(r => loanIds.Contains(r.LoanId))
                .OrderByDescending(r => r.PaymentDate)
                .Take(5)
                .ToListAsync();

            var activeLoans = allLoans.Where(l => l.Status == "Active").ToList();
            var pendingLoans = allLoans.Where(l => l.Status == "Pending").ToList();
            var closedLoans = allLoans.Where(l => l.Status == "Closed").ToList();

            decimal totalSanctioned = allLoans.Where(l => l.Status == "Active" || l.Status == "Closed")
                .Sum(l => l.PrincipalAmount);

            decimal outstanding = activeLoans.Sum(l => l.RemainingBalance);
            decimal totalRepaid = await _context.Repayments
                .Where(r => loanIds.Contains(r.LoanId))
                .SumAsync(r => (decimal?)r.AmountPaid) ?? 0;

            var primaryActiveLoan = activeLoans.OrderBy(l => l.NextDueDate).FirstOrDefault();
            decimal nextEmi = primaryActiveLoan?.EMIAmount ?? 0;
            DateTime? nextDueDate = primaryActiveLoan?.NextDueDate;

            int progressPct = 0;
            if (totalRepaid + outstanding > 0)
            {
                progressPct = (int)Math.Min(100, Math.Round((totalRepaid / (totalRepaid + outstanding)) * 100));
            }

            // Authentic Microfinance Credit Limit Computation (Inspired by mPokket/Indian NBFC rules)
            decimal creditMultiplier = customer.KycStatus == "Verified" ? 2.5m : 1.5m;
            decimal baseLimit = Math.Round(customer.MonthlyIncome * creditMultiplier, 0);
            decimal totalApprovedCreditLimit = Math.Max(24000m, Math.Min(150000m, baseLimit));
            decimal availableLimit = Math.Max(0m, totalApprovedCreditLimit - outstanding);

            decimal foreclosureAmount = primaryActiveLoan != null ? Math.Round(primaryActiveLoan.RemainingBalance * 0.98m, 2) : 0m;
            decimal foreclosureSavings = primaryActiveLoan != null ? Math.Round(primaryActiveLoan.RemainingBalance - foreclosureAmount, 2) : 0m;

            var vm = new BorrowerDashboardViewModel
            {
                Customer = customer,
                ActiveLoans = activeLoans,
                PendingApplications = pendingLoans,
                ClosedLoans = closedLoans,
                RecentRepayments = repayments,
                TotalSanctionedPrincipal = totalSanctioned,
                TotalOutstandingBalance = outstanding,
                TotalRepaidAmount = totalRepaid,
                NextEmiAmount = nextEmi,
                NextEmiDueDate = nextDueDate,
                RepaymentProgressPercentage = progressPct,
                TotalApprovedCreditLimit = totalApprovedCreditLimit,
                AvailableCreditLimit = availableLimit,
                PrimaryLoan = primaryActiveLoan,
                ForeclosureAmount = foreclosureAmount,
                ForeclosureSavings = foreclosureSavings
            };

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> Loans(string tab = "active")
        {
            int customerId = GetCurrentCustomerId();
            var loans = await _context.Loans
                .Where(l => l.CustomerId == customerId)
                .OrderByDescending(l => l.LoanId)
                .ToListAsync();

            ViewBag.ActiveTab = tab;
            return View(loans);
        }

        [HttpGet]
        public async Task<IActionResult> LoanDetails(int id)
        {
            int customerId = GetCurrentCustomerId();

            // Strict Data Ownership Validation (Prevents IDOR)
            var loan = await _context.Loans
                .FirstOrDefaultAsync(l => l.LoanId == id && l.CustomerId == customerId);

            if (loan == null)
            {
                return NotFound();
            }

            var customer = await _context.Customers.FindAsync(customerId);
            if (customer == null) return RedirectToAction("Login", "BorrowerAuth");

            var repayments = await _context.Repayments
                .Where(r => r.LoanId == id)
                .OrderByDescending(r => r.PaymentDate)
                .ToListAsync();

            // Compute EMI Schedule for this loan
            var schedule = GenerateEmiSchedule(loan, repayments.Count);

            decimal foreclosureAmount = Math.Round(loan.RemainingBalance * 0.98m, 2);
            decimal foreclosureSavings = Math.Round(loan.RemainingBalance - foreclosureAmount, 2);
            int paidCount = repayments.Count;
            int pendingCount = Math.Max(0, loan.TenureMonths - paidCount);
            decimal totalPaidForLoan = repayments.Sum(r => r.AmountPaid);

            var vm = new LoanDetailsViewModel
            {
                Loan = loan,
                Customer = customer,
                Repayments = repayments,
                Schedule = schedule,
                TotalPaidAmount = totalPaidForLoan,
                ForeclosureAmount = foreclosureAmount,
                ForeclosureSavings = foreclosureSavings,
                PaidInstallmentsCount = paidCount,
                PendingInstallmentsCount = pendingCount
            };

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> Apply()
        {
            int customerId = GetCurrentCustomerId();
            var customer = await _context.Customers.FindAsync(customerId);

            if (customer == null)
            {
                return RedirectToAction("Login", "BorrowerAuth");
            }

            bool hasActiveLoan = await _context.Loans
                .AnyAsync(l => l.CustomerId == customerId && l.Status == "Active");

            bool hasPendingLoan = await _context.Loans
                .AnyAsync(l => l.CustomerId == customerId && l.Status == "Pending");

            ViewBag.Customer = customer;
            ViewBag.HasActiveLoan = hasActiveLoan;
            ViewBag.HasPendingLoan = hasPendingLoan;

            var vm = new LoanApplicationViewModel
            {
                MonthlyHouseholdIncome = customer.MonthlyIncome
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Apply(LoanApplicationViewModel model)
        {
            int customerId = GetCurrentCustomerId();
            var customer = await _context.Customers.FindAsync(customerId);

            if (customer == null) return RedirectToAction("Login", "BorrowerAuth");

            // Server-side business rule validation
            bool hasActiveLoan = await _context.Loans.AnyAsync(l => l.CustomerId == customerId && l.Status == "Active");
            bool hasPendingLoan = await _context.Loans.AnyAsync(l => l.CustomerId == customerId && l.Status == "Pending");

            if (hasActiveLoan)
            {
                ModelState.AddModelError(string.Empty, "You currently have an active loan. FinGuard policy permits one active loan at a time.");
            }

            if (hasPendingLoan)
            {
                ModelState.AddModelError(string.Empty, "You already have a loan application under review. Please await officer approval.");
            }

            if (!model.AgreeTerms)
            {
                ModelState.AddModelError(nameof(model.AgreeTerms), "You must confirm that you have read and agreed to the lending guidelines.");
            }

            if (!ModelState.IsValid)
            {
                ViewBag.Customer = customer;
                ViewBag.HasActiveLoan = hasActiveLoan;
                ViewBag.HasPendingLoan = hasPendingLoan;
                return View(model);
            }

            // Calculate standard Microfinance terms (12% fixed annual rate)
            double principal = (double)model.RequestedPrincipal;
            double annualRate = 12.0;
            double monthlyRate = (annualRate / 12.0) / 100.0;
            int n = model.TenureMonths;

            double emiCalc = (principal * monthlyRate * Math.Pow(1 + monthlyRate, n)) / (Math.Pow(1 + monthlyRate, n) - 1);
            decimal emi = (decimal)Math.Round(emiCalc, 2);

            int nextLoanId = (await _context.Loans.MaxAsync(l => (int?)l.LoanId) ?? 5000) + 1;

            var newApplication = new Loan
            {
                LoanId = nextLoanId,
                CustomerId = customerId,
                PrincipalAmount = model.RequestedPrincipal,
                InterestRate = annualRate,
                TenureMonths = model.TenureMonths,
                EMIAmount = emi,
                RemainingBalance = emi * n,
                NextDueDate = DateTime.UtcNow.AddMonths(1),
                Status = "Pending" // Persisted as Pending for Admin Approval workflow
            };

            _context.Loans.Add(newApplication);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Application #APP-{nextLoanId} successfully submitted! A loan officer will review your application.";
            return RedirectToAction(nameof(Applications));
        }

        [HttpGet]
        public async Task<IActionResult> Applications()
        {
            int customerId = GetCurrentCustomerId();
            var applications = await _context.Loans
                .Where(l => l.CustomerId == customerId && (l.Status == "Pending" || l.Status == "Active"))
                .OrderByDescending(l => l.LoanId)
                .ToListAsync();

            return View(applications);
        }

        [HttpGet]
        public async Task<IActionResult> Schedule(int? loanId)
        {
            int customerId = GetCurrentCustomerId();
            var activeLoans = await _context.Loans
                .Where(l => l.CustomerId == customerId && l.Status == "Active")
                .ToListAsync();

            Loan? targetLoan = null;
            if (loanId.HasValue)
            {
                targetLoan = activeLoans.FirstOrDefault(l => l.LoanId == loanId.Value);
            }

            targetLoan ??= activeLoans.FirstOrDefault();

            if (targetLoan == null)
            {
                // Fallback to most recent loan if no active loans
                targetLoan = await _context.Loans
                    .Where(l => l.CustomerId == customerId)
                    .OrderByDescending(l => l.LoanId)
                    .FirstOrDefaultAsync();
            }

            if (targetLoan == null)
            {
                return View(new List<EmiScheduleItemViewModel>());
            }

            int paidInstallments = await _context.Repayments
                .CountAsync(r => r.LoanId == targetLoan.LoanId);

            var customer = await _context.Customers.FindAsync(customerId);
            ViewBag.Customer = customer;
            ViewBag.ActiveLoans = activeLoans;
            ViewBag.SelectedLoan = targetLoan;
            ViewBag.PaidCount = paidInstallments;

            decimal foreclosureAmount = Math.Round(targetLoan.RemainingBalance * 0.98m, 2);
            decimal foreclosureSavings = Math.Round(targetLoan.RemainingBalance - foreclosureAmount, 2);
            ViewBag.ForeclosureAmount = foreclosureAmount;
            ViewBag.ForeclosureSavings = foreclosureSavings;

            var schedule = GenerateEmiSchedule(targetLoan, paidInstallments);
            return View(schedule);
        }

        [HttpGet]
        public async Task<IActionResult> Repayments()
        {
            int customerId = GetCurrentCustomerId();
            var loanIds = await _context.Loans
                .Where(l => l.CustomerId == customerId)
                .Select(l => l.LoanId)
                .ToListAsync();

            var repayments = await _context.Repayments
                .Where(r => loanIds.Contains(r.LoanId))
                .OrderByDescending(r => r.PaymentDate)
                .ToListAsync();

            return View(repayments);
        }

        [HttpGet]
        public async Task<IActionResult> Receipt(int id)
        {
            int customerId = GetCurrentCustomerId();

            // Safe ownership verification
            var repayment = await _context.Repayments.FindAsync(id);
            if (repayment == null) return NotFound();

            var loan = await _context.Loans
                .FirstOrDefaultAsync(l => l.LoanId == repayment.LoanId && l.CustomerId == customerId);

            if (loan == null)
            {
                return Forbid();
            }

            var customer = await _context.Customers.FindAsync(customerId);

            ViewBag.Customer = customer;
            ViewBag.Loan = loan;
            return View(repayment);
        }

        [HttpGet]
        public IActionResult PaymentStatus()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessPayment(int loanId, decimal amount, string paymentMethod = "UPI")
        {
            int customerId = GetCurrentCustomerId();
            var loan = await _context.Loans.FirstOrDefaultAsync(l => l.LoanId == loanId && l.CustomerId == customerId);
            if (loan == null) return NotFound();

            if (loan.Status != "Active" || loan.RemainingBalance <= 0)
            {
                TempData["Error"] = "This loan facility is not active or has already been fully settled.";
                return RedirectToAction("LoanDetails", new { id = loanId });
            }

            if (amount <= 0 || amount > loan.RemainingBalance + 500)
            {
                TempData["Error"] = "Please enter a valid installment or settlement amount.";
                return RedirectToAction("LoanDetails", new { id = loanId });
            }

            // Server-side Idempotency: Prevent duplicate submissions within 30 seconds
            var recentDuplicate = await _context.Repayments
                .AnyAsync(r => r.LoanId == loanId && r.AmountPaid == amount && r.PaymentDate > DateTime.UtcNow.AddSeconds(-30));
            if (recentDuplicate)
            {
                TempData["Error"] = "A payment transaction with this exact amount was just processed. Please check your receipts.";
                return RedirectToAction("LoanDetails", new { id = loanId });
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                int nextRepId = (await _context.Repayments.MaxAsync(r => (int?)r.RepaymentId) ?? 1000) + 1;
                var repayment = new Repayment
                {
                    RepaymentId = nextRepId,
                    LoanId = loan.LoanId,
                    AmountPaid = amount,
                    PaymentDate = DateTime.UtcNow,
                    CollectedBy = $"Digital Gateway ({paymentMethod.ToUpper()})"
                };

                _context.Repayments.Add(repayment);

                loan.RemainingBalance = Math.Max(0, loan.RemainingBalance - amount);
                loan.NextDueDate = loan.NextDueDate.AddMonths(1);

                if (loan.RemainingBalance <= 0)
                {
                    loan.RemainingBalance = 0;
                    loan.Status = "Closed";

                    var customer = await _context.Customers.FindAsync(customerId);
                    if (customer != null)
                    {
                        customer.CreditStatus = "Eligible";
                        _context.Customers.Update(customer);
                    }
                }

                _context.Loans.Update(loan);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["Success"] = $"Payment of ₹{amount:N2} processed successfully via {paymentMethod}!";
                return RedirectToAction("Receipt", new { id = repayment.RepaymentId });
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                TempData["Error"] = "Payment gateway simulation error. Please try again or pay through your center officer.";
                return RedirectToAction("LoanDetails", new { id = loanId });
            }
        }

        // ==========================================
        // RAZORPAY TEST MODE EMI PAYMENT & RECOVERY
        // ==========================================

        [HttpGet]
        public async Task<IActionResult> PayEmi(int loanId)
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return RedirectToAction("Login", "BorrowerAuth");

            var loan = await _context.Loans
                .FirstOrDefaultAsync(l => l.LoanId == loanId && l.CustomerId == customerId);

            if (loan == null)
            {
                TempData["Error"] = "Loan facility not found or access denied.";
                return RedirectToAction(nameof(Loans));
            }

            if (loan.Status != "Active" || loan.RemainingBalance <= 0)
            {
                TempData["Error"] = "This loan facility is not active or has already been fully paid.";
                return RedirectToAction(nameof(LoanDetails), new { id = loanId });
            }

            var customer = await _context.Customers.FindAsync(customerId);
            if (customer == null) return RedirectToAction("Login", "BorrowerAuth");

            decimal foreclosureAmount = Math.Round(loan.RemainingBalance * 0.98m, 2);
            decimal foreclosureSavings = Math.Round(loan.RemainingBalance - foreclosureAmount, 2);
            int paidCount = await _context.Repayments.CountAsync(r => r.LoanId == loanId && r.PaymentStatus == "Success");

            var publicSettings = _razorpayService.GetPublicSettings();

            var vm = new PayEmiViewModel
            {
                LoanId = loan.LoanId,
                Loan = loan,
                Customer = customer,
                EMIAmount = loan.EMIAmount,
                RemainingBalance = loan.RemainingBalance,
                ForeclosureAmount = foreclosureAmount,
                ForeclosureSavings = foreclosureSavings,
                NextDueDate = loan.NextDueDate,
                PaidInstallmentsCount = paidCount,
                TotalInstallmentsCount = loan.TenureMonths,
                RazorpayKeyId = publicSettings.KeyId,
                CompanyName = publicSettings.CompanyName,
                ThemeColor = publicSettings.ThemeColor,
                IsTestMode = true
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateRazorpayOrder([FromBody] CreateOrderRequest request)
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return Unauthorized(new { success = false, message = "Session expired. Please sign in." });

            if (request == null || request.Amount <= 0)
            {
                return BadRequest(new { success = false, message = "Invalid payment parameters." });
            }

            var result = await _razorpayService.CreateOrderAsync(customerId, request);
            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Json(result);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyRazorpayPayment([FromBody] PaymentVerificationRequest request)
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return Unauthorized(new { success = false, message = "Session expired. Please sign in." });

            if (request == null || string.IsNullOrWhiteSpace(request.RazorpayPaymentId))
            {
                return BadRequest(new { success = false, message = "Invalid payment response received." });
            }

            var result = await _razorpayService.ProcessSuccessfulPaymentAsync(customerId, request);
            if (!result.Success)
            {
                return BadRequest(result);
            }

            result.RedirectUrl = Url.Action(nameof(Receipt), new { id = result.RepaymentId });
            return Json(result);
        }

        [HttpPost]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> RazorpayWebhook()
        {
            using var reader = new StreamReader(Request.Body, Encoding.UTF8);
            string rawBody = await reader.ReadToEndAsync();

            string signature = Request.Headers["X-Razorpay-Signature"].ToString();
            if (string.IsNullOrEmpty(signature))
            {
                return BadRequest("Missing X-Razorpay-Signature header.");
            }

            var (success, message) = await _razorpayService.ProcessWebhookPaymentAsync(rawBody, signature);
            if (!success)
            {
                return BadRequest(message);
            }

            return Ok(new { status = "processed", message });
        }

        [HttpGet]
        public async Task<IActionResult> Document(int id, string type = "kfs")
        {
            int customerId = GetCurrentCustomerId();
            var loan = await _context.Loans.FirstOrDefaultAsync(l => l.LoanId == id && l.CustomerId == customerId);
            if (loan == null) return NotFound();

            var customer = await _context.Customers.FindAsync(customerId);
            ViewBag.Customer = customer;
            ViewBag.DocType = type.ToLower();

            return View(loan);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmailLoanDocuments(int id)
        {
            int customerId = GetCurrentCustomerId();
            var loan = await _context.Loans.FirstOrDefaultAsync(l => l.LoanId == id && l.CustomerId == customerId);
            if (loan == null) return NotFound();

            var customer = await _context.Customers.FindAsync(customerId);
            if (customer == null || string.IsNullOrWhiteSpace(customer.Email))
            {
                return Json(new { success = false, message = "Registered email not found for this profile." });
            }

            // Dispatch digital sanction confirmation via EmailService
            await _emailService.SendOtpEmailAsync(customer.Email, "SANCTION-" + loan.LoanId, customer.Name);

            return Json(new { success = true, message = $"Official loan documents for Facility #{loan.LoanId} dispatched to {customer.Email}." });
        }

        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            int customerId = GetCurrentCustomerId();
            var customer = await _context.Customers.FindAsync(customerId);

            if (customer == null) return RedirectToAction("Login", "BorrowerAuth");

            var vm = new BorrowerProfileUpdateViewModel
            {
                CustomerId = customer.CustomerId,
                Name = customer.Name,
                Email = customer.Email,
                Phone = customer.Phone,
                Address = customer.Address,
                PinCode = customer.PinCode
            };

            string? email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            var authUser = !string.IsNullOrEmpty(email)
                ? await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower())
                : null;

            ViewBag.Customer = customer;
            ViewBag.IsEmailVerified = authUser?.IsVerified ?? false;
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(BorrowerProfileUpdateViewModel model)
        {
            int customerId = GetCurrentCustomerId();
            var customer = await _context.Customers.FindAsync(customerId);

            if (customer == null) return RedirectToAction("Login", "BorrowerAuth");

            if (!ModelState.IsValid)
            {
                string? email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
                var authUser = !string.IsNullOrEmpty(email)
                    ? await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower())
                    : null;
                ViewBag.Customer = customer;
                ViewBag.IsEmailVerified = authUser?.IsVerified ?? false;
                return View("Profile", model);
            }

            customer.Address = model.Address.Trim();
            customer.PinCode = model.PinCode.Trim();

            _context.Customers.Update(customer);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Your residential address has been updated successfully.";
            return RedirectToAction(nameof(Profile));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(BorrowerChangePasswordViewModel model)
        {
            string? email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(email)) return RedirectToAction("Login", "BorrowerAuth");

            var authUser = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
            if (authUser == null) return RedirectToAction("Login", "BorrowerAuth");

            if (!ModelState.IsValid)
            {
                TempData["PasswordError"] = "Please fill in all required password fields correctly (minimum 8 characters).";
                return RedirectToAction(nameof(Profile));
            }

            bool isCurrentValid = false;
            try
            {
                if (!string.IsNullOrEmpty(authUser.Password))
                {
                    isCurrentValid = BCrypt.Net.BCrypt.Verify(model.CurrentPassword, authUser.Password);
                }
            }
            catch
            {
                isCurrentValid = false;
            }

            if (!isCurrentValid)
            {
                TempData["PasswordError"] = "The current password entered is incorrect.";
                return RedirectToAction(nameof(Profile));
            }

            authUser.Password = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
            _context.AdminUsers.Update(authUser);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Your password has been changed successfully.";
            return RedirectToAction(nameof(Profile));
        }

        [HttpGet]
        public async Task<IActionResult> Kyc()
        {
            int customerId = GetCurrentCustomerId();
            var customer = await _context.Customers.FindAsync(customerId);
            if (customer == null) return RedirectToAction("Login", "BorrowerAuth");

            return View(customer);
        }

        [HttpGet]
        public async Task<IActionResult> Notifications()
        {
            int customerId = GetCurrentCustomerId();
            var customer = await _context.Customers
                .Include(c => c.Loans)
                .FirstOrDefaultAsync(c => c.CustomerId == customerId);

            if (customer == null) return RedirectToAction("Login", "BorrowerAuth");

            var loanIds = customer.Loans?.Select(l => l.LoanId).ToList() ?? new List<int>();
            var recentRepayments = await _context.Repayments
                .Where(r => loanIds.Contains(r.LoanId))
                .OrderByDescending(r => r.PaymentDate)
                .Take(5)
                .ToListAsync();

            ViewBag.RecentRepayments = recentRepayments;
            return View(customer);
        }

        [HttpGet]
        public IActionResult Support()
        {
            return View();
        }

        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            return View();
        }

        // ==========================================
        // BANK ACCOUNT MANAGEMENT (BORROWER FACING)
        // ==========================================

        [HttpGet]
        public async Task<IActionResult> BankAccounts()
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return RedirectToAction("Login", "BorrowerAuth");

            var accounts = await _bankAccountService.GetCustomerAccountsAsync(customerId);
            var customer = await _context.Customers.FindAsync(customerId);
            ViewBag.Customer = customer;

            return View(accounts);
        }

        [HttpGet]
        public IActionResult AddBankAccount()
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return RedirectToAction("Login", "BorrowerAuth");

            var customer = _context.Customers.Find(customerId);
            var vm = new BankAccountCreateViewModel
            {
                AccountHolderName = customer?.Name ?? string.Empty
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddBankAccount(BankAccountCreateViewModel model)
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return RedirectToAction("Login", "BorrowerAuth");

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var (success, message, account) = await _bankAccountService.CreateAccountAsync(customerId, model);
            if (!success)
            {
                ModelState.AddModelError(string.Empty, message);
                return View(model);
            }

            TempData["Success"] = message;
            return RedirectToAction(nameof(BankAccounts));
        }

        [HttpGet]
        public async Task<IActionResult> EditBankAccount(int id)
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return RedirectToAction("Login", "BorrowerAuth");

            var account = await _bankAccountService.GetAccountByIdAsync(id, customerId);
            if (account == null)
            {
                TempData["Error"] = "Bank account not found or access denied.";
                return RedirectToAction(nameof(BankAccounts));
            }

            var vm = new BankAccountEditViewModel
            {
                Id = account.Id,
                BankName = account.BankName,
                AccountHolderName = account.AccountHolderName,
                CurrentMaskedNumber = account.MaskedAccountNumber,
                IFSCCode = account.IFSCCode,
                AccountType = account.AccountType,
                BranchName = account.BranchName,
                VerificationStatus = account.VerificationStatus,
                IsPrimary = account.IsPrimary
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditBankAccount(BankAccountEditViewModel model)
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return RedirectToAction("Login", "BorrowerAuth");

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var (success, message) = await _bankAccountService.UpdateAccountAsync(customerId, model);
            if (!success)
            {
                ModelState.AddModelError(string.Empty, message);
                return View(model);
            }

            TempData["Success"] = message;
            return RedirectToAction(nameof(BankAccounts));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetPrimaryBankAccount(int id)
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return RedirectToAction("Login", "BorrowerAuth");

            var (success, message) = await _bankAccountService.SetPrimaryAsync(customerId, id);
            if (success)
            {
                TempData["Success"] = message;
            }
            else
            {
                TempData["Error"] = message;
            }

            return RedirectToAction(nameof(BankAccounts));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteBankAccount(int id)
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return RedirectToAction("Login", "BorrowerAuth");

            var (success, message) = await _bankAccountService.DeleteAccountAsync(customerId, id);
            if (success)
            {
                TempData["Success"] = message;
            }
            else
            {
                TempData["Error"] = message;
            }

            return RedirectToAction(nameof(BankAccounts));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyBankAccountDemo(int id)
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return RedirectToAction("Login", "BorrowerAuth");

            var (success, message) = await _bankAccountService.SimulateVerificationAsync(customerId, id, "Verified");
            if (success)
            {
                TempData["Success"] = message;
            }
            else
            {
                TempData["Error"] = message;
            }

            return RedirectToAction(nameof(BankAccounts));
        }

        // ==========================================
        // SIMULATED LOAN DISBURSEMENT (BORROWER FACING)
        // ==========================================

        [HttpGet]
        public async Task<IActionResult> Disbursements()
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return RedirectToAction("Login", "BorrowerAuth");

            var eligibleLoans = await _disbursementService.GetEligibleLoansAsync(customerId);
            var bankAccounts = await _bankAccountService.GetCustomerAccountsAsync(customerId);
            var history = await _disbursementService.GetDisbursementHistoryAsync(customerId);
            var customer = await _context.Customers.FindAsync(customerId);

            ViewBag.Customer = customer;
            ViewBag.EligibleLoans = eligibleLoans;
            ViewBag.BankAccounts = bankAccounts;
            ViewBag.HasVerifiedAccount = bankAccounts.Any(a => a.VerificationStatus == "Verified");

            return View(history);
        }

        [HttpGet]
        public async Task<IActionResult> DisburseLoan(int id)
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return RedirectToAction("Login", "BorrowerAuth");

            var loan = await _disbursementService.GetEligibleLoanByIdAsync(id, customerId);
            if (loan == null)
            {
                TempData["Error"] = "This loan is not eligible for disbursement or does not belong to your account.";
                return RedirectToAction(nameof(Disbursements));
            }

            var allAccounts = await _bankAccountService.GetCustomerAccountsAsync(customerId);
            var verifiedAccounts = allAccounts.Where(a => a.VerificationStatus == "Verified").ToList();

            var (deductions, netPayout) = _disbursementService.CalculatePayoutBreakdown(loan.PrincipalAmount);

            int selectedAccountId = verifiedAccounts.FirstOrDefault(a => a.IsPrimary)?.Id
                                    ?? verifiedAccounts.FirstOrDefault()?.Id
                                    ?? 0;

            var vm = new DisbursementInitiateViewModel
            {
                LoanId = loan.LoanId,
                Loan = loan,
                PrincipalAmount = loan.PrincipalAmount,
                Deductions = deductions,
                NetPayout = netPayout,
                SelectedBankAccountId = selectedAccountId,
                AvailableBankAccounts = verifiedAccounts,
                TermsConfirmed = false
            };

            var customer = await _context.Customers.FindAsync(customerId);
            ViewBag.Customer = customer;
            ViewBag.HasVerifiedAccount = verifiedAccounts.Any();
            ViewBag.AllAccounts = allAccounts;

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmDisbursement(int loanId, int bankAccountId, bool termsConfirmed)
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return RedirectToAction("Login", "BorrowerAuth");

            if (!termsConfirmed)
            {
                TempData["Error"] = "You must acknowledge and confirm the simulated demo disclaimer before proceeding.";
                return RedirectToAction(nameof(DisburseLoan), new { id = loanId });
            }

            var (success, message, disbursement) = await _disbursementService.ExecuteSimulatedDisbursementAsync(
                customerId, loanId, bankAccountId);

            if (!success || disbursement == null)
            {
                TempData["Error"] = message;
                return RedirectToAction(nameof(DisburseLoan), new { id = loanId });
            }

            TempData["Success"] = message;
            return RedirectToAction(nameof(DisbursementReceipt), new { id = disbursement.Id });
        }

        [HttpGet]
        public async Task<IActionResult> DisbursementReceipt(int id)
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return RedirectToAction("Login", "BorrowerAuth");

            var disbursement = await _context.LoanDisbursements
                .Include(d => d.Loan)
                .Include(d => d.BankAccount)
                .FirstOrDefaultAsync(d => d.Id == id && d.CustomerId == customerId);

            if (disbursement == null)
            {
                TempData["Error"] = "Disbursement transaction receipt not found.";
                return RedirectToAction(nameof(Disbursements));
            }

            var customer = await _context.Customers.FindAsync(customerId);
            ViewBag.Customer = customer;

            return View(disbursement);
        }

        private List<EmiScheduleItemViewModel> GenerateEmiSchedule(Loan loan, int paidCount)
        {
            var list = new List<EmiScheduleItemViewModel>();
            if (loan.TenureMonths <= 0 || loan.EMIAmount <= 0) return list;

            decimal monthlyRate = (decimal)(loan.InterestRate / 12.0) / 100m;
            decimal balance = loan.PrincipalAmount;
            DateTime baseDate = loan.NextDueDate.AddMonths(-1);

            for (int i = 1; i <= loan.TenureMonths; i++)
            {
                decimal interest = Math.Round(balance * monthlyRate, 2);
                decimal principal = loan.EMIAmount - interest;
                if (principal < 0) principal = loan.EMIAmount;

                balance = Math.Max(0, balance - principal);

                string status;
                if (i <= paidCount)
                {
                    status = "Paid";
                }
                else if (i == paidCount + 1)
                {
                    status = "Due";
                }
                else
                {
                    status = "Upcoming";
                }

                list.Add(new EmiScheduleItemViewModel
                {
                    InstallmentNumber = i,
                    DueDate = baseDate.AddMonths(i),
                    EmiAmount = loan.EMIAmount,
                    PrincipalComponent = principal,
                    InterestComponent = interest,
                    RemainingBalanceAfter = balance,
                    Status = status
                });
            }

            return list;
        }
    }
}
