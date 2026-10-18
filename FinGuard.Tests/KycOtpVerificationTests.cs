using System;
using System.Threading.Tasks;
using FinGuard.Models;
using FinGuard.Services.Kyc;
using FinGuard.Utilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinGuard.Tests
{
    public class KycOtpVerificationTests
    {
        private (SandboxKycVerificationProvider Provider, IMemoryCache Cache) CreateProvider(int cooldown = 60, int expiryMins = 10, int maxAttempts = 3)
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var settings = Options.Create(new KycSettings
            {
                ProviderMode = "Sandbox",
                OtpResendCooldownSeconds = cooldown,
                OtpExpiryMinutes = expiryMins,
                MaxOtpAttempts = maxAttempts
            });
            var provider = new SandboxKycVerificationProvider(cache, settings, NullLogger<SandboxKycVerificationProvider>.Instance);
            return (provider, cache);
        }

        private string GetValidAadhaar()
        {
            string base11 = "54829104826";
            int checkDigit = KycFormatHelper.GenerateVerhoeffCheckDigit(base11);
            return base11 + checkDigit;
        }

        [Fact]
        public async Task InitiateOtp_ValidAadhaar_ReturnsSuccessWithTransactionRef()
        {
            var (provider, _) = CreateProvider();
            string validAadhaar = GetValidAadhaar();

            var result = await provider.InitiateOtpAsync(validAadhaar, "Aditya Kumar");

            Assert.True(result.Success);
            Assert.NotNull(result.TransactionReference);
            Assert.StartsWith("SBX-UIDAI-", result.TransactionReference);
            Assert.Equal("•••• •••• " + validAadhaar.Substring(8), result.MaskedAadhaar);
            Assert.True(result.IsSandbox);
        }

        [Fact]
        public async Task InitiateOtp_InvalidAadhaar_ReturnsFormatError()
        {
            var (provider, _) = CreateProvider();

            var result = await provider.InitiateOtpAsync("111122223333", "Aditya Kumar");

            Assert.False(result.Success);
            Assert.Null(result.TransactionReference);
            Assert.Equal("INVALID_AADHAAR_FORMAT", result.ErrorCode);
        }

        [Fact]
        public async Task VerifyOtp_ValidOtp_SucceedsAndClearsChallenge()
        {
            var (provider, _) = CreateProvider();
            string validAadhaar = GetValidAadhaar();

            // Initiate challenge
            var initResult = await provider.InitiateOtpAsync(validAadhaar, "Aditya Kumar");
            Assert.True(initResult.Success);

            // Extract the simulated demo OTP from the provider result message: "(Demo code: XXXXXX)"
            string msg = initResult.Message;
            int idx = msg.IndexOf("Demo code: ");
            Assert.True(idx > 0);
            string otp = msg.Substring(idx + 11, 6);

            // Verify with genuine generated OTP
            var verifyResult = await provider.VerifyOtpAsync(initResult.TransactionReference!, otp);

            Assert.True(verifyResult.Success);
            Assert.NotNull(verifyResult.VerifiedAtUtc);
            Assert.Equal(initResult.MaskedAadhaar, verifyResult.MaskedAadhaar);

            // Replay attack test: second submission must fail because challenge was consumed
            var replayResult = await provider.VerifyOtpAsync(initResult.TransactionReference!, otp);
            Assert.False(replayResult.Success);
            Assert.Equal("OTP_EXPIRED", replayResult.ErrorCode);
        }

        [Fact]
        public async Task VerifyOtp_WrongOtp_DecrementsRemainingAttempts()
        {
            var (provider, _) = CreateProvider(maxAttempts: 3);
            string validAadhaar = GetValidAadhaar();

            var initResult = await provider.InitiateOtpAsync(validAadhaar, "Aditya Kumar");

            // Submit incorrect OTP
            var verifyResult = await provider.VerifyOtpAsync(initResult.TransactionReference!, "000000");

            Assert.False(verifyResult.Success);
            Assert.Equal("INVALID_OTP", verifyResult.ErrorCode);
            Assert.Equal(2, verifyResult.RemainingAttempts);
            Assert.False(verifyResult.LockoutExceeded);
        }

        [Fact]
        public async Task VerifyOtp_ExceedsMaxAttempts_LocksOutSession()
        {
            var (provider, _) = CreateProvider(maxAttempts: 3);
            string validAadhaar = GetValidAadhaar();

            var initResult = await provider.InitiateOtpAsync(validAadhaar, "Aditya Kumar");
            string txn = initResult.TransactionReference!;

            // Attempt 1
            await provider.VerifyOtpAsync(txn, "000001");
            // Attempt 2
            await provider.VerifyOtpAsync(txn, "000002");
            // Attempt 3
            var thirdResult = await provider.VerifyOtpAsync(txn, "000003");
            Assert.Equal(0, thirdResult.RemainingAttempts);

            // Attempt 4 (Beyond max attempts)
            var fourthResult = await provider.VerifyOtpAsync(txn, "000004");
            Assert.False(fourthResult.Success);
            Assert.True(fourthResult.LockoutExceeded);
            Assert.Equal("MAX_ATTEMPTS_EXCEEDED", fourthResult.ErrorCode);
        }

        [Fact]
        public async Task VerifyOtp_ExpiredSession_ReturnsExpiredError()
        {
            // Create provider with 0 min expiry so it expires immediately
            var (provider, _) = CreateProvider(expiryMins: -1);
            string validAadhaar = GetValidAadhaar();

            var initResult = await provider.InitiateOtpAsync(validAadhaar, "Aditya Kumar");

            var verifyResult = await provider.VerifyOtpAsync(initResult.TransactionReference!, "123456");

            Assert.False(verifyResult.Success);
            Assert.Equal("OTP_EXPIRED", verifyResult.ErrorCode);
        }
    }
}
