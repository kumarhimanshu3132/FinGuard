using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinGuard.Models
{
    public static class KycStageStatus
    {
        public const string NotStarted = "Not Started";
        public const string Pending = "Pending";
        public const string InProgress = "Verification in Progress";
        public const string Verified = "Verified";
        public const string Rejected = "Rejected";
        public const string ActionRequired = "Action Required";
    }

    public static class OverallKycStatusConstants
    {
        public const string Incomplete = "Incomplete";
        public const string Pending = "Pending";
        public const string Verified = "Verified";
        public const string Rejected = "Rejected";
        public const string ActionRequired = "Action Required";
    }

    public class KycVerificationRecord
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int CustomerId { get; set; }

        [ForeignKey("CustomerId")]
        public virtual Customer Customer { get; set; } = null!;

        // ---------------------------------------------
        // Stage 1: Aadhaar Verification
        // ---------------------------------------------
        [Required]
        [MaxLength(40)]
        public string AadhaarStatus { get; set; } = KycStageStatus.NotStarted;

        [MaxLength(24)]
        public string? MaskedAadhaar { get; set; }

        [MaxLength(120)]
        public string? AadhaarTransactionRef { get; set; }

        public DateTime? AadhaarVerifiedAtUtc { get; set; }

        [MaxLength(255)]
        public string? AadhaarFailureReason { get; set; }

        // ---------------------------------------------
        // Stage 2: PAN Card Verification
        // ---------------------------------------------
        [Required]
        [MaxLength(40)]
        public string PanStatus { get; set; } = KycStageStatus.NotStarted;

        [MaxLength(20)]
        public string? MaskedPan { get; set; }

        [MaxLength(120)]
        public string? PanTransactionRef { get; set; }

        [MaxLength(150)]
        public string? PanHolderName { get; set; }

        public DateTime? PanVerifiedAtUtc { get; set; }

        [MaxLength(255)]
        public string? PanFailureReason { get; set; }

        // ---------------------------------------------
        // Stage 3: OTP Verification Challenge
        // ---------------------------------------------
        [Required]
        [MaxLength(40)]
        public string OtpStatus { get; set; } = KycStageStatus.NotStarted;

        [MaxLength(50)]
        public string? ActiveOtpChallengeType { get; set; } // "AadhaarOtp" or "ContactOtp"

        [MaxLength(120)]
        public string? OtpTransactionRef { get; set; }

        public DateTime? OtpExpiresAtUtc { get; set; }

        public int OtpAttemptsCount { get; set; } = 0;

        public int MaxOtpAttempts { get; set; } = 3;

        public DateTime? LastOtpSentAtUtc { get; set; }

        public DateTime? OtpVerifiedAtUtc { get; set; }

        [MaxLength(255)]
        public string? OtpFailureReason { get; set; }

        // ---------------------------------------------
        // Stage 4: Overall KYC Status & Approval
        // ---------------------------------------------
        [Required]
        [MaxLength(40)]
        public string OverallKycStatus { get; set; } = OverallKycStatusConstants.Incomplete;

        public DateTime? CompletedAtUtc { get; set; }

        // Provider Mode Tracking (Audit whether completed via Sandbox or Production)
        [MaxLength(30)]
        public string VerificationMode { get; set; } = "Sandbox";

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public bool IsAadhaarVerified => AadhaarStatus == KycStageStatus.Verified;
        public bool IsPanVerified => PanStatus == KycStageStatus.Verified;
        public bool IsOtpVerified => OtpStatus == KycStageStatus.Verified;
        public bool IsOverallVerified => OverallKycStatus == OverallKycStatusConstants.Verified;
    }

    public class KycAuditLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int CustomerId { get; set; }

        [Required]
        [MaxLength(80)]
        public string Action { get; set; } = string.Empty;

        [Required]
        [MaxLength(40)]
        public string Stage { get; set; } = string.Empty; // "Aadhaar", "PAN", "OTP", "Approval"

        [Required]
        [MaxLength(40)]
        public string Status { get; set; } = string.Empty;

        [MaxLength(120)]
        public string? TransactionReference { get; set; }

        [MaxLength(500)]
        public string? Details { get; set; }

        [MaxLength(60)]
        public string? IpAddress { get; set; }

        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    }
}
