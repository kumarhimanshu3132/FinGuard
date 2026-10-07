using System;
using System.ComponentModel.DataAnnotations;

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
        public decimal AmountPaid { get; set; }

        public DateTime PaymentDate { get; set; }

        [Required]
        public string CollectedBy { get; set; } = string.Empty;
    }
}