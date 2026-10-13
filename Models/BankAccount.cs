using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinGuard.Models
{
    public class BankAccount
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int CustomerId { get; set; }

        [Required(ErrorMessage = "Please enter the bank name.")]
        [StringLength(100, ErrorMessage = "Bank name cannot exceed 100 characters.")]
        public string BankName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter the account holder name.")]
        [StringLength(150, ErrorMessage = "Account holder name cannot exceed 150 characters.")]
        public string AccountHolderName { get; set; } = string.Empty;

        [Required]
        public string EncryptedAccountNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(30)]
        public string MaskedAccountNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter a valid IFSC code.")]
        [RegularExpression(@"^[A-Z]{4}0[A-Z0-9]{6}$", ErrorMessage = "IFSC code must be 11 characters (e.g., SBIN0001234 or HDFC0004567).")]
        [StringLength(11, MinimumLength = 11)]
        public string IFSCCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please select the account type.")]
        [StringLength(20)]
        public string AccountType { get; set; } = "Savings"; // Savings or Current

        [StringLength(100)]
        public string? BranchName { get; set; }

        [Required]
        [StringLength(30)]
        public string VerificationStatus { get; set; } = "Pending"; // Pending, Verified, Rejected

        public bool IsPrimary { get; set; } = false;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAtUtc { get; set; }

        [ForeignKey("CustomerId")]
        public virtual Customer? Customer { get; set; }

        public virtual ICollection<LoanDisbursement>? Disbursements { get; set; }
    }
}
