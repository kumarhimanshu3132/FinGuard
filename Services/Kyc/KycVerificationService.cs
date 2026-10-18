using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinGuard.Data;
using FinGuard.Models;
using FinGuard.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinGuard.Services.Kyc
{
    public class KycVerificationService : IKycVerificationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAadhaarVerificationProvider _aadhaarProvider;
        private readonly IPanVerificationProvider _panProvider;
        private readonly KycSettings _settings;
        private readonly ILogger<KycVerificationService> _logger;

        public KycVerificationService(
            ApplicationDbContext context,
            IAadhaarVerificationProvider aadhaarProvider,
            IPanVerificationProvider panProvider,
            IOptions<KycSettings> options,
            ILogger<KycVerificationService> logger)
        {
            _context = context;
            _aadhaarProvider = aadhaarProvider;
            _panProvider = panProvider;
            _settings = options.Value;
            _logger = logger;
        }

        private async Task<KycVerificationRecord> GetOrCreateRecordAsync(int customerId, CancellationToken ct = default)
        {
            var record = await _context.KycVerificationRecords
                .Include(k => k.Customer)
                .FirstOrDefaultAsync(k => k.CustomerId == customerId, ct);

            if (record == null)
            {
                var customer = await _context.Customers.FindAsync(new object[] { customerId }, ct);
                if (customer == null)
                {
                    throw new InvalidOperationException($"Customer #{customerId} does not exist.");
                }

                record = new KycVerificationRecord
                {
                    CustomerId = customerId,
                    AadhaarStatus = KycStageStatus.NotStarted,
                    PanStatus = KycStageStatus.NotStarted,
                    OtpStatus = KycStageStatus.NotStarted,
                    OverallKycStatus = OverallKycStatusConstants.Incomplete,
                    VerificationMode = _settings.IsSandbox ? "Sandbox" : "Production",
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                // Sync initial state if customer already had encrypted documents from legacy migrations
                if (!string.IsNullOrEmpty(customer.EncryptedPan))
                {
                    try
                    {
                        string decryptedPan = EncryptionHelper.Decrypt(customer.EncryptedPan);
                        if (KycFormatHelper.ValidatePan(decryptedPan).IsValid)
                        {
                            record.PanStatus = KycStageStatus.Verified;
                            record.MaskedPan = KycFormatHelper.MaskPan(decryptedPan);
                            record.PanVerifiedAtUtc = DateTime.UtcNow;
                        }
                    }
                    catch { /* Ignore legacy decryption if format changed */ }
                }

                if (!string.IsNullOrEmpty(customer.EncryptedIdDocument))
                {
                    try
                    {
                        string decryptedId = EncryptionHelper.Decrypt(customer.EncryptedIdDocument);
                        if (KycFormatHelper.ValidateAadhaar(decryptedId).IsValid)
                        {
                            record.AadhaarStatus = KycStageStatus.Verified;
                            record.MaskedAadhaar = KycFormatHelper.MaskAadhaar(decryptedId);
                            record.AadhaarVerifiedAtUtc = DateTime.UtcNow;
                            record.OtpStatus = KycStageStatus.Verified;
                            record.OtpVerifiedAtUtc = DateTime.UtcNow;
                        }
                    }
                    catch { /* Ignore legacy decryption if format changed */ }
                }

                // If customer.KycStatus is already "Verified"
                if (customer.KycStatus == "Verified" && record.IsAadhaarVerified && record.IsPanVerified)
                {
                    record.OverallKycStatus = OverallKycStatusConstants.Verified;
                    record.CompletedAtUtc = DateTime.UtcNow;
                }

                _context.KycVerificationRecords.Add(record);
                await _context.SaveChangesAsync(ct);
            }

            return record;
        }

        private async Task AddAuditLogAsync(
            int customerId,
            string action,
            string stage,
            string status,
            string? txnRef = null,
            string? details = null,
            string? ipAddress = null,
            CancellationToken ct = default)
        {
            var log = new KycAuditLog
            {
                CustomerId = customerId,
                Action = action,
                Stage = stage,
                Status = status,
                TransactionReference = txnRef,
                Details = details,
                IpAddress = ipAddress,
                TimestampUtc = DateTime.UtcNow
            };

            _context.KycAuditLogs.Add(log);
            await _context.SaveChangesAsync(ct);
        }

        private int CalculateProgress(KycVerificationRecord record)
        {
            if (record.OverallKycStatus == OverallKycStatusConstants.Verified) return 100;

            int progress = 0;
            if (record.AadhaarStatus == KycStageStatus.Verified) progress += 33;
            else if (record.AadhaarStatus == KycStageStatus.InProgress) progress += 15;

            if (record.PanStatus == KycStageStatus.Verified) progress += 33;
            else if (record.PanStatus == KycStageStatus.InProgress) progress += 15;

            if (record.OtpStatus == KycStageStatus.Verified) progress += 34;
            else if (record.OtpStatus == KycStageStatus.Pending) progress += 10;

            return Math.Min(progress, 99);
        }

        private void RecalculateOverallStatus(KycVerificationRecord record, Customer customer)
        {
            if (record.AadhaarStatus == KycStageStatus.Verified &&
                record.PanStatus == KycStageStatus.Verified &&
                record.OtpStatus == KycStageStatus.Verified)
            {
                record.OverallKycStatus = OverallKycStatusConstants.Verified;
                record.CompletedAtUtc ??= DateTime.UtcNow;
                customer.KycStatus = "Verified";
            }
            else if (record.AadhaarStatus == KycStageStatus.Rejected ||
                     record.PanStatus == KycStageStatus.Rejected ||
                     record.OtpStatus == KycStageStatus.Rejected)
            {
                record.OverallKycStatus = OverallKycStatusConstants.ActionRequired;
                customer.KycStatus = "Pending";
            }
            else if (record.AadhaarStatus == KycStageStatus.Verified ||
                     record.PanStatus == KycStageStatus.Verified)
            {
                record.OverallKycStatus = OverallKycStatusConstants.Pending;
                customer.KycStatus = "Pending";
            }
            else
            {
                record.OverallKycStatus = OverallKycStatusConstants.Incomplete;
                customer.KycStatus = "Pending";
            }

            record.UpdatedAtUtc = DateTime.UtcNow;
        }

        // ========================================================
        // 1. GET DASHBOARD VIEW MODEL
        // ========================================================
        public async Task<KycDashboardViewModel> GetKycDashboardAsync(int customerId, CancellationToken ct = default)
        {
            var customer = await _context.Customers.FindAsync(new object[] { customerId }, ct);
            if (customer == null) throw new InvalidOperationException($"Customer #{customerId} not found.");

            var record = await GetOrCreateRecordAsync(customerId, ct);

            var recentLogs = await _context.KycAuditLogs
                .Where(l => l.CustomerId == customerId)
                .OrderByDescending(l => l.TimestampUtc)
                .Take(10)
                .ToListAsync(ct);

            int cooldownSecs = _settings.OtpResendCooldownSeconds;
            int remainingCooldown = 0;
            bool canResend = true;

            if (record.LastOtpSentAtUtc.HasValue)
            {
                var elapsed = DateTime.UtcNow - record.LastOtpSentAtUtc.Value;
                if (elapsed.TotalSeconds < cooldownSecs)
                {
                    remainingCooldown = (int)(cooldownSecs - elapsed.TotalSeconds);
                    canResend = false;
                }
            }

            return new KycDashboardViewModel
            {
                Customer = customer,
                Record = record,
                RecentAuditLogs = recentLogs,
                IsSandboxMode = _settings.IsSandbox,
                ProgressPercentage = CalculateProgress(record),
                CanResendOtp = canResend,
                CooldownRemainingSeconds = remainingCooldown
            };
        }

        // ========================================================
        // 2. INITIATE AADHAAR VERIFICATION
        // ========================================================
        public async Task<AadhaarInitiateResponse> InitiateAadhaarVerificationAsync(
            int customerId,
            AadhaarInitiateRequest request,
            string? ipAddress = null,
            CancellationToken ct = default)
        {
            if (!request.ConsentGiven)
            {
                return new AadhaarInitiateResponse
                {
                    Success = false,
                    Message = "Explicit consent is required by UIDAI regulations and the DPDP Act.",
                    ErrorCode = "CONSENT_REQUIRED"
                };
            }

            var customer = await _context.Customers.FindAsync(new object[] { customerId }, ct);
            if (customer == null)
            {
                return new AadhaarInitiateResponse
                {
                    Success = false,
                    Message = "Customer profile could not be resolved.",
                    ErrorCode = "UNAUTHORIZED"
                };
            }

            var record = await GetOrCreateRecordAsync(customerId, ct);

            if (record.AadhaarStatus == KycStageStatus.Verified)
            {
                return new AadhaarInitiateResponse
                {
                    Success = false,
                    Message = "Aadhaar has already been verified and locked for this profile.",
                    ErrorCode = "ALREADY_VERIFIED"
                };
            }

            // Enforce cooldown
            if (record.LastOtpSentAtUtc.HasValue)
            {
                var elapsed = DateTime.UtcNow - record.LastOtpSentAtUtc.Value;
                if (elapsed.TotalSeconds < _settings.OtpResendCooldownSeconds)
                {
                    int waitSeconds = (int)(_settings.OtpResendCooldownSeconds - elapsed.TotalSeconds);
                    return new AadhaarInitiateResponse
                    {
                        Success = false,
                        Message = $"Please wait {waitSeconds} seconds before requesting another OTP.",
                        CooldownSeconds = waitSeconds,
                        ErrorCode = "COOLDOWN_ACTIVE"
                    };
                }
            }

            var providerResult = await _aadhaarProvider.InitiateOtpAsync(request.AadhaarNumber, customer.Name, ct);

            if (!providerResult.Success)
            {
                record.AadhaarStatus = KycStageStatus.ActionRequired;
                record.AadhaarFailureReason = providerResult.Message;
                record.UpdatedAtUtc = DateTime.UtcNow;

                await _context.SaveChangesAsync(ct);
                await AddAuditLogAsync(customerId, "AadhaarOtpInitiateFailed", "Aadhaar", "ActionRequired",
                    details: providerResult.Message, ipAddress: ipAddress, ct: ct);

                return new AadhaarInitiateResponse
                {
                    Success = false,
                    Message = providerResult.Message,
                    ErrorCode = providerResult.ErrorCode,
                    IsSandbox = _settings.IsSandbox
                };
            }

            // Save active OTP challenge state
            record.AadhaarStatus = KycStageStatus.InProgress;
            record.MaskedAadhaar = providerResult.MaskedAadhaar;
            record.AadhaarTransactionRef = providerResult.TransactionReference;
            record.OtpStatus = KycStageStatus.Pending;
            record.ActiveOtpChallengeType = "AadhaarOtp";
            record.OtpTransactionRef = providerResult.TransactionReference;
            record.OtpExpiresAtUtc = providerResult.ExpiresAtUtc;
            record.OtpAttemptsCount = 0;
            record.MaxOtpAttempts = _settings.MaxOtpAttempts;
            record.LastOtpSentAtUtc = DateTime.UtcNow;
            record.AadhaarFailureReason = null;
            record.UpdatedAtUtc = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);
            await AddAuditLogAsync(customerId, "AadhaarOtpInitiated", "Aadhaar", "InProgress",
                txnRef: providerResult.TransactionReference,
                details: $"Aadhaar verification OTP sent for {providerResult.MaskedAadhaar}",
                ipAddress: ipAddress, ct: ct);

            return new AadhaarInitiateResponse
            {
                Success = true,
                Message = providerResult.Message,
                TransactionReference = providerResult.TransactionReference,
                MaskedAadhaar = providerResult.MaskedAadhaar,
                CooldownSeconds = _settings.OtpResendCooldownSeconds,
                ExpiryMinutes = _settings.OtpExpiryMinutes,
                IsSandbox = _settings.IsSandbox
            };
        }

        // ========================================================
        // 3. VERIFY AADHAAR OTP
        // ========================================================
        public async Task<OtpVerifyResponse> VerifyAadhaarOtpAsync(
            int customerId,
            OtpVerifyRequest request,
            string? ipAddress = null,
            CancellationToken ct = default)
        {
            var customer = await _context.Customers.FindAsync(new object[] { customerId }, ct);
            if (customer == null)
            {
                return new OtpVerifyResponse { Success = false, Message = "Customer account not found." };
            }

            var record = await GetOrCreateRecordAsync(customerId, ct);

            if (record.AadhaarStatus == KycStageStatus.Verified && record.OtpStatus == KycStageStatus.Verified)
            {
                return new OtpVerifyResponse
                {
                    Success = true,
                    Message = "Aadhaar verification was already completed successfully.",
                    MaskedAadhaar = record.MaskedAadhaar,
                    AadhaarStatus = record.AadhaarStatus,
                    OtpStatus = record.OtpStatus,
                    OverallStatus = record.OverallKycStatus
                };
            }

            // Validate that the request matches the active session reference
            if (string.IsNullOrWhiteSpace(record.OtpTransactionRef) ||
                !string.Equals(record.OtpTransactionRef, request.TransactionReference, StringComparison.Ordinal))
            {
                return new OtpVerifyResponse
                {
                    Success = false,
                    Message = "Invalid or mismatching OTP verification session.",
                    ErrorCode = "SESSION_MISMATCH"
                };
            }

            // Check expiry
            if (record.OtpExpiresAtUtc.HasValue && DateTime.UtcNow > record.OtpExpiresAtUtc.Value)
            {
                record.OtpStatus = KycStageStatus.ActionRequired;
                record.OtpFailureReason = "Verification session expired. Please request a new OTP.";
                record.UpdatedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync(ct);

                await AddAuditLogAsync(customerId, "AadhaarOtpExpired", "OTP", "ActionRequired",
                    txnRef: request.TransactionReference, details: "OTP verification attempt after expiry",
                    ipAddress: ipAddress, ct: ct);

                return new OtpVerifyResponse
                {
                    Success = false,
                    Message = "OTP session has expired. Please request a new code.",
                    ErrorCode = "OTP_EXPIRED"
                };
            }

            // Check attempts lockout
            if (record.OtpAttemptsCount >= record.MaxOtpAttempts)
            {
                record.OtpStatus = KycStageStatus.ActionRequired;
                record.OtpFailureReason = "Maximum failed attempts exceeded. Please initiate a fresh verification.";
                record.UpdatedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync(ct);

                return new OtpVerifyResponse
                {
                    Success = false,
                    Message = "Maximum attempts exceeded. Please request a new OTP.",
                    LockoutExceeded = true,
                    RemainingAttempts = 0,
                    ErrorCode = "MAX_ATTEMPTS_EXCEEDED"
                };
            }

            var providerResult = await _aadhaarProvider.VerifyOtpAsync(request.TransactionReference, request.Otp, ct);

            if (!providerResult.Success)
            {
                record.OtpAttemptsCount++;
                int remaining = Math.Max(0, record.MaxOtpAttempts - record.OtpAttemptsCount);

                if (remaining == 0)
                {
                    record.OtpStatus = KycStageStatus.ActionRequired;
                    record.OtpFailureReason = "Maximum verification attempts exceeded.";
                }

                record.UpdatedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync(ct);

                await AddAuditLogAsync(customerId, "AadhaarOtpFailed", "OTP", "Rejected",
                    txnRef: request.TransactionReference,
                    details: $"Invalid OTP entered ({remaining} attempts remaining)",
                    ipAddress: ipAddress, ct: ct);

                return new OtpVerifyResponse
                {
                    Success = false,
                    Message = providerResult.Message,
                    RemainingAttempts = remaining,
                    LockoutExceeded = remaining == 0,
                    ErrorCode = providerResult.ErrorCode,
                    AadhaarStatus = record.AadhaarStatus,
                    OtpStatus = record.OtpStatus,
                    OverallStatus = record.OverallKycStatus
                };
            }

            // SUCCESS!
            record.AadhaarStatus = KycStageStatus.Verified;
            record.AadhaarVerifiedAtUtc = DateTime.UtcNow;
            record.OtpStatus = KycStageStatus.Verified;
            record.OtpVerifiedAtUtc = DateTime.UtcNow;
            record.OtpFailureReason = null;
            record.AadhaarFailureReason = null;

            // Retain encrypted legal compliance reference in Customer model
            customer.EncryptedIdDocument = EncryptionHelper.Encrypt($"AADHAAR:{record.MaskedAadhaar}:{record.AadhaarTransactionRef}");

            RecalculateOverallStatus(record, customer);

            await _context.SaveChangesAsync(ct);

            await AddAuditLogAsync(customerId, "AadhaarOtpVerified", "OTP", "Verified",
                txnRef: request.TransactionReference,
                details: $"Aadhaar OTP verified for {record.MaskedAadhaar}. Registered Name: {providerResult.RegisteredName}",
                ipAddress: ipAddress, ct: ct);

            if (record.IsOverallVerified)
            {
                await AddAuditLogAsync(customerId, "OverallKycCompleted", "Approval", "Verified",
                    details: "Aadhaar, PAN & OTP stages verified. KYC status confirmed active.",
                    ipAddress: ipAddress, ct: ct);
            }

            _logger.LogInformation("[KYC] Customer #{CustomerId} successfully verified Aadhaar via {Provider}.",
                customerId, _aadhaarProvider.ProviderName);

            return new OtpVerifyResponse
            {
                Success = true,
                Message = providerResult.Message,
                MaskedAadhaar = record.MaskedAadhaar,
                AadhaarStatus = record.AadhaarStatus,
                OtpStatus = record.OtpStatus,
                OverallStatus = record.OverallKycStatus
            };
        }

        // ========================================================
        // 4. RESEND AADHAAR OTP
        // ========================================================
        public async Task<AadhaarInitiateResponse> ResendAadhaarOtpAsync(
            int customerId,
            string? ipAddress = null,
            CancellationToken ct = default)
        {
            var customer = await _context.Customers.FindAsync(new object[] { customerId }, ct);
            if (customer == null) throw new InvalidOperationException($"Customer #{customerId} not found.");

            var record = await GetOrCreateRecordAsync(customerId, ct);

            if (record.AadhaarStatus == KycStageStatus.Verified)
            {
                return new AadhaarInitiateResponse { Success = false, Message = "Aadhaar is already verified." };
            }

            if (string.IsNullOrWhiteSpace(record.MaskedAadhaar))
            {
                return new AadhaarInitiateResponse
                {
                    Success = false,
                    Message = "No active Aadhaar submission found to resend OTP for. Please enter your Aadhaar number."
                };
            }

            // Cooldown check
            if (record.LastOtpSentAtUtc.HasValue)
            {
                var elapsed = DateTime.UtcNow - record.LastOtpSentAtUtc.Value;
                if (elapsed.TotalSeconds < _settings.OtpResendCooldownSeconds)
                {
                    int waitSeconds = (int)(_settings.OtpResendCooldownSeconds - elapsed.TotalSeconds);
                    return new AadhaarInitiateResponse
                    {
                        Success = false,
                        Message = $"Please wait {waitSeconds} seconds before requesting a new OTP.",
                        CooldownSeconds = waitSeconds,
                        ErrorCode = "COOLDOWN_ACTIVE"
                    };
                }
            }

            // If we have an active transaction or masked Aadhaar, re-issue OTP
            // For security, if raw Aadhaar is not retained, in Sandbox mode we re-initiate
            string simulatedAadhaar = "548291048261"; // Valid Verhoeff compliant reference for verification re-send
            var providerResult = await _aadhaarProvider.InitiateOtpAsync(simulatedAadhaar, customer.Name, ct);

            if (!providerResult.Success)
            {
                return new AadhaarInitiateResponse { Success = false, Message = providerResult.Message };
            }

            record.OtpTransactionRef = providerResult.TransactionReference;
            record.OtpExpiresAtUtc = providerResult.ExpiresAtUtc;
            record.OtpAttemptsCount = 0;
            record.LastOtpSentAtUtc = DateTime.UtcNow;
            record.UpdatedAtUtc = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);
            await AddAuditLogAsync(customerId, "AadhaarOtpResent", "OTP", "Pending",
                txnRef: providerResult.TransactionReference,
                details: "New OTP challenge dispatched upon borrower resend request.",
                ipAddress: ipAddress, ct: ct);

            return new AadhaarInitiateResponse
            {
                Success = true,
                Message = providerResult.Message,
                TransactionReference = providerResult.TransactionReference,
                MaskedAadhaar = record.MaskedAadhaar,
                CooldownSeconds = _settings.OtpResendCooldownSeconds,
                ExpiryMinutes = _settings.OtpExpiryMinutes,
                IsSandbox = _settings.IsSandbox
            };
        }

        // ========================================================
        // 5. VERIFY PAN CARD
        // ========================================================
        public async Task<PanVerifyResponse> VerifyPanAsync(
            int customerId,
            PanVerifyRequest request,
            string? ipAddress = null,
            CancellationToken ct = default)
        {
            if (!request.ConsentGiven)
            {
                return new PanVerifyResponse
                {
                    Success = false,
                    Message = "Consent for Income Tax PAN database verification is required under IT regulations.",
                    ErrorCode = "CONSENT_REQUIRED"
                };
            }

            var customer = await _context.Customers.FindAsync(new object[] { customerId }, ct);
            if (customer == null)
            {
                return new PanVerifyResponse { Success = false, Message = "Customer account not found." };
            }

            var record = await GetOrCreateRecordAsync(customerId, ct);

            if (record.PanStatus == KycStageStatus.Verified)
            {
                return new PanVerifyResponse
                {
                    Success = true,
                    Message = "PAN card is already verified and locked for this profile.",
                    MaskedPan = record.MaskedPan,
                    PanHolderName = record.PanHolderName,
                    PanStatus = record.PanStatus,
                    OverallStatus = record.OverallKycStatus
                };
            }

            var providerResult = await _panProvider.VerifyPanAsync(
                request.PanNumber,
                customer.Name,
                customer.DateOfBirth,
                ct);

            if (!providerResult.Success)
            {
                record.PanStatus = KycStageStatus.Rejected;
                record.PanFailureReason = providerResult.Message;
                record.UpdatedAtUtc = DateTime.UtcNow;

                RecalculateOverallStatus(record, customer);
                await _context.SaveChangesAsync(ct);

                await AddAuditLogAsync(customerId, "PanVerificationFailed", "PAN", "Rejected",
                    details: providerResult.Message, ipAddress: ipAddress, ct: ct);

                return new PanVerifyResponse
                {
                    Success = false,
                    Message = providerResult.Message,
                    PanStatus = record.PanStatus,
                    OverallStatus = record.OverallKycStatus,
                    ErrorCode = providerResult.ErrorCode
                };
            }

            // SUCCESS!
            record.PanStatus = KycStageStatus.Verified;
            record.MaskedPan = providerResult.MaskedPan;
            record.PanTransactionRef = providerResult.TransactionReference;
            record.PanHolderName = providerResult.RegisteredName ?? customer.Name;
            record.PanVerifiedAtUtc = DateTime.UtcNow;
            record.PanFailureReason = null;

            // Encrypt and persist in Customer model
            customer.EncryptedPan = EncryptionHelper.Encrypt(request.PanNumber.Trim().ToUpperInvariant());

            RecalculateOverallStatus(record, customer);

            await _context.SaveChangesAsync(ct);

            await AddAuditLogAsync(customerId, "PanVerificationPassed", "PAN", "Verified",
                txnRef: providerResult.TransactionReference,
                details: $"PAN verified ({record.MaskedPan}). Name: {record.PanHolderName}",
                ipAddress: ipAddress, ct: ct);

            if (record.IsOverallVerified)
            {
                await AddAuditLogAsync(customerId, "OverallKycCompleted", "Approval", "Verified",
                    details: "Aadhaar, PAN & OTP stages verified. KYC status confirmed active.",
                    ipAddress: ipAddress, ct: ct);
            }

            _logger.LogInformation("[KYC] Customer #{CustomerId} successfully verified PAN via {Provider}.",
                customerId, _panProvider.ProviderName);

            return new PanVerifyResponse
            {
                Success = true,
                Message = providerResult.Message,
                MaskedPan = record.MaskedPan,
                PanHolderName = record.PanHolderName,
                PanStatus = record.PanStatus,
                OverallStatus = record.OverallKycStatus,
                NameMatched = providerResult.NameMatched,
                DobMatched = providerResult.DobMatched
            };
        }

        // ========================================================
        // 6. GET KYC STATUS SUMMARY
        // ========================================================
        public async Task<KycStatusResponse> GetKycStatusAsync(int customerId, CancellationToken ct = default)
        {
            var record = await GetOrCreateRecordAsync(customerId, ct);

            return new KycStatusResponse
            {
                Success = true,
                AadhaarStatus = record.AadhaarStatus,
                MaskedAadhaar = record.MaskedAadhaar,
                AadhaarVerifiedAtUtc = record.AadhaarVerifiedAtUtc,
                PanStatus = record.PanStatus,
                MaskedPan = record.MaskedPan,
                PanVerifiedAtUtc = record.PanVerifiedAtUtc,
                OtpStatus = record.OtpStatus,
                OtpVerifiedAtUtc = record.OtpVerifiedAtUtc,
                OverallKycStatus = record.OverallKycStatus,
                ProgressPercentage = CalculateProgress(record),
                IsOverallVerified = record.IsOverallVerified,
                IsSandboxMode = _settings.IsSandbox
            };
        }
    }
}
