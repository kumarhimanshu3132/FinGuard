using System.Collections.Generic;
using System.Threading.Tasks;
using FinGuard.Models;

namespace FinGuard.Services
{
    public interface IBankAccountService
    {
        Task<List<BankAccount>> GetCustomerAccountsAsync(int customerId);
        Task<BankAccount?> GetAccountByIdAsync(int id, int customerId);
        Task<(bool Success, string Message, BankAccount? Account)> CreateAccountAsync(int customerId, BankAccountCreateViewModel model);
        Task<(bool Success, string Message)> UpdateAccountAsync(int customerId, BankAccountEditViewModel model);
        Task<(bool Success, string Message)> DeleteAccountAsync(int customerId, int accountId);
        Task<(bool Success, string Message)> SetPrimaryAsync(int customerId, int accountId);
        Task<(bool Success, string Message)> SimulateVerificationAsync(int customerId, int accountId, string status = "Verified");
    }
}
