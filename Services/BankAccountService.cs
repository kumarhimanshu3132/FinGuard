using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using FinGuard.Data;
using FinGuard.Models;
using FinGuard.Utilities;

namespace FinGuard.Services
{
    public class BankAccountService : IBankAccountService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<BankAccountService> _logger;

        private static readonly Regex IfscRegex = new Regex(@"^[A-Z]{4}0[A-Z0-9]{6}$", RegexOptions.Compiled);
        private static readonly Regex AccountNumberRegex = new Regex(@"^\d{9,18}$", RegexOptions.Compiled);

        public BankAccountService(ApplicationDbContext context, ILogger<BankAccountService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<List<BankAccount>> GetCustomerAccountsAsync(int customerId)
        {
            return await _context.BankAccounts
                .Where(b => b.CustomerId == customerId)
                .OrderByDescending(b => b.IsPrimary)
                .ThenByDescending(b => b.CreatedAtUtc)
                .ToListAsync();
        }

        public async Task<BankAccount?> GetAccountByIdAsync(int id, int customerId)
        {
            return await _context.BankAccounts
                .FirstOrDefaultAsync(b => b.Id == id && b.CustomerId == customerId);
        }

        public async Task<(bool Success, string Message, BankAccount? Account)> CreateAccountAsync(int customerId, BankAccountCreateViewModel model)
        {
            if (model == null) return (false, "Invalid bank account details.", null);

            string ifsc = (model.IFSCCode ?? string.Empty).Trim().ToUpperInvariant();
            if (!IfscRegex.IsMatch(ifsc))
            {
                return (false, "Invalid IFSC format. Must be 11 uppercase characters (e.g. SBIN0001234).", null);
            }

            string cleanAccount = (model.AccountNumber ?? string.Empty).Trim();
            if (!AccountNumberRegex.IsMatch(cleanAccount))
            {
                return (false, "Account number must be between 9 and 18 numeric digits.", null);
            }

            if (cleanAccount != (model.ConfirmAccountNumber ?? string.Empty).Trim())
            {
                return (false, "Account numbers do not match.", null);
            }

            // Check if account already exists for this customer (comparing last 4 digits & bank)
            string last4 = cleanAccount.Substring(cleanAccount.Length - 4);
            string masked = $"•••• •••• {last4}";

            bool customerHasAccounts = await _context.BankAccounts.AnyAsync(b => b.CustomerId == customerId);

            // Determine if new account should be primary
            bool shouldBePrimary = !customerHasAccounts || model.SetAsPrimary;

            if (shouldBePrimary && customerHasAccounts)
            {
                // Unset existing primary accounts for this customer
                var existingPrimary = await _context.BankAccounts
                    .Where(b => b.CustomerId == customerId && b.IsPrimary)
                    .ToListAsync();

                foreach (var acc in existingPrimary)
                {
                    acc.IsPrimary = false;
                    acc.UpdatedAtUtc = DateTime.UtcNow;
                }
            }

            // Secure AES-256 encryption
            string encryptedNumber = EncryptionHelper.Encrypt(cleanAccount);

            var bankAccount = new BankAccount
            {
                CustomerId = customerId,
                BankName = model.BankName.Trim(),
                AccountHolderName = model.AccountHolderName.Trim(),
                EncryptedAccountNumber = encryptedNumber,
                MaskedAccountNumber = masked,
                IFSCCode = ifsc,
                AccountType = model.AccountType == "Current" ? "Current" : "Savings",
                BranchName = string.IsNullOrWhiteSpace(model.BranchName) ? null : model.BranchName.Trim(),
                VerificationStatus = "Pending", // Must start as Pending
                IsPrimary = shouldBePrimary,
                CreatedAtUtc = DateTime.UtcNow
            };

            _context.BankAccounts.Add(bankAccount);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Bank account ID {AccountId} created for Customer {CustomerId} with status {Status}.",
                bankAccount.Id, customerId, bankAccount.VerificationStatus);

            return (true, "Bank account added successfully with Pending verification status.", bankAccount);
        }

        public async Task<(bool Success, string Message)> UpdateAccountAsync(int customerId, BankAccountEditViewModel model)
        {
            var account = await _context.BankAccounts
                .FirstOrDefaultAsync(b => b.Id == model.Id && b.CustomerId == customerId);

            if (account == null)
            {
                return (false, "Bank account record not found or access denied.");
            }

            string ifsc = (model.IFSCCode ?? string.Empty).Trim().ToUpperInvariant();
            if (!IfscRegex.IsMatch(ifsc))
            {
                return (false, "Invalid IFSC format. Must be 11 uppercase characters (e.g. SBIN0001234).");
            }

            bool ifscChanged = !string.Equals(account.IFSCCode, ifsc, StringComparison.OrdinalIgnoreCase);
            bool numberChanged = !string.IsNullOrWhiteSpace(model.AccountNumber);

            if (numberChanged)
            {
                string cleanNumber = model.AccountNumber!.Trim();
                if (!AccountNumberRegex.IsMatch(cleanNumber))
                {
                    return (false, "Account number must be between 9 and 18 numeric digits.");
                }

                if (cleanNumber != (model.ConfirmAccountNumber ?? string.Empty).Trim())
                {
                    return (false, "Account numbers do not match.");
                }

                string last4 = cleanNumber.Substring(cleanNumber.Length - 4);
                account.EncryptedAccountNumber = EncryptionHelper.Encrypt(cleanNumber);
                account.MaskedAccountNumber = $"•••• •••• {last4}";
            }

            account.BankName = model.BankName.Trim();
            account.AccountHolderName = model.AccountHolderName.Trim();
            account.IFSCCode = ifsc;
            account.AccountType = model.AccountType == "Current" ? "Current" : "Savings";
            account.BranchName = string.IsNullOrWhiteSpace(model.BranchName) ? null : model.BranchName.Trim();

            // If sensitive details (account number or IFSC) changed, reset verification
            if (numberChanged || ifscChanged)
            {
                account.VerificationStatus = "Pending";
            }

            account.UpdatedAtUtc = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            _logger.LogInformation("Bank account ID {AccountId} updated for Customer {CustomerId}.", account.Id, customerId);

            return (true, "Bank account details updated successfully.");
        }

        public async Task<(bool Success, string Message)> DeleteAccountAsync(int customerId, int accountId)
        {
            var account = await _context.BankAccounts
                .FirstOrDefaultAsync(b => b.Id == accountId && b.CustomerId == customerId);

            if (account == null)
            {
                return (false, "Bank account not found or access denied.");
            }

            // Rule: Do not remove an account referenced by an existing disbursement record
            bool isReferencedInDisbursement = await _context.LoanDisbursements
                .AnyAsync(d => d.BankAccountId == accountId);

            if (isReferencedInDisbursement)
            {
                return (false, "Cannot remove this bank account because it is referenced in an official loan disbursement record.");
            }

            // Rule: Do not remove primary account if other accounts exist without selecting another first
            int totalAccounts = await _context.BankAccounts.CountAsync(b => b.CustomerId == customerId);
            if (account.IsPrimary && totalAccounts > 1)
            {
                return (false, "This is your primary bank account. Please designate another account as primary before removing this one.");
            }

            _context.BankAccounts.Remove(account);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Bank account ID {AccountId} removed for Customer {CustomerId}.", accountId, customerId);
            return (true, "Bank account removed successfully.");
        }

        public async Task<(bool Success, string Message)> SetPrimaryAsync(int customerId, int accountId)
        {
            var target = await _context.BankAccounts
                .FirstOrDefaultAsync(b => b.Id == accountId && b.CustomerId == customerId);

            if (target == null)
            {
                return (false, "Bank account not found or access denied.");
            }

            var allAccounts = await _context.BankAccounts
                .Where(b => b.CustomerId == customerId)
                .ToListAsync();

            foreach (var acc in allAccounts)
            {
                acc.IsPrimary = (acc.Id == accountId);
                acc.UpdatedAtUtc = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Bank account ID {AccountId} set as primary for Customer {CustomerId}.", accountId, customerId);

            return (true, $"{target.BankName} ({target.MaskedAccountNumber}) is now your primary disbursement account.");
        }

        public async Task<(bool Success, string Message)> SimulateVerificationAsync(int customerId, int accountId, string status = "Verified")
        {
            var account = await _context.BankAccounts
                .FirstOrDefaultAsync(b => b.Id == accountId && b.CustomerId == customerId);

            if (account == null)
            {
                return (false, "Bank account not found or access denied.");
            }

            account.VerificationStatus = status;
            account.UpdatedAtUtc = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            _logger.LogInformation("Simulated verification of Bank Account ID {AccountId} to {Status}.", accountId, status);

            return (true, $"Bank account verified successfully (Simulated Sandbox).");
        }
    }
}
