using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FinGuard.Models
{
    public class KycSettings
    {
        public const string SectionName = "Kyc";

        /// <summary>
        /// "Sandbox" for development/mock mode; "Production" for live authorized provider.
        /// </summary>
        public string ProviderMode { get; set; } = "Sandbox";

        /// <summary>
        /// Base URL for the authorized verification provider API (e.g., https://api.surepass.io or https://sandbox.cashfree.com).
        /// </summary>
        public string ProviderBaseUrl { get; set; } = "https://api.sandbox.surepass.io";

        /// <summary>
        /// API Token / Key for the authorized provider.
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Client secret if required by provider.
        /// </summary>
        public string ApiSecret { get; set; } = string.Empty;

        /// <summary>
        /// Cooldown time in seconds between OTP requests.
        /// </summary>
        public int OtpResendCooldownSeconds { get; set; } = 60;

        /// <summary>
        /// OTP validity window in minutes.
        /// </summary>
        public int OtpExpiryMinutes { get; set; } = 10;

        /// <summary>
        /// Maximum allowed failed OTP attempts before invalidation.
        /// </summary>
        public int MaxOtpAttempts { get; set; } = 3;

        public bool IsSandbox => string.Equals(ProviderMode, "Sandbox", StringComparison.OrdinalIgnoreCase);
    }

    public class KycDashboardViewModel
    {
        public Customer Customer { get; set; } = null!;
        public KycVerificationRecord Record { get; set; } = null!;
        public List<KycAuditLog> RecentAuditLogs { get; set; } = new();

        public string AadhaarStatus => Record.AadhaarStatus;
        public string PanStatus => Record.PanStatus;
        public string OtpStatus => Record.OtpStatus;
        public string OverallKycStatus => Record.OverallKycStatus;

        public bool IsAadhaarVerified => Record.IsAadhaarVerified;
        public bool IsPanVerified => Record.IsPanVerified;
        public bool IsOtpVerified => Record.IsOtpVerified;
        public bool IsOverallVerified => Record.IsOverallVerified;

        public string? MaskedAadhaar => Record.MaskedAadhaar;
        public string? MaskedPan => Record.MaskedPan;

        public bool IsSandboxMode { get; set; } = true;
        public int ProgressPercentage { get; set; }

        public int RemainingOtpAttempts => Math.Max(0, Record.MaxOtpAttempts - Record.OtpAttemptsCount);
        public bool CanResendOtp { get; set; }
        public int CooldownRemainingSeconds { get; set; }
    }

    public class AadhaarInitiateRequest
    {
        [Required(ErrorMessage = "Please enter a valid 12-digit Aadhaar number.")]
        [RegularExpression(@"^[\d\s\-]{12,16}$", ErrorMessage = "Please enter a valid 12-digit Aadhaar number.")]
        public string AadhaarNumber { get; set; } = string.Empty;

        [Range(typeof(bool), "true", "true", ErrorMessage = "Explicit consent under UIDAI regulations and DPDP Act is required.")]
        public bool ConsentGiven { get; set; }
    }

    public class AadhaarInitiateResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? TransactionReference { get; set; }
        public string? MaskedAadhaar { get; set; }
        public int CooldownSeconds { get; set; } = 60;
        public int ExpiryMinutes { get; set; } = 10;
        public string? ErrorCode { get; set; }
        public bool IsSandbox { get; set; }
    }

    public class OtpVerifyRequest
    {
        [Required(ErrorMessage = "Transaction reference is required.")]
        public string TransactionReference { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter the 6-digit OTP code.")]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "Verification OTP must consist of exactly 6 numeric digits.")]
        public string Otp { get; set; } = string.Empty;
    }

    public class OtpVerifyResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? MaskedAadhaar { get; set; }
        public string AadhaarStatus { get; set; } = KycStageStatus.NotStarted;
        public string OtpStatus { get; set; } = KycStageStatus.NotStarted;
        public string OverallStatus { get; set; } = OverallKycStatusConstants.Incomplete;
        public int RemainingAttempts { get; set; }
        public bool LockoutExceeded { get; set; }
        public string? ErrorCode { get; set; }
    }

    public class PanVerifyRequest
    {
        [Required(ErrorMessage = "PAN number is required.")]
        [RegularExpression(@"^[A-Z]{5}[0-9]{4}[A-Z]{1}$", ErrorMessage = "Invalid PAN format. Must be 10 characters (e.g. ABCDE1234F).")]
        public string PanNumber { get; set; } = string.Empty;

        [Range(typeof(bool), "true", "true", ErrorMessage = "Consent for Income Tax PAN database verification is required.")]
        public bool ConsentGiven { get; set; }
    }

    public class PanVerifyResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? MaskedPan { get; set; }
        public string? PanHolderName { get; set; }
        public string PanStatus { get; set; } = KycStageStatus.NotStarted;
        public string OverallStatus { get; set; } = OverallKycStatusConstants.Incomplete;
        public bool NameMatched { get; set; }
        public bool DobMatched { get; set; }
        public string? ErrorCode { get; set; }
    }

    public class KycStatusResponse
    {
        public bool Success { get; set; }
        public string AadhaarStatus { get; set; } = KycStageStatus.NotStarted;
        public string? MaskedAadhaar { get; set; }
        public DateTime? AadhaarVerifiedAtUtc { get; set; }

        public string PanStatus { get; set; } = KycStageStatus.NotStarted;
        public string? MaskedPan { get; set; }
        public DateTime? PanVerifiedAtUtc { get; set; }

        public string OtpStatus { get; set; } = KycStageStatus.NotStarted;
        public DateTime? OtpVerifiedAtUtc { get; set; }

        public string OverallKycStatus { get; set; } = OverallKycStatusConstants.Incomplete;
        public int ProgressPercentage { get; set; }
        public bool IsOverallVerified { get; set; }
        public bool IsSandboxMode { get; set; }
        public string? Message { get; set; }
    }
}
