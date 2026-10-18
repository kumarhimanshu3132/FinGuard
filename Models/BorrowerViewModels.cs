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
        [Required(ErrorMessage = "Please enter your full legal name.")]
        [RegularExpression(@"^[a-zA-Z\s\.\'-]{2,70}$", ErrorMessage = "Please enter a valid legal name.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your email address.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your 10-digit mobile number.")]
        [RegularExpression(@"^[6789]\d{9}$", ErrorMessage = "Please enter a valid 10-digit mobile number.")]
        public string Phone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please create a secure password.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters long.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please confirm your password.")]
        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your date of birth.")]
        public DateTime DateOfBirth { get; set; }

        [Required(ErrorMessage = "Please enter your estimated monthly income.")]
        [Range(1000, 10000000, ErrorMessage = "Monthly income must be at least ₹1,000.")]
        public decimal? MonthlyIncome { get; set; }

        [Required(ErrorMessage = "Please enter your residential address.")]
        public string Address { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your 6-digit postal PIN code.")]
        [RegularExpression(@"^[1-9]\d{5}$", ErrorMessage = "Please enter a valid 6-digit postal PIN code.")]
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
        public List<Loan> AllLoans { get; set; } = new();
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

        // College Project Workflow Elements
        public List<BankAccount> BankAccounts { get; set; } = new();
        public KycVerificationRecord? KycRecord { get; set; }
        public BankAccount? PrimaryBankAccount => BankAccounts.FirstOrDefault(b => b.IsPrimary) ?? BankAccounts.FirstOrDefault();
        public bool HasVerifiedBank => BankAccounts.Any(b => b.VerificationStatus == "Verified");
        public bool HasVerifiedPan => KycRecord?.PanStatus == KycStageStatus.Verified || !string.IsNullOrEmpty(Customer.EncryptedPan);
        public bool HasVerifiedAadhaar => KycRecord?.AadhaarStatus == KycStageStatus.Verified || !string.IsNullOrEmpty(Customer.EncryptedIdDocument);
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
        [Range(5000, 1000000, ErrorMessage = "Loan requests must be between ₹5,000 and ₹10,00,000 (Maximum ₹10 Lakhs).")]
        public decimal RequestedPrincipal { get; set; } = 50000;

        [Required(ErrorMessage = "Please choose a repayment tenure.")]
        [Range(3, 120, ErrorMessage = "Repayment tenure must be between 3 and 120 months (up to 10 years).")]
        public int TenureMonths { get; set; } = 12;

        [Required(ErrorMessage = "Please state the loan purpose.")]
        public string Purpose { get; set; } = "Education / Business Development";

        [Required(ErrorMessage = "Please confirm your current monthly household income.")]
        [Range(1000, 10000000, ErrorMessage = "Please enter a valid monthly income.")]
        public decimal MonthlyHouseholdIncome { get; set; }

        public int? BankAccountId { get; set; }

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

    public class SetNewPasswordViewModel
    {
        [Required(ErrorMessage = "Please enter your registered email address.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your new permanent password.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters long.")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please confirm your new password.")]
        [Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class BankAccountEligibilityDto
    {
        public BankAccount BankAccount { get; set; } = null!;
        public bool IsEligible { get; set; } = true;
        public string? IneligibilityReason { get; set; }
        public int? ActiveLoanId { get; set; }
        public decimal ActiveLoanPrincipal { get; set; }
        public decimal RepaidAmount { get; set; }
        public decimal RequiredRepayment { get; set; }
        public decimal RepaymentPercentage { get; set; }
    }

    public class PayEmiViewModel
    {
        public int LoanId { get; set; }
        public Loan Loan { get; set; } = null!;
        public Customer Customer { get; set; } = null!;
        public decimal EMIAmount { get; set; }
        public decimal RemainingBalance { get; set; }
        public decimal ForeclosureAmount { get; set; }
        public decimal ForeclosureSavings { get; set; }
        public DateTime NextDueDate { get; set; }
        public int PaidInstallmentsCount { get; set; }
        public int TotalInstallmentsCount { get; set; }
    }
}
