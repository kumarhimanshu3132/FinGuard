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
using FinGuard.Services.Kyc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using FinGuard.Utilities;

namespace FinGuard.Controllers
{
    [Authorize(AuthenticationSchemes = BorrowerAuthController.BorrowerAuthScheme)]
    public class PortalController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly EmailService _emailService;
        private readonly IBankAccountService _bankAccountService;
        private readonly ISimulatedDisbursementService _disbursementService;
        private readonly IKycVerificationService _kycService;

        public PortalController(
            ApplicationDbContext context,
            EmailService emailService,
            IBankAccountService bankAccountService,
            ISimulatedDisbursementService disbursementService,
            IKycVerificationService kycService)
        {
            _context = context;
            _emailService = emailService;
            _bankAccountService = bankAccountService;
            _disbursementService = disbursementService;
            _kycService = kycService;
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

            if (customer.OtpCode == "FORCE_PASSWORD_RESET")
            {
                return RedirectToAction("SetNewPassword", "BorrowerAuth");
            }

            var allLoans = await _context.Loans
                .Where(l => l.CustomerId == customerId)
                .Include(l => l.Disbursements)!
                    .ThenInclude(d => d.BankAccount)
                .OrderByDescending(l => l.LoanId)
                .ToListAsync();

            var loanIds = allLoans.Select(l => l.LoanId).ToList();
            var allRepayments = await _context.Repayments
                .Where(r => loanIds.Contains(r.LoanId))
                .OrderByDescending(r => r.PaymentDate)
                .ToListAsync();

            var repayments = allRepayments
                .Take(5)
                .ToList();

            var loanRepaymentsMap = allRepayments
                .GroupBy(r => r.LoanId)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.AmountPaid));

            ViewBag.LoanRepaymentsMap = loanRepaymentsMap;

            var activeLoans = allLoans.Where(l => l.Status == "Active").ToList();
            var pendingLoans = allLoans.Where(l => l.Status == "Pending").ToList();
            var closedLoans = allLoans.Where(l => l.Status == "Closed").ToList();

            decimal totalSanctioned = allLoans.Where(l => l.Status == "Active" || l.Status == "Closed")
                .Sum(l => l.PrincipalAmount);

            decimal outstanding = activeLoans.Sum(l => l.RemainingBalance);
            decimal totalRepaid = allRepayments.Sum(r => r.AmountPaid);

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

            var bankAccounts = await _context.BankAccounts
                .Where(b => b.CustomerId == customerId)
                .OrderByDescending(b => b.IsPrimary)
                .ThenByDescending(b => b.CreatedAtUtc)
                .ToListAsync();

            var kycRecord = await _context.KycVerificationRecords
                .FirstOrDefaultAsync(k => k.CustomerId == customerId);

            var vm = new BorrowerDashboardViewModel
            {
                Customer = customer,
                ActiveLoans = activeLoans,
                PendingApplications = pendingLoans,
                ClosedLoans = closedLoans,
                AllLoans = allLoans,
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
                ForeclosureSavings = foreclosureSavings,
                BankAccounts = bankAccounts,
                KycRecord = kycRecord
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

        private async Task<List<BankAccountEligibilityDto>> GetBankAccountEligibilitiesAsync(int customerId, List<BankAccount> bankAccounts)
        {
            var activeLoans = await _context.Loans
                .Include(l => l.Disbursements)
                .Where(l => l.CustomerId == customerId && l.Status == "Active")
                .ToListAsync();

            var pendingLoans = await _context.Loans
                .Include(l => l.Disbursements)
                .Where(l => l.CustomerId == customerId && l.Status == "Pending")
                .ToListAsync();

            var activeLoanIds = activeLoans.Select(l => l.LoanId).ToList();
            var repayments = await _context.Repayments
                .Where(r => activeLoanIds.Contains(r.LoanId))
                .ToListAsync();

            var repaymentsMap = repayments
                .GroupBy(r => r.LoanId)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.AmountPaid));

            var result = new List<BankAccountEligibilityDto>();

            foreach (var bank in bankAccounts)
            {
                var item = new BankAccountEligibilityDto
                {
                    BankAccount = bank,
                    IsEligible = true
                };

                // Match active loan either via disbursement record or default primary if legacy
                var activeLoan = activeLoans.FirstOrDefault(l =>
                    l.Disbursements != null && l.Disbursements.Any(d => d.BankAccountId == bank.Id)
                );

                if (activeLoan == null && bankAccounts.Count == 1 && activeLoans.Any())
                {
                    activeLoan = activeLoans.FirstOrDefault();
                }

                if (activeLoan != null)
                {
                    decimal repaid = repaymentsMap.ContainsKey(activeLoan.LoanId) ? repaymentsMap[activeLoan.LoanId] : 0m;
                    decimal halfPrincipal = activeLoan.PrincipalAmount / 2m;
                    decimal pct = activeLoan.PrincipalAmount > 0 ? Math.Round((repaid / activeLoan.PrincipalAmount) * 100m, 1) : 0m;

                    item.ActiveLoanId = activeLoan.LoanId;
                    item.ActiveLoanPrincipal = activeLoan.PrincipalAmount;
                    item.RepaidAmount = repaid;
                    item.RequiredRepayment = halfPrincipal;
                    item.RepaymentPercentage = pct;

                    if (repaid < halfPrincipal)
                    {
                        item.IsEligible = false;
                        item.IneligibilityReason = $"Active Loan #LN-{activeLoan.LoanId} on this account has repaid ₹{repaid:N0} of ₹{activeLoan.PrincipalAmount:N0} ({pct}%). At least 50% repayment (₹{halfPrincipal:N0}) is required before applying again with this bank account.";
                    }
                }

                // Check pending application on this bank account
                var pendingLoan = pendingLoans.FirstOrDefault(l =>
                    l.Disbursements != null && l.Disbursements.Any(d => d.BankAccountId == bank.Id)
                );
                if (pendingLoan != null && item.IsEligible)
                {
                    item.IsEligible = false;
                    item.IneligibilityReason = $"Loan Application #APP-{pendingLoan.LoanId} on this bank account is currently under review by underwriting.";
                }

                result.Add(item);
            }

            return result;
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

            var bankAccounts = await _context.BankAccounts
                .Where(b => b.CustomerId == customerId)
                .OrderByDescending(b => b.IsPrimary)
                .ThenByDescending(b => b.CreatedAtUtc)
                .ToListAsync();

            bool hasBank = bankAccounts.Any();

            var kyc = await _context.KycVerificationRecords
                .FirstOrDefaultAsync(k => k.CustomerId == customerId);
            bool isPanVerified = (kyc != null && kyc.IsPanVerified) || !string.IsNullOrEmpty(customer.EncryptedPan);
            bool isAadhaarVerified = (kyc != null && kyc.IsAadhaarVerified) || !string.IsNullOrEmpty(customer.EncryptedIdDocument);
            bool isKycComplete = isPanVerified && isAadhaarVerified;

            var bankEligibilities = await GetBankAccountEligibilitiesAsync(customerId, bankAccounts);
            bool hasEligibleBank = bankEligibilities.Any(b => b.IsEligible);
            var firstEligibleBank = bankEligibilities.FirstOrDefault(b => b.IsEligible)?.BankAccount 
                ?? bankAccounts.FirstOrDefault();

            bool canApply = hasBank && isKycComplete && hasEligibleBank;

            ViewBag.Customer = customer;
            ViewBag.BankAccounts = bankAccounts;
            ViewBag.BankEligibilities = bankEligibilities;
            ViewBag.PrimaryBank = firstEligibleBank;
            ViewBag.HasBank = hasBank;
            ViewBag.HasEligibleBank = hasEligibleBank;
            ViewBag.KycRecord = kyc;
            ViewBag.IsPanVerified = isPanVerified;
            ViewBag.IsAadhaarVerified = isAadhaarVerified;
            ViewBag.IsKycComplete = isKycComplete;
            ViewBag.CanApply = canApply;

            var vm = new LoanApplicationViewModel
            {
                MonthlyHouseholdIncome = customer.MonthlyIncome,
                BankAccountId = firstEligibleBank?.Id
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

            // Server-side prerequisite validation: Bank Account + PAN + Aadhaar
            var bankAccounts = await _context.BankAccounts
                .Where(b => b.CustomerId == customerId)
                .OrderByDescending(b => b.IsPrimary)
                .ToListAsync();

            bool hasBank = bankAccounts.Any();
            var kyc = await _context.KycVerificationRecords.FirstOrDefaultAsync(k => k.CustomerId == customerId);
            bool isPanVerified = (kyc != null && kyc.IsPanVerified) || !string.IsNullOrEmpty(customer.EncryptedPan);
            bool isAadhaarVerified = (kyc != null && kyc.IsAadhaarVerified) || !string.IsNullOrEmpty(customer.EncryptedIdDocument);

            if (!hasBank)
            {
                ModelState.AddModelError(string.Empty, "You must link an authentic bank account before applying for a loan.");
            }
            if (!isPanVerified || !isAadhaarVerified)
            {
                ModelState.AddModelError(string.Empty, "You must complete PAN and Aadhaar identity verification (KYC) before applying for a loan.");
            }

            var bankEligibilities = await GetBankAccountEligibilitiesAsync(customerId, bankAccounts);

            int chosenBankId = model.BankAccountId ?? (bankAccounts.FirstOrDefault(b => b.IsPrimary)?.Id ?? bankAccounts.FirstOrDefault()?.Id ?? 0);
            var selectedEligibility = bankEligibilities.FirstOrDefault(b => b.BankAccount.Id == chosenBankId);

            if (selectedEligibility != null && !selectedEligibility.IsEligible)
            {
                ModelState.AddModelError(nameof(model.BankAccountId), selectedEligibility.IneligibilityReason ?? "This bank account is ineligible: at least 50% of the active loan on this account must be repaid before applying again.");
            }

            if (!model.AgreeTerms)
            {
                ModelState.AddModelError(nameof(model.AgreeTerms), "You must confirm that you have read and agreed to the lending guidelines.");
            }

            if (!ModelState.IsValid)
            {
                bool hasEligibleBank = bankEligibilities.Any(b => b.IsEligible);
                ViewBag.Customer = customer;
                ViewBag.BankAccounts = bankAccounts;
                ViewBag.BankEligibilities = bankEligibilities;
                ViewBag.PrimaryBank = bankAccounts.FirstOrDefault(b => b.Id == chosenBankId) ?? bankAccounts.FirstOrDefault();
                ViewBag.HasBank = hasBank;
                ViewBag.HasEligibleBank = hasEligibleBank;
                ViewBag.KycRecord = kyc;
                ViewBag.IsPanVerified = isPanVerified;
                ViewBag.IsAadhaarVerified = isAadhaarVerified;
                ViewBag.IsKycComplete = isPanVerified && isAadhaarVerified;
                ViewBag.CanApply = hasBank && isPanVerified && isAadhaarVerified && hasEligibleBank;
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
                Status = "Pending"
            };

            _context.Loans.Add(newApplication);

            // Link the selected or primary bank account as disbursement destination
            var bankToLink = (model.BankAccountId.HasValue 
                ? bankAccounts.FirstOrDefault(b => b.Id == model.BankAccountId.Value) 
                : null) ?? bankAccounts.FirstOrDefault();

            if (bankToLink != null)
            {
                var disbursement = new LoanDisbursement
                {
                    LoanId = nextLoanId,
                    CustomerId = customerId,
                    BankAccountId = bankToLink.Id,
                    TransactionReference = $"TXN-DISB-{nextLoanId}-{DateTime.UtcNow.Ticks % 1000000}",
                    PrincipalAmount = model.RequestedPrincipal,
                    Deductions = 0m,
                    NetDisbursedAmount = model.RequestedPrincipal,
                    Status = "Pending",
                    PaymentMode = "IMPS / NEFT",
                    Remarks = $"Disbursement destination: {bankToLink.BankName}"
                };
                _context.LoanDisbursements.Add(disbursement);
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = $"Application #APP-{nextLoanId} successfully submitted! Your request is currently under review.";
            return RedirectToAction(nameof(Applications));
        }

        [HttpGet]
        public async Task<IActionResult> Applications()
        {
            int customerId = GetCurrentCustomerId();
            var applications = await _context.Loans
                .Include(l => l.Disbursements)!
                .ThenInclude(d => d.BankAccount)
                .Where(l => l.CustomerId == customerId)
                .OrderByDescending(l => l.LoanId)
                .ToListAsync();

            return View(applications);
        }

        [HttpGet]
        public Task<IActionResult> LoanRequests()
        {
            return Applications();
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
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = "This loan facility is not active or has already been fully settled." });
                }
                TempData["Error"] = "This loan facility is not active or has already been fully settled.";
                return RedirectToAction("LoanDetails", new { id = loanId });
            }

            if (amount <= 0 || amount > loan.RemainingBalance)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = $"Payment amount cannot exceed the current outstanding balance of ₹{loan.RemainingBalance:N2}." });
                }
                TempData["Error"] = $"Payment amount cannot exceed the current outstanding balance of ₹{loan.RemainingBalance:N2}.";
                return RedirectToAction("LoanDetails", new { id = loanId });
            }

            // Server-side Idempotency: Prevent duplicate submissions within 15 seconds
            var recentDuplicate = await _context.Repayments
                .AnyAsync(r => r.LoanId == loanId && r.AmountPaid == amount && r.PaymentDate > DateTime.UtcNow.AddSeconds(-15));
            if (recentDuplicate)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = "A payment transaction with this exact amount was just processed. Please check your receipts." });
                }
                TempData["Error"] = "A payment transaction with this exact amount was just processed. Please check your receipts.";
                return RedirectToAction("LoanDetails", new { id = loanId });
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                int nextRepId = (await _context.Repayments.MaxAsync(r => (int?)r.RepaymentId) ?? 1000) + 1;
                string txnRef = $"TXN{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(1000, 9999)}";
                string bankRef = $"BANK{DateTime.UtcNow:yyyyMMdd}{loan.LoanId}{Random.Shared.Next(100, 999)}";

                var repayment = new Repayment
                {
                    RepaymentId = nextRepId,
                    LoanId = loan.LoanId,
                    AmountPaid = amount,
                    PaymentDate = DateTime.UtcNow,
                    CollectedBy = $"Digital Gateway ({paymentMethod})",
                    PaymentMethod = paymentMethod,
                    PaymentStatus = "Success",
                    IsTestMode = false,
                    TransactionReference = txnRef,
                    BankReference = bankRef
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

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new 
                    { 
                        success = true, 
                        repaymentId = repayment.RepaymentId,
                        message = $"Payment of ₹{amount:N2} processed successfully via {paymentMethod}!",
                        redirectUrl = Url.Action("Receipt", new { id = repayment.RepaymentId }) 
                    });
                }

                TempData["Success"] = $"Payment of ₹{amount:N2} processed successfully via {paymentMethod}!";
                return RedirectToAction("Receipt", new { id = repayment.RepaymentId });
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = "Payment processing error. Please try again." });
                }
                TempData["Error"] = "Payment processing error. Please try again or pay through your center officer.";
                return RedirectToAction("LoanDetails", new { id = loanId });
            }
        }

        // ==========================================
        // RAZORPAY TEST MODE EMI PAYMENT & RECOVERY
        // ==========================================

        [HttpGet]
        public async Task<IActionResult> PayEmi(int? loanId = null)
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return RedirectToAction("Login", "BorrowerAuth");

            Loan? loan = null;
            if (loanId.HasValue && loanId.Value > 0)
            {
                loan = await _context.Loans
                    .FirstOrDefaultAsync(l => l.LoanId == loanId.Value && l.CustomerId == customerId);
            }
            else
            {
                loan = await _context.Loans
                    .Where(l => l.CustomerId == customerId && l.Status == "Active" && l.RemainingBalance > 0)
                    .OrderByDescending(l => l.LoanId)
                    .FirstOrDefaultAsync();
            }

            if (loan == null)
            {
                TempData["Info"] = "You do not have any active loans requiring installment payments.";
                return RedirectToAction(nameof(Loans));
            }

            if (loan.Status != "Active" || loan.RemainingBalance <= 0)
            {
                TempData["Error"] = "This loan facility is not active or has already been fully paid.";
                return RedirectToAction(nameof(LoanDetails), new { id = loan.LoanId });
            }

            var customer = await _context.Customers.FindAsync(customerId);
            if (customer == null) return RedirectToAction("Login", "BorrowerAuth");

            var allActiveLoans = await _context.Loans
                .Where(l => l.CustomerId == customerId && l.Status == "Active" && l.RemainingBalance > 0)
                .OrderByDescending(l => l.LoanId)
                .ToListAsync();

            ViewBag.AllActiveLoans = allActiveLoans;

            decimal foreclosureAmount = Math.Round(loan.RemainingBalance * 0.98m, 2);
            decimal foreclosureSavings = Math.Round(loan.RemainingBalance - foreclosureAmount, 2);
            int paidCount = await _context.Repayments.CountAsync(r => r.LoanId == loan.LoanId && r.PaymentStatus == "Success");

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
                TotalInstallmentsCount = loan.TenureMonths
            };

            return View(vm);
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

            var kyc = await _context.KycVerificationRecords.FirstOrDefaultAsync(k => k.CustomerId == customerId);
            ViewBag.Customer = customer;
            ViewBag.IsEmailVerified = customer.IsVerified;
            ViewBag.IsMobileVerified = kyc != null && kyc.IsOtpVerified;
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
                var kyc = await _context.KycVerificationRecords.FirstOrDefaultAsync(k => k.CustomerId == customerId);
                ViewBag.Customer = customer;
                ViewBag.IsEmailVerified = customer.IsVerified;
                ViewBag.IsMobileVerified = kyc != null && kyc.IsOtpVerified;
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
            int customerId = GetCurrentCustomerId();
            var customer = await _context.Customers.FindAsync(customerId);
            if (customer == null)
            {
                string? email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
                if (!string.IsNullOrEmpty(email))
                {
                    customer = await _context.Customers.FirstOrDefaultAsync(c => c.Email.ToLower() == email.ToLower());
                }
            }

            if (customer == null) return RedirectToAction("Login", "BorrowerAuth");

            if (!ModelState.IsValid)
            {
                TempData["PasswordError"] = "Please fill in all required password fields correctly (minimum 8 characters).";
                return RedirectToAction(nameof(Profile));
            }

            bool isCurrentValid = false;
            try
            {
                if (!string.IsNullOrEmpty(customer.Password))
                {
                    isCurrentValid = BCrypt.Net.BCrypt.Verify(model.CurrentPassword, customer.Password);
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

            customer.Password = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
            _context.Customers.Update(customer);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Your password has been changed successfully.";
            return RedirectToAction(nameof(Profile));
        }

        [HttpGet]
        public async Task<IActionResult> Kyc()
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return RedirectToAction("Login", "BorrowerAuth");

            var vm = await _kycService.GetKycDashboardAsync(customerId);
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InitiateAadhaar([FromBody] AadhaarInitiateRequest request)
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return Unauthorized(new { success = false, message = "Session expired. Please sign in." });

            if (!ModelState.IsValid)
            {
                var errors = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return BadRequest(new { success = false, message = errors });
            }

            string? ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var result = await _kycService.InitiateAadhaarVerificationAsync(customerId, request, ipAddress);

            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Json(result);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyAadhaarOtp([FromBody] OtpVerifyRequest request)
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return Unauthorized(new { success = false, message = "Session expired. Please sign in." });

            if (!ModelState.IsValid)
            {
                var errors = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return BadRequest(new { success = false, message = errors });
            }

            string? ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var result = await _kycService.VerifyAadhaarOtpAsync(customerId, request, ipAddress);

            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Json(result);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendAadhaarOtp()
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return Unauthorized(new { success = false, message = "Session expired. Please sign in." });

            string? ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var result = await _kycService.ResendAadhaarOtpAsync(customerId, ipAddress);

            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Json(result);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyPan([FromBody] PanVerifyRequest request)
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return Unauthorized(new { success = false, message = "Session expired. Please sign in." });

            if (!ModelState.IsValid)
            {
                var errors = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return BadRequest(new { success = false, message = errors });
            }

            string? ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var result = await _kycService.VerifyPanAsync(customerId, request, ipAddress);

            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Json(result);
        }

        [HttpGet]
        public async Task<IActionResult> KycStatusJson()
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return Unauthorized(new { success = false, message = "Session expired." });

            var status = await _kycService.GetKycStatusAsync(customerId);
            return Json(status);
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

            if (account != null)
            {
                account.VerificationStatus = "Verified";
                await _context.SaveChangesAsync();
            }

            TempData["Success"] = $"Bank account ({model.BankName}) linked and verified successfully for loan processing!";
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

        // ========================================================
        // COLLEGE PROJECT WORKFLOW ACTIONS (BANK, PAN, AADHAAR, LOAN)
        // ========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickAddBank([FromBody] QuickBankRequest request)
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return Unauthorized(new { success = false, message = "Session expired. Please sign in." });

            if (request == null || string.IsNullOrWhiteSpace(request.BankName) || string.IsNullOrWhiteSpace(request.AccountNumber))
            {
                return BadRequest(new { success = false, message = "Please provide valid bank details." });
            }

            string cleanAcc = request.AccountNumber.Trim();
            string cleanConfirm = (request.ConfirmAccountNumber ?? string.Empty).Trim();
            if (cleanAcc != cleanConfirm)
            {
                return BadRequest(new { success = false, message = "Account numbers do not match." });
            }

            if (cleanAcc.Length < 9 || cleanAcc.Length > 18 || !cleanAcc.All(char.IsDigit))
            {
                return BadRequest(new { success = false, message = "Account number must be 9 to 18 numeric digits." });
            }

            string ifsc = (request.IFSCCode ?? string.Empty).Trim().ToUpperInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(ifsc, @"^[A-Z]{4}0[A-Z0-9]{6}$"))
            {
                return BadRequest(new { success = false, message = "Invalid IFSC Code. Must be 11 characters (e.g., SBIN0001234)." });
            }

            var customer = await _context.Customers.FindAsync(customerId);
            if (customer == null) return NotFound(new { success = false, message = "Customer not found." });

            string holderName = string.IsNullOrWhiteSpace(request.AccountHolderName) ? customer.Name : request.AccountHolderName.Trim();
            string last4 = cleanAcc.Length >= 4 ? cleanAcc.Substring(cleanAcc.Length - 4) : cleanAcc;
            string masked = $"•••• •••• {last4}";

            // Unset previous primary accounts
            var existingAccounts = await _context.BankAccounts.Where(b => b.CustomerId == customerId).ToListAsync();
            foreach (var acc in existingAccounts)
            {
                acc.IsPrimary = false;
            }

            var newAcc = new BankAccount
            {
                CustomerId = customerId,
                BankName = request.BankName.Trim(),
                AccountHolderName = holderName,
                EncryptedAccountNumber = EncryptionHelper.Encrypt(cleanAcc),
                MaskedAccountNumber = masked,
                IFSCCode = ifsc,
                AccountType = request.AccountType == "Current" ? "Current" : "Savings",
                BranchName = string.IsNullOrWhiteSpace(request.BranchName) ? "Main Branch" : request.BranchName.Trim(),
                VerificationStatus = "Verified", // Verified for immediate usability in college demo
                IsPrimary = true,
                CreatedAtUtc = DateTime.UtcNow
            };

            _context.BankAccounts.Add(newAcc);
            await _context.SaveChangesAsync();

            return Json(new { 
                success = true, 
                message = $"{request.BankName} Account ending in {last4} added and verified successfully!",
                bankName = newAcc.BankName,
                maskedNumber = newAcc.MaskedAccountNumber,
                ifsc = newAcc.IFSCCode,
                id = newAcc.Id
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InitiatePanOtp([FromBody] QuickPanOtpRequest request)
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return Unauthorized(new { success = false, message = "Session expired." });

            string pan = (request?.PanNumber ?? string.Empty).Trim().ToUpperInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(pan, @"^[A-Z]{5}[0-9]{4}[A-Z]{1}$"))
            {
                return BadRequest(new { success = false, message = "Invalid PAN format. Must be 5 letters, 4 numbers, 1 letter (e.g. ABCDE1234F)." });
            }

            // Generate 6-digit OTP for verification
            string authOtp = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 999999).ToString();
            string cacheKey = $"DEMO_PAN_OTP_{customerId}_{pan}";
            
            var cache = HttpContext.RequestServices.GetService<IMemoryCache>();
            cache?.Set(cacheKey, authOtp, TimeSpan.FromMinutes(15));

            string maskedPan = $"{pan.Substring(0, 3)}•••••{pan.Substring(8)}";

            return Json(new {
                success = true,
                otpCode = authOtp,
                maskedPan = maskedPan,
                message = $"Verification Code generated for PAN {maskedPan}!"
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyPanOtp([FromBody] QuickVerifyPanRequest request)
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return Unauthorized(new { success = false, message = "Session expired." });

            string pan = (request?.PanNumber ?? string.Empty).Trim().ToUpperInvariant();
            string enteredOtp = (request?.Otp ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(pan) || enteredOtp.Length != 6)
            {
                return BadRequest(new { success = false, message = "Please enter the 6-digit OTP code." });
            }

            string cacheKey = $"DEMO_PAN_OTP_{customerId}_{pan}";
            var cache = HttpContext.RequestServices.GetService<IMemoryCache>();
            
            bool isMatch = false;
            if (cache != null && cache.TryGetValue(cacheKey, out string? expectedOtp))
            {
                isMatch = string.Equals(expectedOtp, enteredOtp, StringComparison.Ordinal);
            }
            else
            {
                // Fallback for demonstration
                isMatch = enteredOtp.Length == 6;
            }

            if (!isMatch)
            {
                return BadRequest(new { success = false, message = "Incorrect OTP code. Please use the demo code shown on screen." });
            }

            var customer = await _context.Customers.FindAsync(customerId);
            if (customer == null) return NotFound(new { success = false, message = "Customer not found." });

            var kycRecord = await _context.KycVerificationRecords.FirstOrDefaultAsync(k => k.CustomerId == customerId);
            if (kycRecord == null)
            {
                kycRecord = new KycVerificationRecord
                {
                    CustomerId = customerId,
                    CreatedAtUtc = DateTime.UtcNow
                };
                _context.KycVerificationRecords.Add(kycRecord);
            }

            string maskedPan = $"{pan.Substring(0, 3)}•••••{pan.Substring(8)}";
            customer.EncryptedPan = EncryptionHelper.Encrypt(pan);
            
            kycRecord.PanStatus = KycStageStatus.Verified;
            kycRecord.MaskedPan = maskedPan;
            kycRecord.PanHolderName = customer.Name;
            kycRecord.PanVerifiedAtUtc = DateTime.UtcNow;
            kycRecord.PanTransactionRef = "DEMO-PAN-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
            kycRecord.UpdatedAtUtc = DateTime.UtcNow;

            if (kycRecord.AadhaarStatus == KycStageStatus.Verified)
            {
                customer.KycStatus = "Verified";
                kycRecord.OverallKycStatus = OverallKycStatusConstants.Verified;
                kycRecord.CompletedAtUtc = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            return Json(new {
                success = true,
                maskedPan = maskedPan,
                overallKycStatus = customer.KycStatus,
                message = "PAN Card verified successfully!"
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InitiateAadhaarOtp([FromBody] QuickAadhaarOtpRequest request)
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return Unauthorized(new { success = false, message = "Session expired." });

            string aadhaar = (request?.AadhaarNumber ?? string.Empty).Replace(" ", "").Trim();
            if (aadhaar.Length != 12 || !aadhaar.All(char.IsDigit))
            {
                return BadRequest(new { success = false, message = "Aadhaar number must be exactly 12 numeric digits." });
            }

            string authOtp = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 999999).ToString();
            string cacheKey = $"DEMO_AADHAAR_OTP_{customerId}_{aadhaar}";
            
            var cache = HttpContext.RequestServices.GetService<IMemoryCache>();
            cache?.Set(cacheKey, authOtp, TimeSpan.FromMinutes(15));

            string last4 = aadhaar.Substring(8);
            string maskedAadhaar = $"•••• •••• {last4}";

            return Json(new {
                success = true,
                otpCode = authOtp,
                maskedAadhaar = maskedAadhaar,
                message = $"UIDAI Verification Code generated for Aadhaar {maskedAadhaar}!"
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickVerifyAadhaarOtp([FromBody] QuickVerifyAadhaarRequest request)
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return Unauthorized(new { success = false, message = "Session expired." });

            string aadhaar = (request?.AadhaarNumber ?? string.Empty).Replace(" ", "").Trim();
            string enteredOtp = (request?.Otp ?? string.Empty).Trim();

            if (aadhaar.Length != 12 || enteredOtp.Length != 6)
            {
                return BadRequest(new { success = false, message = "Please enter a valid 12-digit Aadhaar and 6-digit OTP." });
            }

            string cacheKey = $"DEMO_AADHAAR_OTP_{customerId}_{aadhaar}";
            var cache = HttpContext.RequestServices.GetService<IMemoryCache>();
            
            bool isMatch = false;
            if (cache != null && cache.TryGetValue(cacheKey, out string? expectedOtp))
            {
                isMatch = string.Equals(expectedOtp, enteredOtp, StringComparison.Ordinal);
            }
            else
            {
                isMatch = enteredOtp.Length == 6;
            }

            if (!isMatch)
            {
                return BadRequest(new { success = false, message = "Incorrect OTP code. Please use the demo code shown on screen." });
            }

            var customer = await _context.Customers.FindAsync(customerId);
            if (customer == null) return NotFound(new { success = false, message = "Customer not found." });

            var kycRecord = await _context.KycVerificationRecords.FirstOrDefaultAsync(k => k.CustomerId == customerId);
            if (kycRecord == null)
            {
                kycRecord = new KycVerificationRecord
                {
                    CustomerId = customerId,
                    CreatedAtUtc = DateTime.UtcNow
                };
                _context.KycVerificationRecords.Add(kycRecord);
            }

            string last4 = aadhaar.Substring(8);
            string maskedAadhaar = $"•••• •••• {last4}";

            customer.EncryptedIdDocument = EncryptionHelper.Encrypt(aadhaar);
            
            kycRecord.AadhaarStatus = KycStageStatus.Verified;
            kycRecord.OtpStatus = KycStageStatus.Verified;
            kycRecord.MaskedAadhaar = maskedAadhaar;
            kycRecord.AadhaarVerifiedAtUtc = DateTime.UtcNow;
            kycRecord.OtpVerifiedAtUtc = DateTime.UtcNow;
            kycRecord.AadhaarTransactionRef = "DEMO-UIDAI-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
            kycRecord.UpdatedAtUtc = DateTime.UtcNow;

            if (kycRecord.PanStatus == KycStageStatus.Verified)
            {
                customer.KycStatus = "Verified";
                kycRecord.OverallKycStatus = OverallKycStatusConstants.Verified;
                kycRecord.CompletedAtUtc = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            return Json(new {
                success = true,
                maskedAadhaar = maskedAadhaar,
                overallKycStatus = customer.KycStatus,
                message = "Aadhaar Card verified successfully!"
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickApplyLoan([FromBody] QuickLoanApplyRequest request)
        {
            int customerId = GetCurrentCustomerId();
            if (customerId == 0) return Unauthorized(new { success = false, message = "Session expired." });

            if (request == null || request.RequestedPrincipal < 5000 || request.RequestedPrincipal > 1000000 || request.TenureMonths < 3 || request.TenureMonths > 120)
            {
                return BadRequest(new { success = false, message = "Please specify a valid loan amount (₹5,000 to ₹10,00,000) and tenure (up to 120 months / 10 years)." });
            }

            bool hasActive = await _context.Loans.AnyAsync(l => l.CustomerId == customerId && (l.Status == "Active" || l.Status == "Approved"));
            if (hasActive)
            {
                return BadRequest(new { success = false, message = "You currently have an active/approved loan. Please clear previous loan first." });
            }

            bool hasPending = await _context.Loans.AnyAsync(l => l.CustomerId == customerId && l.Status == "Pending");
            if (hasPending)
            {
                return BadRequest(new { success = false, message = "You already have a loan application in review by FinGuard Admin." });
            }

            double principal = (double)request.RequestedPrincipal;
            double annualRate = 12.0;
            double monthlyRate = (annualRate / 12.0) / 100.0;
            int n = request.TenureMonths;

            double emiCalc = (principal * monthlyRate * Math.Pow(1 + monthlyRate, n)) / (Math.Pow(1 + monthlyRate, n) - 1);
            decimal emi = (decimal)Math.Round(emiCalc, 2);

            int nextLoanId = (await _context.Loans.MaxAsync(l => (int?)l.LoanId) ?? 5000) + 1;

            var newLoan = new Loan
            {
                LoanId = nextLoanId,
                CustomerId = customerId,
                PrincipalAmount = request.RequestedPrincipal,
                InterestRate = annualRate,
                TenureMonths = n,
                EMIAmount = emi,
                RemainingBalance = emi * n,
                NextDueDate = DateTime.UtcNow.AddMonths(1),
                Status = "Pending",
                DisbursementStatus = "Undisbursed"
            };

            _context.Loans.Add(newLoan);
            await _context.SaveChangesAsync();

            return Json(new {
                success = true,
                loanId = nextLoanId,
                principal = request.RequestedPrincipal,
                emi = emi,
                message = $"Loan Application #APP-{nextLoanId} submitted successfully! Your request has reached FinGuard Admin."
            });
        }
    }

    public class QuickBankRequest
    {
        public string BankName { get; set; } = string.Empty;
        public string AccountHolderName { get; set; } = string.Empty;
        public string AccountNumber { get; set; } = string.Empty;
        public string ConfirmAccountNumber { get; set; } = string.Empty;
        public string IFSCCode { get; set; } = string.Empty;
        public string AccountType { get; set; } = "Savings";
        public string? BranchName { get; set; }
    }

    public class QuickPanOtpRequest
    {
        public string PanNumber { get; set; } = string.Empty;
    }

    public class QuickVerifyPanRequest
    {
        public string PanNumber { get; set; } = string.Empty;
        public string Otp { get; set; } = string.Empty;
    }

    public class QuickAadhaarOtpRequest
    {
        public string AadhaarNumber { get; set; } = string.Empty;
    }

    public class QuickVerifyAadhaarRequest
    {
        public string AadhaarNumber { get; set; } = string.Empty;
        public string Otp { get; set; } = string.Empty;
    }

    public class QuickLoanApplyRequest
    {
        public decimal RequestedPrincipal { get; set; }
        public int TenureMonths { get; set; }
        public string Purpose { get; set; } = "Working Capital / Personal Need";
        public int? BankAccountId { get; set; }
    }
}
