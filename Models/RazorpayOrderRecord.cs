using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinGuard.Models
{
    public class RazorpayOrderRecord
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int CustomerId { get; set; }

        [Required]
        public int LoanId { get; set; }

        [Required]
        [MaxLength(100)]
        public string RazorpayOrderId { get; set; } = string.Empty;

        /// <summary>
        /// Order amount in paise (1 INR = 100 paise)
        /// </summary>
        public long AmountInPaise { get; set; }

        /// <summary>
        /// Human-readable amount in INR
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [Required]
        [MaxLength(10)]
        public string Currency { get; set; } = "INR";

        [MaxLength(100)]
        public string Receipt { get; set; } = string.Empty;

        /// <summary>
        /// Status: Created, Paid, Failed, Expired
        /// </summary>
        [Required]
        [MaxLength(30)]
        public string Status { get; set; } = "Created";

        public bool IsTestMode { get; set; } = true;

        [MaxLength(100)]
        public string? RazorpayPaymentId { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAtUtc { get; set; }

        // Navigation properties
        [ForeignKey("CustomerId")]
        public virtual Customer? Customer { get; set; }

        [ForeignKey("LoanId")]
        public virtual Loan? Loan { get; set; }
    }
}
