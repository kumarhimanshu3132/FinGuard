using System;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FinGuard.Models;
using FinGuard.Utilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinGuard.Services.Kyc
{
    public class SandboxKycVerificationProvider : IAadhaarVerificationProvider, IPanVerificationProvider
    {
        private readonly IMemoryCache _cache;
        private readonly KycSettings _settings;
        private readonly ILogger<SandboxKycVerificationProvider> _logger;

        public string ProviderName => "FinGuard Institutional Sandbox Gateway (UIDAI / NSDL Mock)";
        public bool IsSandbox => true;

        // Challenge state stored in cache
        private class SandboxOtpChallenge
        {
            public string TransactionReference { get; set; } = string.Empty;
            public string MaskedAadhaar { get; set; } = string.Empty;
            public string CustomerName { get; set; } = string.Empty;
            public string ExpectedOtp { get; set; } = string.Empty;
            public DateTime ExpiresAtUtc { get; set; }
            public int AttemptsCount { get; set; }
            public DateTime CreatedAtUtc { get; set; }
        }

        public SandboxKycVerificationProvider(
            IMemoryCache cache,
            IOptions<KycSettings> options,
            ILogger<SandboxKycVerificationProvider> logger)
        {
            _cache = cache;
            _settings = options.Value;
            _logger = logger;
        }

        // ========================================================
        // AADHAAR OTP INITIATION
        // ========================================================
        public async Task<AadhaarInitiateResult> InitiateOtpAsync(
            string aadhaarNumber,
            string customerName,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(400, cancellationToken); // Simulate network latency

            var (isValid, errorMessage) = KycFormatHelper.ValidateAadhaar(aadhaarNumber, isSandbox: true);
            if (!isValid)
            {
                return new AadhaarInitiateResult
                {
                    Success = false,
                    Message = errorMessage ?? "The Aadhaar checksum is invalid. Please check the number entered.",
                    ErrorCode = "INVALID_AADHAAR_FORMAT",
                    IsSandbox = true
                };
            }

            string masked = KycFormatHelper.MaskAadhaar(aadhaarNumber);

            // Generate cryptographically unique transaction ID
            string txnRef = "SBX-UIDAI-" + Guid.NewGuid().ToString("N").Substring(0, 12).ToUpperInvariant();

            // In sandbox mode, generate a 6-digit OTP challenge
            // For predictable deterministic testing in automated tests or demo, standard sandbox OTP is derived or generated
            string generatedOtp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();

            // If special test Aadhaar ending in '0000', use test OTP '482619' for deterministic automated tests
            if (aadhaarNumber.Replace(" ", "").EndsWith("0000"))
            {
                generatedOtp = "482619";
            }

            var challenge = new SandboxOtpChallenge
            {
                TransactionReference = txnRef,
                MaskedAadhaar = masked,
                CustomerName = customerName,
                ExpectedOtp = generatedOtp,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(_settings.OtpExpiryMinutes),
                AttemptsCount = 0,
                CreatedAtUtc = DateTime.UtcNow
            };

            // Cache challenge with expiry
            _cache.Set($"KYC_CHALLENGE_{txnRef}", challenge, TimeSpan.FromMinutes(_settings.OtpExpiryMinutes + 2));

            _logger.LogInformation("[Sandbox KYC] Aadhaar OTP initiated for {Masked}. Txn: {TxnRef}.",
                masked, txnRef);

            return new AadhaarInitiateResult
            {
                Success = true,
                TransactionReference = txnRef,
                MaskedAadhaar = masked,
                ExpiresAtUtc = challenge.ExpiresAtUtc,
                CooldownSeconds = _settings.OtpResendCooldownSeconds,
                Message = $"[SANDBOX DEMO] OTP challenge dispatched. (Demo code: {generatedOtp}) - or authorization code 123456",
                IsSandbox = true
            };
        }

        // ========================================================
        // AADHAAR OTP VERIFICATION
        // ========================================================
        public async Task<AadhaarOtpVerifyResult> VerifyOtpAsync(
            string transactionReference,
            string otp,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(350, cancellationToken); // Simulate network latency

            if (string.IsNullOrWhiteSpace(transactionReference))
            {
                return new AadhaarOtpVerifyResult
                {
                    Success = false,
                    Message = "Transaction reference is missing.",
                    ErrorCode = "MISSING_TXN_REF"
                };
            }

            string cacheKey = $"KYC_CHALLENGE_{transactionReference}";
            if (!_cache.TryGetValue(cacheKey, out SandboxOtpChallenge? challenge) || challenge == null)
            {
                return new AadhaarOtpVerifyResult
                {
                    Success = false,
                    Message = "The OTP challenge has expired or is invalid. Please request a new OTP.",
                    ErrorCode = "OTP_EXPIRED"
                };
            }

            if (DateTime.UtcNow > challenge.ExpiresAtUtc)
            {
                _cache.Remove(cacheKey);
                return new AadhaarOtpVerifyResult
                {
                    Success = false,
                    Message = "The verification session has timed out. Please initiate a new Aadhaar OTP.",
                    ErrorCode = "OTP_EXPIRED"
                };
            }

            challenge.AttemptsCount++;

            if (challenge.AttemptsCount > _settings.MaxOtpAttempts)
            {
                _cache.Remove(cacheKey);
                return new AadhaarOtpVerifyResult
                {
                    Success = false,
                    Message = $"Maximum verification attempts ({_settings.MaxOtpAttempts}) exceeded. This challenge has been invalidated for security.",
                    ErrorCode = "MAX_ATTEMPTS_EXCEEDED",
                    LockoutExceeded = true,
                    RemainingAttempts = 0
                };
            }

            // Verify submitted OTP matches expected challenge or standard test codes
            string submittedOtp = otp?.Trim() ?? string.Empty;
            bool isMatch = string.Equals(challenge.ExpectedOtp, submittedOtp, StringComparison.Ordinal)
                || submittedOtp == "123456"
                || submittedOtp == "482619";

            if (!isMatch)
            {
                int remaining = Math.Max(0, _settings.MaxOtpAttempts - challenge.AttemptsCount);
                _cache.Set(cacheKey, challenge, TimeSpan.FromMinutes(_settings.OtpExpiryMinutes));

                return new AadhaarOtpVerifyResult
                {
                    Success = false,
                    Message = remaining > 0 
                        ? $"OTP verification failed. Please check the OTP and try again. ({remaining} attempt(s) remaining)"
                        : "OTP verification failed. Maximum attempts exceeded. Please request a new OTP.",
                    ErrorCode = "INVALID_OTP",
                    RemainingAttempts = remaining,
                    LockoutExceeded = remaining == 0
                };
            }

            // OTP verified successfully! Remove challenge from cache to prevent replay
            _cache.Remove(cacheKey);

            return new AadhaarOtpVerifyResult
            {
                Success = true,
                TransactionReference = transactionReference,
                MaskedAadhaar = challenge.MaskedAadhaar,
                RegisteredName = challenge.CustomerName,
                VerifiedAtUtc = DateTime.UtcNow,
                Message = "[SANDBOX DEMO] Aadhaar OTP successfully verified against UIDAI sandbox registry."
            };
        }

        // ========================================================
        // PAN VERIFICATION
        // ========================================================
        public async Task<PanVerificationResult> VerifyPanAsync(
            string panNumber,
            string legalName,
            DateTime dateOfBirth,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(400, cancellationToken); // Simulate network latency

            var (isValid, errorMessage) = KycFormatHelper.ValidatePan(panNumber);
            if (!isValid)
            {
                return new PanVerificationResult
                {
                    Success = false,
                    Message = errorMessage ?? "Invalid PAN format.",
                    ErrorCode = "INVALID_PAN_FORMAT",
                    IsSandbox = true
                };
            }

            string cleanPan = panNumber.Trim().ToUpperInvariant();
            string maskedPan = KycFormatHelper.MaskPan(cleanPan);
            string txnRef = "SBX-NSDL-" + Guid.NewGuid().ToString("N").Substring(0, 12).ToUpperInvariant();

            // Simulate testing rejection flag if PAN begins with 'ZZ' or contains 'FAIL'
            if (cleanPan.StartsWith("ZZ") || cleanPan.Contains("FAIL"))
            {
                return new PanVerificationResult
                {
                    Success = false,
                    TransactionReference = txnRef,
                    MaskedPan = maskedPan,
                    Message = "[SANDBOX DEMO] Record not found in Income Tax Department database or status inactive.",
                    ErrorCode = "PAN_NOT_FOUND",
                    IsSandbox = true
                };
            }

            // Verify demographic name correspondence
            // 5th character of PAN is the first character of the last name/surname
            string[] nameParts = legalName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string lastName = nameParts.Length > 0 ? nameParts[nameParts.Length - 1].ToUpperInvariant() : "";
            char expectedChar = !string.IsNullOrEmpty(lastName) ? lastName[0] : ' ';

            char panFifthChar = cleanPan[4];
            bool fifthCharMatches = (expectedChar != ' ' && panFifthChar == expectedChar);

            // In sandbox mode, verify legal name matching
            bool nameMatch = true;
            if (!string.IsNullOrWhiteSpace(legalName))
            {
                // In demo/test sandbox, we accept the name if reasonable or match
                nameMatch = true;
            }

            return new PanVerificationResult
            {
                Success = true,
                TransactionReference = txnRef,
                MaskedPan = maskedPan,
                RegisteredName = legalName,
                NameMatched = nameMatch,
                DobMatched = true,
                VerifiedAtUtc = DateTime.UtcNow,
                Message = "[SANDBOX DEMO] PAN verified against NSDL/Income Tax database. Name and status confirmed Active.",
                IsSandbox = true
            };
        }
    }
}
