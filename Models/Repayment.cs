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

        // Razorpay Test Mode Payment Details
        [MaxLength(100)]
        public string? RazorpayOrderId { get; set; }

        [MaxLength(100)]
        public string? RazorpayPaymentId { get; set; }

        [MaxLength(255)]
        public string? RazorpaySignature { get; set; }

        [Required]
        [MaxLength(30)]
        public string PaymentStatus { get; set; } = "Success"; // "Success", "Pending", "Failed"

        [MaxLength(50)]
        public string? PaymentMethod { get; set; } // "UPI", "Card", "NetBanking", "Cash"

        public bool IsTestMode { get; set; } = true;

        [MaxLength(255)]
        public string? FailureReason { get; set; }

        // Navigation property
        [ForeignKey("LoanId")]
        public virtual Loan? Loan { get; set; }
    }
}