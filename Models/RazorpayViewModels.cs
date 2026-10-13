using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FinGuard.Models
{
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

        public string RazorpayKeyId { get; set; } = string.Empty;
        public string CompanyName { get; set; } = "FinGuard Microfinance";
        public string ThemeColor { get; set; } = "#4f46e5";
        public bool IsTestMode { get; set; } = true;
    }

    public class CreateOrderRequest
    {
        [Required]
        public int LoanId { get; set; }

        [Required]
        [Range(1, double.MaxValue, ErrorMessage = "Payment amount must be greater than 0.")]
        public decimal Amount { get; set; }

        public string PaymentType { get; set; } = "EMI"; // "EMI", "Foreclosure", "Custom"
    }

    public class CreateOrderResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? OrderId { get; set; }
        public long AmountInPaise { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "INR";
        public string KeyId { get; set; } = string.Empty;
        public string CompanyName { get; set; } = "FinGuard Microfinance";
        public string ThemeColor { get; set; } = "#4f46e5";
        public string BorrowerName { get; set; } = string.Empty;
        public string BorrowerEmail { get; set; } = string.Empty;
        public string BorrowerPhone { get; set; } = string.Empty;
        public bool IsTestMode { get; set; } = true;
    }

    public class PaymentVerificationRequest
    {
        [Required]
        public int LoanId { get; set; }

        [Required]
        public string RazorpayOrderId { get; set; } = string.Empty;

        [Required]
        public string RazorpayPaymentId { get; set; } = string.Empty;

        [Required]
        public string RazorpaySignature { get; set; } = string.Empty;

        public string? PaymentMethod { get; set; } // "UPI", "Card", "NetBanking", etc.
    }

    public class PaymentVerificationResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int RepaymentId { get; set; }
        public string? RedirectUrl { get; set; }
        public string? PaymentId { get; set; }
        public string? OrderId { get; set; }
        public decimal AmountPaid { get; set; }
        public decimal RemainingBalance { get; set; }
        public string LoanStatus { get; set; } = string.Empty;
    }
}
