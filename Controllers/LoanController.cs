using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using FinGuard.Data;
using FinGuard.Models;
using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Authorization;

namespace FinGuard.Controllers
{
    public class LoanController : Controller
    {
        private readonly ApplicationDbContext _context;

        public LoanController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var loans = await _context.Loans.OrderByDescending(l => l.LoanId).ToListAsync();
            var customerIds = loans.Select(l => l.CustomerId).Distinct().ToList();
            var customers = await _context.Customers
                .Include(c => c.BankAccounts)
                .Include(c => c.KycVerificationRecord)
                .Where(c => customerIds.Contains(c.CustomerId))
                .ToDictionaryAsync(c => c.CustomerId);
            ViewBag.CustomerMap = customers;
            return View(loans);
        }

        public async Task<IActionResult> Create()
        {
            var customers = await _context.Customers.ToListAsync();
            ViewBag.Customers = new SelectList(customers, "CustomerId", "Name");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Loan loan)
        {
            if (ModelState.IsValid)
            {
                bool loanExists = await _context.Loans.AnyAsync(l => l.LoanId == loan.LoanId);
                if (loanExists)
                {
                    ModelState.AddModelError("LoanId", "This Loan ID is already allocated. Please assign a different Loan ID.");
                    var custList = await _context.Customers.ToListAsync();
                    ViewBag.Customers = new SelectList(custList, "CustomerId", "Name");
                    return View(loan);
                }

                var existingCustomer = await _context.Customers.FindAsync(loan.CustomerId);
                if (existingCustomer == null || existingCustomer.KycStatus != "Verified")
                {
                    ModelState.AddModelError("CustomerId", "This customer has not completed KYC verification.");
                    var custList = await _context.Customers.ToListAsync();
                    ViewBag.Customers = new SelectList(custList, "CustomerId", "Name");
                    return View(loan);
                }

                // Prevent multiple active loans for a single customer
                bool hasActiveLoan = await _context.Loans
                    .AnyAsync(l => l.CustomerId == loan.CustomerId && l.Status == "Active");

                if (hasActiveLoan)
                {
                    ModelState.AddModelError("CustomerId", "This customer already has an active loan. Clear previous loan first.");
                    var custList = await _context.Customers.ToListAsync();
                    ViewBag.Customers = new SelectList(custList, "CustomerId", "Name");
                    return View(loan);
                }

                // Ensure tenure is positive
                loan.TenureMonths = Math.Abs(loan.TenureMonths);

                double p = (double)loan.PrincipalAmount;
                double r = (loan.InterestRate / 12) / 100;
                int n = loan.TenureMonths;

                if (r > 0 && n > 0)
                {
                    double emi = (p * r * Math.Pow(1 + r, n)) / (Math.Pow(1 + r, n) - 1);
                    loan.EMIAmount = (decimal)Math.Round(emi, 2);
                    loan.RemainingBalance = loan.EMIAmount * n;
                }
                else if (n > 0)
                {
                    loan.EMIAmount = loan.PrincipalAmount / n;
                    loan.RemainingBalance = loan.PrincipalAmount;
                }
                else
                {
                    loan.EMIAmount = 0;
                    loan.RemainingBalance = loan.PrincipalAmount;
                }

                loan.NextDueDate = DateTime.UtcNow.AddMonths(1);
                loan.Status = "Active";

                _context.Loans.Add(loan);

                var customer = await _context.Customers.FindAsync(loan.CustomerId);
                if (customer != null)
                {
                    customer.CreditStatus = "Active";
                    _context.Customers.Update(customer);
                }

                await _context.SaveChangesAsync();

                TempData["Success"] = "Loan issued successfully and Customer is now Active!";
                return RedirectToAction(nameof(Index));
            }

            var customers = await _context.Customers.ToListAsync();
            ViewBag.Customers = new SelectList(customers, "CustomerId", "Name");
            return View(loan);
        }

        // Dedicated Loan Requests Page for Admin Review
        [HttpGet]
        public async Task<IActionResult> Requests(string? status = null)
        {
            var query = _context.Loans
                .Include(l => l.Disbursements)
                .AsQueryable();

            if (!string.IsNullOrEmpty(status) && status != "All")
            {
                if (status == "Approved")
                {
                    query = query.Where(l => (l.Status == "Active" || l.Status == "Approved") && l.RemainingBalance > 0);
                }
                else if (status == "Closed")
                {
                    query = query.Where(l => l.Status == "Closed" || l.RemainingBalance <= 0);
                }
                else
                {
                    query = query.Where(l => l.Status == status);
                }
            }

            var loans = await query.OrderByDescending(l => l.LoanId).ToListAsync();

            var customerIds = loans.Select(l => l.CustomerId).Distinct().ToList();
            var customers = await _context.Customers
                .Include(c => c.BankAccounts)
                .Include(c => c.KycVerificationRecord)
                .Where(c => customerIds.Contains(c.CustomerId))
                .ToDictionaryAsync(c => c.CustomerId);

            var bankAccountIds = loans
                .SelectMany(l => l.Disbursements ?? Enumerable.Empty<LoanDisbursement>())
                .Select(d => d.BankAccountId)
                .Distinct()
                .ToList();

            var bankAccounts = await _context.BankAccounts
                .Where(b => bankAccountIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id);

            ViewBag.PendingCount = await _context.Loans.CountAsync(l => l.Status == "Pending");
            ViewBag.ApprovedCount = await _context.Loans.CountAsync(l => (l.Status == "Active" || l.Status == "Approved") && l.RemainingBalance > 0);
            ViewBag.ClosedCount = await _context.Loans.CountAsync(l => l.Status == "Closed" || l.RemainingBalance <= 0);
            ViewBag.RejectedCount = await _context.Loans.CountAsync(l => l.Status == "Rejected");
            ViewBag.TotalCount = await _context.Loans.CountAsync();

            ViewBag.CustomerMap = customers;
            ViewBag.BankAccountMap = bankAccounts;
            ViewBag.CurrentFilter = status ?? "Pending";

            return View(loans);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id, string? returnUrl = null)
        {
            var loan = await _context.Loans
                .Include(l => l.Disbursements)
                .FirstOrDefaultAsync(l => l.LoanId == id);

            if (loan == null)
            {
                TempData["Error"] = "Loan application not found.";
                return RedirectToAction(returnUrl == "Requests" ? nameof(Requests) : nameof(Index));
            }

            if (loan.Status == "Pending")
            {
                loan.Status = "Active";
                loan.DisbursementStatus = "Disbursed";

                var customer = await _context.Customers
                    .Include(c => c.BankAccounts)
                    .FirstOrDefaultAsync(c => c.CustomerId == loan.CustomerId);

                if (customer != null && customer.CreditStatus != "Active")
                {
                    customer.CreditStatus = "Active";
                    _context.Customers.Update(customer);
                }

                // Check disbursement record
                var disbursement = loan.Disbursements?.FirstOrDefault()
                    ?? await _context.LoanDisbursements.FirstOrDefaultAsync(d => d.LoanId == id);

                BankAccount? bank = null;
                if (disbursement != null)
                {
                    bank = await _context.BankAccounts.FindAsync(disbursement.BankAccountId);
                }

                if (bank == null && customer?.BankAccounts != null)
                {
                    bank = customer.BankAccounts.FirstOrDefault(b => b.IsPrimary) ?? customer.BankAccounts.FirstOrDefault();
                }

                if (bank != null)
                {
                    if (bank.VerificationStatus != "Verified")
                    {
                        bank.VerificationStatus = "Verified";
                        bank.UpdatedAtUtc = DateTime.UtcNow;
                        _context.BankAccounts.Update(bank);
                    }

                    string randomSuffix = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 999999).ToString();
                    string txRef = $"IMPS-DISB-{DateTime.UtcNow:yyyyMMdd}-{randomSuffix}";

                    if (disbursement == null)
                    {
                        disbursement = new LoanDisbursement
                        {
                            LoanId = loan.LoanId,
                            CustomerId = loan.CustomerId,
                            BankAccountId = bank.Id,
                            PrincipalAmount = loan.PrincipalAmount,
                            Deductions = 0m,
                            NetDisbursedAmount = loan.PrincipalAmount,
                            Status = "Completed",
                            IsSimulated = true,
                            PaymentMode = "IMPS / Instant Payout (Simulated)",
                            TransactionReference = txRef,
                            CreatedAtUtc = DateTime.UtcNow,
                            CompletedAtUtc = DateTime.UtcNow,
                            Remarks = $"Disbursed to {bank.BankName} ({bank.MaskedAccountNumber}) via simulated IMPS."
                        };
                        _context.LoanDisbursements.Add(disbursement);
                    }
                    else
                    {
                        disbursement.Status = "Completed";
                        disbursement.BankAccountId = bank.Id;
                        disbursement.CompletedAtUtc = DateTime.UtcNow;
                        disbursement.IsSimulated = true;
                        if (string.IsNullOrEmpty(disbursement.TransactionReference))
                        {
                            disbursement.TransactionReference = txRef;
                        }
                        disbursement.Remarks = $"Disbursed to {bank.BankName} ({bank.MaskedAccountNumber}) via simulated IMPS.";
                        _context.LoanDisbursements.Update(disbursement);
                    }
                }

                await _context.SaveChangesAsync();
                string bankInfo = bank != null ? $" Disbursed to {bank.BankName} ({bank.MaskedAccountNumber})." : "";
                TempData["Success"] = $"Loan Request #{loan.LoanId} approved successfully!{bankInfo}";
            }
            else
            {
                TempData["Info"] = $"Loan #{loan.LoanId} is already in '{loan.Status}' status.";
            }

            return RedirectToAction(returnUrl == "Requests" ? nameof(Requests) : nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id, string? reason = null, string? returnUrl = null)
        {
            var loan = await _context.Loans
                .Include(l => l.Disbursements)
                .FirstOrDefaultAsync(l => l.LoanId == id);

            if (loan == null)
            {
                TempData["Error"] = "Loan application not found.";
                return RedirectToAction(returnUrl == "Requests" ? nameof(Requests) : nameof(Index));
            }

            if (loan.Status == "Pending")
            {
                string cleanReason = string.IsNullOrWhiteSpace(reason)
                    ? "Application did not meet credit underwriting eligibility criteria."
                    : reason.Trim();

                loan.Status = "Rejected";
                loan.DisbursementStatus = "Rejected";

                var disbursement = loan.Disbursements?.FirstOrDefault()
                    ?? await _context.LoanDisbursements.FirstOrDefaultAsync(d => d.LoanId == id);

                if (disbursement != null)
                {
                    disbursement.Status = "Rejected";
                    disbursement.Remarks = cleanReason;
                    _context.LoanDisbursements.Update(disbursement);
                }
                else
                {
                    var primaryBank = await _context.BankAccounts
                        .Where(b => b.CustomerId == loan.CustomerId)
                        .OrderByDescending(b => b.IsPrimary)
                        .FirstOrDefaultAsync();

                    if (primaryBank != null)
                    {
                        var rejDisb = new LoanDisbursement
                        {
                            LoanId = loan.LoanId,
                            CustomerId = loan.CustomerId,
                            BankAccountId = primaryBank.Id,
                            TransactionReference = $"REJ-{loan.LoanId}-{DateTime.UtcNow.Ticks % 1000000}",
                            PrincipalAmount = loan.PrincipalAmount,
                            Deductions = 0m,
                            NetDisbursedAmount = 0m,
                            Status = "Rejected",
                            PaymentMode = "None",
                            Remarks = cleanReason,
                            CreatedAtUtc = DateTime.UtcNow
                        };
                        _context.LoanDisbursements.Add(rejDisb);
                    }
                }

                await _context.SaveChangesAsync();
                TempData["Success"] = $"Loan Request #{loan.LoanId} has been rejected. Reason logged: \"{cleanReason}\"";
            }
            else
            {
                TempData["Info"] = $"Loan #{loan.LoanId} is already in '{loan.Status}' status.";
            }

            return RedirectToAction(returnUrl == "Requests" ? nameof(Requests) : nameof(Index));
        }
    }
}