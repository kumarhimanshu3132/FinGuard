using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using FinGuard.Models;

namespace FinGuard.Models
{
    public class BorrowerLoginViewModel
    {
        [Required(ErrorMessage = "Please enter your registered email address.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your account password.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
        public string? ReturnUrl { get; set; }
    }

    public class BorrowerRegisterViewModel
    {
        [Required(ErrorMessage = "Please enter your legal full name.")]
        [RegularExpression(@"^[a-zA-Z]+( [a-zA-Z]+)*$", ErrorMessage = "Name can only contain letters and a single space between words.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your email address.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your 10-digit mobile number.")]
        [RegularExpression(@"^[6789]\d{9}$", ErrorMessage = "Phone number must be exactly 10 digits and start with 6, 7, 8, or 9.")]
        public string Phone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter a secure password.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters long.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please confirm your password.")]
        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please provide your Date of Birth.")]
        public DateTime DateOfBirth { get; set; }

        [Required(ErrorMessage = "Please enter your estimated monthly income.")]
        [Range(1000, 10000000, ErrorMessage = "Monthly income must be at least ₹1,000.")]
        public decimal MonthlyIncome { get; set; }

        [Required(ErrorMessage = "Please enter your residential address.")]
        public string Address { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your 6-digit PIN code.")]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "Please enter a valid 6-digit PIN code.")]
        public string PinCode { get; set; } = string.Empty;
    }

    public class BorrowerVerifyOtpViewModel
    {
        [Required(ErrorMessage = "Email address is required.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter the 6-digit verification code.")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Verification code must be exactly 6 digits.")]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "Verification code must consist of 6 numeric digits.")]
        public string Otp { get; set; } = string.Empty;
    }

    public class BorrowerDashboardViewModel
    {
        public Customer Customer { get; set; } = null!;
        public List<Loan> ActiveLoans { get; set; } = new();
        public List<Loan> PendingApplications { get; set; } = new();
        public List<Loan> ClosedLoans { get; set; } = new();
        public List<Repayment> RecentRepayments { get; set; } = new();

        public decimal TotalSanctionedPrincipal { get; set; }
        public decimal TotalOutstandingBalance { get; set; }
        public decimal TotalRepaidAmount { get; set; }
        public decimal NextEmiAmount { get; set; }
        public DateTime? NextEmiDueDate { get; set; }
        public int RepaymentProgressPercentage { get; set; }

        // Mobile Fintech Credit Limit & Real Amortization (mPokket-style)
        public decimal TotalApprovedCreditLimit { get; set; }
        public decimal AvailableCreditLimit { get; set; }
        public Loan? PrimaryLoan { get; set; }
        public decimal ForeclosureAmount { get; set; }
        public decimal ForeclosureSavings { get; set; }
    }

    public class LoanDetailsViewModel
    {
        public Loan Loan { get; set; } = null!;
        public Customer Customer { get; set; } = null!;
        public List<Repayment> Repayments { get; set; } = new();
        public List<EmiScheduleItemViewModel> Schedule { get; set; } = new();
        public decimal TotalPaidAmount { get; set; }
        public decimal ForeclosureAmount { get; set; }
        public decimal ForeclosureSavings { get; set; }
        public int PaidInstallmentsCount { get; set; }
        public int PendingInstallmentsCount { get; set; }
    }

    public class LoanApplicationViewModel
    {
        [Required(ErrorMessage = "Please specify the requested principal amount.")]
        [Range(5000, 200000, ErrorMessage = "Microfinance loan requests must be between ₹5,000 and ₹200,000.")]
        public decimal RequestedPrincipal { get; set; } = 30000;

        [Required(ErrorMessage = "Please choose a repayment tenure.")]
        [Range(3, 36, ErrorMessage = "Tenure must be between 3 and 36 months.")]
        public int TenureMonths { get; set; } = 12;

        [Required(ErrorMessage = "Please state the loan purpose.")]
        public string Purpose { get; set; } = "Small Business / Working Capital";

        [Required(ErrorMessage = "Please confirm your current monthly household income.")]
        [Range(1000, 1000000, ErrorMessage = "Please enter a valid monthly income.")]
        public decimal MonthlyHouseholdIncome { get; set; }

        public bool AgreeTerms { get; set; }
    }

    public class EmiScheduleItemViewModel
    {
        public int InstallmentNumber { get; set; }
        public DateTime DueDate { get; set; }
        public decimal EmiAmount { get; set; }
        public decimal PrincipalComponent { get; set; }
        public decimal InterestComponent { get; set; }
        public decimal RemainingBalanceAfter { get; set; }
        public string Status { get; set; } = "Upcoming"; // Paid, Due, Upcoming
    }

    public class BorrowerProfileUpdateViewModel
    {
        public int CustomerId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Residential address is required.")]
        public string Address { get; set; } = string.Empty;

        [Required(ErrorMessage = "PIN Code is required.")]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "Please enter a valid 6-digit PIN code.")]
        public string PinCode { get; set; } = string.Empty;
    }

    public class BorrowerChangePasswordViewModel
    {
        [Required(ErrorMessage = "Please enter your current password.")]
        [DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter a new password.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters long.")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please confirm your new password.")]
        [Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
        [DataType(DataType.Password)]
        public string ConfirmNewPassword { get; set; } = string.Empty;
    }

    public class BankAccountCreateViewModel
    {
        [Required(ErrorMessage = "Please enter the bank name.")]
        [StringLength(100, ErrorMessage = "Bank name cannot exceed 100 characters.")]
        public string BankName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter the account holder name.")]
        [RegularExpression(@"^[a-zA-Z]+( [a-zA-Z]+)*$", ErrorMessage = "Account holder name can only contain letters and a single space between words.")]
        [StringLength(150, ErrorMessage = "Name cannot exceed 150 characters.")]
        public string AccountHolderName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter the bank account number.")]
        [RegularExpression(@"^\d{9,18}$", ErrorMessage = "Account number must be between 9 and 18 numeric digits.")]
        public string AccountNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please re-enter the bank account number to confirm.")]
        [Compare("AccountNumber", ErrorMessage = "Account numbers do not match.")]
        public string ConfirmAccountNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter the 11-character IFSC code.")]
        [RegularExpression(@"^[A-Z]{4}0[A-Z0-9]{6}$", ErrorMessage = "Invalid IFSC format. Must be 11 uppercase characters (e.g. SBIN0001234 or HDFC0004567).")]
        public string IFSCCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please select account type.")]
        public string AccountType { get; set; } = "Savings";

        [StringLength(100, ErrorMessage = "Branch name cannot exceed 100 characters.")]
        public string? BranchName { get; set; }

        public bool SetAsPrimary { get; set; } = false;
    }

    public class BankAccountEditViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Please enter the bank name.")]
        [StringLength(100, ErrorMessage = "Bank name cannot exceed 100 characters.")]
        public string BankName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter the account holder name.")]
        [RegularExpression(@"^[a-zA-Z]+( [a-zA-Z]+)*$", ErrorMessage = "Account holder name can only contain letters and a single space between words.")]
        public string AccountHolderName { get; set; } = string.Empty;

        [RegularExpression(@"^\d{9,18}$", ErrorMessage = "Account number must be between 9 and 18 numeric digits.")]
        public string? AccountNumber { get; set; }

        [Compare("AccountNumber", ErrorMessage = "Account numbers do not match.")]
        public string? ConfirmAccountNumber { get; set; }

        public string CurrentMaskedNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter the 11-character IFSC code.")]
        [RegularExpression(@"^[A-Z]{4}0[A-Z0-9]{6}$", ErrorMessage = "Invalid IFSC format (e.g. SBIN0001234).")]
        public string IFSCCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please select account type.")]
        public string AccountType { get; set; } = "Savings";

        [StringLength(100)]
        public string? BranchName { get; set; }

        public string VerificationStatus { get; set; } = "Pending";
        public bool IsPrimary { get; set; } = false;
    }

    public class DisbursementInitiateViewModel
    {
        public int LoanId { get; set; }
        public Loan? Loan { get; set; }
        public decimal PrincipalAmount { get; set; }
        public decimal Deductions { get; set; }
        public decimal NetPayout { get; set; }
        public int SelectedBankAccountId { get; set; }
        public List<BankAccount> AvailableBankAccounts { get; set; } = new();
        public bool TermsConfirmed { get; set; }
    }

    public class DisbursementHistoryItemViewModel
    {
        public int DisbursementId { get; set; }
        public int LoanId { get; set; }
        public string TransactionReference { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public string MaskedAccountNumber { get; set; } = string.Empty;
        public string IFSCCode { get; set; } = string.Empty;
        public decimal PrincipalAmount { get; set; }
        public decimal Deductions { get; set; }
        public decimal NetDisbursedAmount { get; set; }
        public string Status { get; set; } = "Completed";
        public bool IsSimulated { get; set; } = true;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
    }
}
