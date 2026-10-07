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

            return View(activeLoans);
        }

        public async Task<IActionResult> Collect(int? loanId)
        {
            if (loanId == null) return NotFound();

            var loan = await _context.Loans.FindAsync(loanId);
            if (loan == null) return NotFound();

            var repayment = new Repayment
            {
                LoanId = loan.LoanId,
                AmountPaid = loan.EMIAmount
            };

            ViewBag.RemainingBalance = loan.RemainingBalance;
            ViewBag.Principal = loan.PrincipalAmount;
            return View(repayment);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Collect(Repayment repayment)
        {
            // Remove properties that are set on the server-side from validation
            ModelState.Remove("PaymentDate");
            ModelState.Remove("CollectedBy");
            ModelState.Remove("Loan"); // Sometimes navigation properties cause validation errors

            if (ModelState.IsValid)
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    var loan = await _context.Loans.FindAsync(repayment.LoanId);
                    if (loan == null) return NotFound();

                    // Enforce strict UTC time for PostgreSQL compatibility
                    repayment.PaymentDate = DateTime.UtcNow;

                    // Track which Admin collected the EMI
                    repayment.CollectedBy = User.Identity?.Name ?? "Admin";

                    _context.Repayments.Add(repayment);

                    // Deduct collected amount from the remaining loan balance
                    loan.RemainingBalance -= repayment.AmountPaid;

                    // Auto-Close Loan Logic
                    if (loan.RemainingBalance <= 0)
                    {
                        loan.RemainingBalance = 0;
                        loan.Status = "Closed";

                        var customer = await _context.Customers.FindAsync(loan.CustomerId);
                        if (customer != null)
                        {
                            customer.CreditStatus = "Pending";
                            _context.Update(customer);
                        }
                    }

                    _context.Update(loan);
                    await _context.SaveChangesAsync();
                    
                    await transaction.CommitAsync();

                    TempData["Success"] = $"EMI of ₹{repayment.AmountPaid} collected successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    ModelState.AddModelError(string.Empty, "An error occurred while processing the repayment.");
                }
            }

            var loanFallback = await _context.Loans.FindAsync(repayment.LoanId);
            if (loanFallback != null)
            {
                ViewBag.RemainingBalance = loanFallback.RemainingBalance;
                ViewBag.Principal = loanFallback.PrincipalAmount;
            }
            return View(repayment);
        }
    }
}