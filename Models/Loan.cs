using System.ComponentModel.DataAnnotations;

namespace FinGuard.Models
{
    public class Loan
    {
        [Key]
        public int LoanId { get; set; }

        [Required]
        public int CustomerId { get; set; }

        [Required]
        public decimal PrincipalAmount { get; set; }

        [Required]
        public double InterestRate { get; set; }

        [Required]
        public int TenureMonths { get; set; }

        public decimal EMIAmount { get; set; }

        public decimal RemainingBalance { get; set; }

        public DateTime NextDueDate { get; set; }

        public string Status { get; set; } = "Pending";
    }
}