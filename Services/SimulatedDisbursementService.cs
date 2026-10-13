using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using FinGuard.Data;
using FinGuard.Models;

namespace FinGuard.Services
{
    public class SimulatedDisbursementService : ISimulatedDisbursementService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<SimulatedDisbursementService> _logger;

        public SimulatedDisbursementService(ApplicationDbContext context, ILogger<SimulatedDisbursementService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<List<Loan>> GetEligibleLoansAsync(int customerId)
        {
            // Eligible loans:
            // 1. Belong to customer
            // 2. Status is "Active" or "Approved"
            // 3. DisbursementStatus is not "Disbursed"
            // 4. No successful disbursement already recorded
            var loans = await _context.Loans
                .Where(l => l.CustomerId == customerId &&
                            (l.Status == "Active" || l.Status == "Approved") &&
                            l.DisbursementStatus != "Disbursed")
                .OrderByDescending(l => l.LoanId)
                .ToListAsync();

            var alreadyDisbursedLoanIds = await _context.LoanDisbursements
                .Where(d => d.CustomerId == customerId && d.Status == "Completed")
                .Select(d => d.LoanId)
                .ToListAsync();

            return loans.Where(l => !alreadyDisbursedLoanIds.Contains(l.LoanId)).ToList();
        }

        public async Task<Loan?> GetEligibleLoanByIdAsync(int loanId, int customerId)
        {
            var eligibleLoans = await GetEligibleLoansAsync(customerId);
            return eligibleLoans.FirstOrDefault(l => l.LoanId == loanId);
        }

        public async Task<List<DisbursementHistoryItemViewModel>> GetDisbursementHistoryAsync(int customerId)
        {
            var records = await _context.LoanDisbursements
                .Include(d => d.BankAccount)
                .Where(d => d.CustomerId == customerId)
                .OrderByDescending(d => d.CreatedAtUtc)
                .ToListAsync();

            return records.Select(d => new DisbursementHistoryItemViewModel
            {
                DisbursementId = d.Id,
                LoanId = d.LoanId,
                TransactionReference = d.TransactionReference,
                BankName = d.BankAccount?.BankName ?? "Verified Bank",
                MaskedAccountNumber = d.BankAccount?.MaskedAccountNumber ?? "•••• •••• ••••",
                IFSCCode = d.BankAccount?.IFSCCode ?? string.Empty,
                PrincipalAmount = d.PrincipalAmount,
                Deductions = d.Deductions,
                NetDisbursedAmount = d.NetDisbursedAmount,
                Status = d.Status,
                IsSimulated = d.IsSimulated,
                CreatedAtUtc = d.CreatedAtUtc,
                CompletedAtUtc = d.CompletedAtUtc
            }).ToList();
        }

        public (decimal Deductions, decimal NetPayout) CalculatePayoutBreakdown(decimal principalAmount)
        {
            // Standard institutional documentation fee: 1% capped at ₹1,000, min ₹250
            decimal rawFee = Math.Round(principalAmount * 0.01m, 2);
            decimal deductions = Math.Max(250m, Math.Min(1000m, rawFee));
            if (deductions > principalAmount) deductions = 0;

            decimal netPayout = Math.Max(0m, principalAmount - deductions);
            return (deductions, netPayout);
        }

        public async Task<(bool Success, string Message, LoanDisbursement? Disbursement)> ExecuteSimulatedDisbursementAsync(
            int customerId, int loanId, int bankAccountId)
        {
            // Begin atomic transaction to prevent race conditions & duplicate disbursements
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Validate loan ownership & eligibility
                var loan = await _context.Loans
                    .FirstOrDefaultAsync(l => l.LoanId == loanId && l.CustomerId == customerId);

                if (loan == null)
                {
                    return (false, "Loan not found or access denied.", null);
                }

                if (loan.Status != "Active" && loan.Status != "Approved")
                {
                    return (false, $"Loan #{loanId} is in '{loan.Status}' status and is not eligible for disbursement.", null);
                }

                if (loan.DisbursementStatus == "Disbursed")
                {
                    return (false, $"Loan #{loanId} has already been disbursed.", null);
                }

                // 2. Concurrency check: Ensure no completed disbursement exists for this loan
                bool alreadyDisbursed = await _context.LoanDisbursements
                    .AnyAsync(d => d.LoanId == loanId && d.Status == "Completed");

                if (alreadyDisbursed)
                {
                    return (false, $"A successful disbursement transaction already exists for Facility #{loanId}.", null);
                }

                // 3. Validate bank account ownership & verification status
                var bankAccount = await _context.BankAccounts
                    .FirstOrDefaultAsync(b => b.Id == bankAccountId && b.CustomerId == customerId);

                if (bankAccount == null)
                {
                    return (false, "Destination bank account not found or access denied.", null);
                }

                if (bankAccount.VerificationStatus != "Verified")
                {
                    return (false, "Disbursement requires an eligible, verified bank account. Please complete account verification first.", null);
                }

                // 4. Calculate payout breakdown
                var (deductions, netPayout) = CalculatePayoutBreakdown(loan.PrincipalAmount);

                // 5. Generate unique transaction reference
                string randomSuffix = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
                string txRef = $"DISB-{DateTime.UtcNow:yyyyMMdd}-{randomSuffix}";

                // 6. Create simulated disbursement transaction record
                var disbursement = new LoanDisbursement
                {
                    LoanId = loan.LoanId,
                    CustomerId = customerId,
                    BankAccountId = bankAccount.Id,
                    TransactionReference = txRef,
                    PrincipalAmount = loan.PrincipalAmount,
                    Deductions = deductions,
                    NetDisbursedAmount = netPayout,
                    Status = "Completed",
                    IsSimulated = true,
                    PaymentMode = "IMPS / Instant Payout (Simulated)",
                    CreatedAtUtc = DateTime.UtcNow,
                    CompletedAtUtc = DateTime.UtcNow,
                    Remarks = "Demo Transaction — No real money transferred."
                };

                _context.LoanDisbursements.Add(disbursement);

                // 7. Update loan status atomically
                loan.DisbursementStatus = "Disbursed";
                if (loan.Status == "Approved")
                {
                    loan.Status = "Active";
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation(
                    "Simulated loan disbursement {TxRef} of {Amount} completed for Customer {CustomerId}, Loan {LoanId}.",
                    txRef, netPayout, customerId, loanId);

                return (true, "Simulated disbursement completed. Demo Transaction — No real money transferred.", disbursement);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error processing simulated disbursement for Customer {CustomerId}, Loan {LoanId}.", customerId, loanId);
                return (false, "An error occurred during simulated disbursement processing. Please try again.", null);
            }
        }
    }
}
