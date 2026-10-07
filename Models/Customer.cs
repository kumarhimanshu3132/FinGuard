using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FinGuard.Models
{
    public class MinimumAgeAttribute : ValidationAttribute
    {
        private readonly int _minimumAge;

        public MinimumAgeAttribute(int minimumAge)
        {
            _minimumAge = minimumAge;
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is DateTime dateOfBirth)
            {
                if (dateOfBirth.AddYears(_minimumAge) > DateTime.Today)
                {
                    return new ValidationResult(ErrorMessage ?? $"Customer must be at least {_minimumAge} years old to onboard.");
                }
                return ValidationResult.Success;
            }
            return new ValidationResult("Invalid Date of Birth.");
        }
    }

    public class Customer
    {
        [Key]
        [Required(ErrorMessage = "Please assign a Customer ID.")]
        [Range(1, int.MaxValue, ErrorMessage = "Customer ID must be a valid positive number.")]
        public int CustomerId { get; set; }

        [Required(ErrorMessage = "Please enter the full name.")]
        [RegularExpression(@"^[a-zA-Z]+( [a-zA-Z]+)*$", ErrorMessage = "Name can only contain letters and a single space between words.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter an email address.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Country Code is required.")]
        public string CountryCode { get; set; } = "+91";

        [Required(ErrorMessage = "Please enter a phone number.")]
        [RegularExpression(@"^[6789]\d{9}$", ErrorMessage = "Phone number must be exactly 10 digits and start with 6, 7, 8, or 9.")]
        public string Phone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please select a date of birth.")]
        [MinimumAge(18, ErrorMessage = "Customer must be at least 18 years old to be eligible for a loan.")]
        public DateTime DateOfBirth { get; set; }

        [Required(ErrorMessage = "Please enter the monthly income.")]
        [Range(1, double.MaxValue, ErrorMessage = "Income must be greater than zero.")]
        public decimal MonthlyIncome { get; set; }

        [Required(ErrorMessage = "Please enter the complete residential address.")]
        public string Address { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter the PIN code.")]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "Please enter a valid 6-digit PIN code.")]
        public string PinCode { get; set; } = string.Empty;

        public string? CreditStatus { get; set; }

        public string? EncryptedPan { get; set; }
        
        public string? EncryptedIdDocument { get; set; }

        public string KycStatus { get; set; } = "Pending";

        public virtual ICollection<Loan>? Loans { get; set; }

        public bool IsEligibleForLoan 
        { 
            get 
            {
                bool isVerified = KycStatus == "Verified";
                bool noActiveLoans = Loans == null || !System.Linq.Enumerable.Any(Loans, l => l.Status == "Active");
                return isVerified && noActiveLoans;
            }
        }
    }
}