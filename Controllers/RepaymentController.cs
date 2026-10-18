using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinGuard.Data;
using FinGuard.Models;
using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Authorization;

namespace FinGuard.Controllers
{
    [Authorize]
    public class RepaymentController : Controller
    {
        private readonly ApplicationDbContext _context;

        public RepaymentController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var activeLoans = await _context.Loans
                .Where(l => l.Status == "Active")
                .ToListAsync();

            var customerIds = activeLoans.Select(l => l.CustomerId).Distinct().ToList();
            var customerNames = await _context.Customers
                .Where(c => customerIds.Contains(c.CustomerId))
                .ToDictionaryAsync(c => c.CustomerId, c => c.Name);

            ViewBag.CustomerNames = customerNames;

            return View(activeLoans);
        }

        public async Task<IActionResult> Collect(int? loanId)
        {
            if (loanId == null) return NotFound();

            var loan = await _context.Loans.FindAsync(loanId);
            if (loan == null) return NotFound();

            var customer = await _context.Customers.FindAsync(loan.CustomerId);

            decimal maxAllowed = Math.Min(loan.EMIAmount, loan.RemainingBalance);
            var repayment = new Repayment
            {
                LoanId = loan.LoanId,
                AmountPaid = maxAllowed
            };

            ViewBag.Customer = customer;
            ViewBag.EMIAmount = loan.EMIAmount;
            ViewBag.RemainingBalance = loan.RemainingBalance;
            ViewBag.Principal = loan.PrincipalAmount;
            ViewBag.MaxCollectable = maxAllowed;
            return View(repayment);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Collect(Repayment repayment)
        {
            // Remove properties that are set on the server-side from validation
            ModelState.Remove("PaymentDate");
            ModelState.Remove("CollectedBy");
            ModelState.Remove("PaymentStatus");
            ModelState.Remove("Loan");

            var loan = await _context.Loans.FindAsync(repayment.LoanId);
            if (loan == null) return NotFound();

            // Strict validation: Amount collected cannot exceed the scheduled monthly EMI or remaining balance
            if (repayment.AmountPaid <= 0)
            {
                ModelState.AddModelError("AmountPaid", "Collection amount must be greater than zero.");
            }
            else if (repayment.AmountPaid > loan.EMIAmount)
            {
                ModelState.AddModelError("AmountPaid", $"Collection amount cannot exceed the scheduled monthly installment EMI of ₹{loan.EMIAmount:N2}.");
            }
            else if (repayment.AmountPaid > loan.RemainingBalance)
            {
                ModelState.AddModelError("AmountPaid", $"Collection amount cannot exceed the current outstanding balance of ₹{loan.RemainingBalance:N2}.");
            }

            if (ModelState.IsValid)
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    // Enforce strict UTC time for PostgreSQL compatibility
                    repayment.PaymentDate = DateTime.UtcNow;

                    // Track which Admin collected the EMI
                    repayment.CollectedBy = User.Identity?.Name ?? "Operations Officer";
                    repayment.PaymentStatus = "Success";
                    repayment.PaymentMethod = "Field Collection";
                    repayment.IsTestMode = false;
                    repayment.TransactionReference = $"COL_{DateTime.UtcNow:yyyyMMddHHmmss}_{loan.LoanId}";
                    repayment.BankReference = $"CASH_{DateTime.UtcNow:yyyyMMdd}_{loan.LoanId}";

                    _context.Repayments.Add(repayment);

                    // Deduct collected amount from the remaining loan balance
                    loan.RemainingBalance -= repayment.AmountPaid;
                    loan.NextDueDate = loan.NextDueDate.AddMonths(1);

                    // Auto-Close Loan Logic
                    if (loan.RemainingBalance <= 0)
                    {
                        loan.RemainingBalance = 0;
                        loan.Status = "Closed";

                        var customer = await _context.Customers.FindAsync(loan.CustomerId);
                        if (customer != null)
                        {
                            customer.CreditStatus = "Eligible";
                            _context.Update(customer);
                        }
                    }

                    _context.Update(loan);
                    await _context.SaveChangesAsync();
                    
                    await transaction.CommitAsync();

                    TempData["Success"] = $"EMI installment of ₹{repayment.AmountPaid:N2} collected and credited successfully for Loan #{loan.LoanId}!";
                    return RedirectToAction(nameof(Receipt), new { id = repayment.RepaymentId });
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    ModelState.AddModelError(string.Empty, "An error occurred while processing the repayment collection.");
                }
            }

            var customerFallback = await _context.Customers.FindAsync(loan.CustomerId);
            ViewBag.Customer = customerFallback;
            ViewBag.EMIAmount = loan.EMIAmount;
            ViewBag.RemainingBalance = loan.RemainingBalance;
            ViewBag.Principal = loan.PrincipalAmount;
            ViewBag.MaxCollectable = Math.Min(loan.EMIAmount, loan.RemainingBalance);
            return View(repayment);
        }

        [HttpGet]
        public async Task<IActionResult> Receipt(int id)
        {
            var repayment = await _context.Repayments
                .Include(r => r.Loan)
                .FirstOrDefaultAsync(r => r.RepaymentId == id);

            if (repayment == null) return NotFound();

            var customer = await _context.Customers.FindAsync(repayment.Loan?.CustomerId);
            ViewBag.Customer = customer;

            return View(repayment);
        }

        public async Task<IActionResult> History(string? search, string? channel)
        {
            var query = _context.Repayments
                .Include(r => r.Loan)
                .OrderByDescending(r => r.PaymentDate)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                string s = search.Trim().ToLower();
                var matchingCustomerIds = await _context.Customers
                    .Where(c => c.Name.ToLower().Contains(s) || c.CustomerId.ToString().Contains(s))
                    .Select(c => c.CustomerId)
                    .ToListAsync();

                query = query.Where(r =>
                    r.RepaymentId.ToString().Contains(s) ||
                    r.LoanId.ToString().Contains(s) ||
                    (r.Loan != null && matchingCustomerIds.Contains(r.Loan.CustomerId)) ||
                    r.CollectedBy.ToLower().Contains(s) ||
                    (r.PaymentMethod != null && r.PaymentMethod.ToLower().Contains(s)) ||
                    (r.TransactionReference != null && r.TransactionReference.ToLower().Contains(s)) ||
                    (r.BankReference != null && r.BankReference.ToLower().Contains(s)));
            }

            if (!string.IsNullOrWhiteSpace(channel) && channel != "All")
            {
                query = query.Where(r => 
                    (r.PaymentMethod != null && r.PaymentMethod.ToLower() == channel.ToLower()) ||
                    r.CollectedBy.ToLower().Contains(channel.ToLower()));
            }

            var repayments = await query.ToListAsync();

            var customerIds = repayments
                .Where(r => r.Loan != null)
                .Select(r => r.Loan!.CustomerId)
                .Distinct()
                .ToList();

            var customers = await _context.Customers
                .Where(c => customerIds.Contains(c.CustomerId))
                .ToDictionaryAsync(c => c.CustomerId, c => c);

            ViewBag.Customers = customers;
            ViewBag.Search = search;
            ViewBag.Channel = channel;
            ViewBag.TotalCollected = repayments.Sum(r => r.AmountPaid);
            ViewBag.TotalCount = repayments.Count;

            return View(repayments);
        }
    }
}