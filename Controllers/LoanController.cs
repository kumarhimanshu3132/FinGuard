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
            var loans = await _context.Loans.ToListAsync();
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
    }
}