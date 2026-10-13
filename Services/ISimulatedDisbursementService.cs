using System.Collections.Generic;
using System.Threading.Tasks;
using FinGuard.Models;

namespace FinGuard.Services
{
    public interface ISimulatedDisbursementService
    {
        Task<List<Loan>> GetEligibleLoansAsync(int customerId);
        Task<Loan?> GetEligibleLoanByIdAsync(int loanId, int customerId);
        Task<List<DisbursementHistoryItemViewModel>> GetDisbursementHistoryAsync(int customerId);
        (decimal Deductions, decimal NetPayout) CalculatePayoutBreakdown(decimal principalAmount);
        Task<(bool Success, string Message, LoanDisbursement? Disbursement)> ExecuteSimulatedDisbursementAsync(int customerId, int loanId, int bankAccountId);
    }
}
