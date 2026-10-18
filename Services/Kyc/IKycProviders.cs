using System;
using System.Threading;
using System.Threading.Tasks;

namespace FinGuard.Services.Kyc
{
    public class AadhaarInitiateResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? TransactionReference { get; set; }
        public string? MaskedAadhaar { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public int CooldownSeconds { get; set; } = 60;
        public string? ErrorCode { get; set; }
        public bool IsSandbox { get; set; }
    }

    public class AadhaarOtpVerifyResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? TransactionReference { get; set; }
        public string? MaskedAadhaar { get; set; }
        public string? RegisteredName { get; set; }
        public DateTime? VerifiedAtUtc { get; set; }
        public string? ErrorCode { get; set; }
        public int RemainingAttempts { get; set; }
        public bool LockoutExceeded { get; set; }
    }

    public class PanVerificationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? TransactionReference { get; set; }
        public string? MaskedPan { get; set; }
        public string? RegisteredName { get; set; }
        public bool NameMatched { get; set; }
        public bool DobMatched { get; set; }
        public DateTime? VerifiedAtUtc { get; set; }
        public string? ErrorCode { get; set; }
        public bool IsSandbox { get; set; }
    }

    public interface IAadhaarVerificationProvider
    {
        string ProviderName { get; }
        bool IsSandbox { get; }
        Task<AadhaarInitiateResult> InitiateOtpAsync(string aadhaarNumber, string customerName, CancellationToken cancellationToken = default);
        Task<AadhaarOtpVerifyResult> VerifyOtpAsync(string transactionReference, string otp, CancellationToken cancellationToken = default);
    }

    public interface IPanVerificationProvider
    {
        string ProviderName { get; }
        bool IsSandbox { get; }
        Task<PanVerificationResult> VerifyPanAsync(string panNumber, string legalName, DateTime dateOfBirth, CancellationToken cancellationToken = default);
    }
}
