using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinGuard.Models
{
    public class LoanDisbursement
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int LoanId { get; set; }

        [Required]
        public int CustomerId { get; set; }

        [Required]
        public int BankAccountId { get; set; }

        [Required]
        [StringLength(60)]
        public string TransactionReference { get; set; } = string.Empty;

        [Column(TypeName = "numeric")]
        public decimal PrincipalAmount { get; set; }

        [Column(TypeName = "numeric")]
        public decimal Deductions { get; set; } = 0m;

        [Column(TypeName = "numeric")]
        public decimal NetDisbursedAmount { get; set; }

        [Required]
        [StringLength(30)]
        public string Status { get; set; } = "Completed"; // Completed, Pending, Failed

        public bool IsSimulated { get; set; } = true;

        [StringLength(50)]
        public string PaymentMode { get; set; } = "IMPS (Simulated)";

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAtUtc { get; set; }

        [StringLength(255)]
        public string? Remarks { get; set; }

        [ForeignKey("LoanId")]
        public virtual Loan? Loan { get; set; }

        [ForeignKey("CustomerId")]
        public virtual Customer? Customer { get; set; }

        [ForeignKey("BankAccountId")]
        public virtual BankAccount? BankAccount { get; set; }
    }
}
