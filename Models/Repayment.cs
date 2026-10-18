using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinGuard.Models
{
    public class Repayment
    {
        [Key]
        public int RepaymentId { get; set; }

        [Required]
        public int LoanId { get; set; }

        [Required(ErrorMessage = "Please enter the EMI amount to collect.")]
        [Range(1, double.MaxValue, ErrorMessage = "Amount must be greater than 0.")]
        [Column(TypeName = "decimal(18,2)")]
        public decimal AmountPaid { get; set; }

        public DateTime PaymentDate { get; set; }

        [Required]
        [MaxLength(100)]
        public string CollectedBy { get; set; } = string.Empty;

        // Banking Payment & Ledger Identifiers
        [MaxLength(100)]
        public string? TransactionReference { get; set; }

        [MaxLength(100)]
        public string? BankReference { get; set; }

        [Required]
        [MaxLength(30)]
        public string PaymentStatus { get; set; } = "Success"; // "Success", "Pending", "Failed"

        [MaxLength(50)]
        public string? PaymentMethod { get; set; } // "UPI", "Card", "NetBanking", "Cash", "Field Collection"

        public bool IsTestMode { get; set; } = false;

        [MaxLength(255)]
        public string? FailureReason { get; set; }

        // Navigation property
        [ForeignKey("LoanId")]
        public virtual Loan? Loan { get; set; }
    }
}