using System;
using System.Net.Http;
using System.Threading.Tasks;
using FinGuard.Data;
using FinGuard.Models;
using FinGuard.Services.Kyc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinGuard.Tests
{
    public class KycSecurityAndAccessTests
    {
        [Fact]
        public async Task ProductionProvider_WithoutApiKey_FailsSafelyWithoutFakingVerification()
        {
            var httpClient = new HttpClient();
            var emptySettings = Options.Create(new KycSettings
            {
                ProviderMode = "Production",
                ApiKey = "", // Missing API key
                ProviderBaseUrl = "https://api.sandbox.surepass.io"
            });

            var prodProvider = new ProductionKycVerificationProvider(
                httpClient,
                emptySettings,
                NullLogger<ProductionKycVerificationProvider>.Instance);

            // Aadhaar attempt
            var aadhaarResult = await prodProvider.InitiateOtpAsync("548291048261", "Aditya");
            Assert.False(aadhaarResult.Success);
            Assert.Equal("CONFIG_MISSING", aadhaarResult.ErrorCode);
            Assert.Contains("apiKey", aadhaarResult.Message, StringComparison.OrdinalIgnoreCase);

            // PAN attempt
            var panResult = await prodProvider.VerifyPanAsync("ABCDE1234F", "Aditya", DateTime.UtcNow.AddYears(-22));
            Assert.False(panResult.Success);
            Assert.Equal("CONFIG_MISSING", panResult.ErrorCode);
        }

        [Fact]
        public async Task ConsentRequired_WhenConsentFalse_RejectsVerification()
        {
            var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: "ConsentTestDb_" + Guid.NewGuid().ToString())
                .Options;

            using var context = new ApplicationDbContext(dbOptions);
            context.Customers.Add(new Customer
            {
                CustomerId = 42,
                Name = "Priya Patel",
                Email = "priya@example.com",
                Phone = "9876543211",
                DateOfBirth = DateTime.UtcNow.AddYears(-24),
                MonthlyIncome = 35000,
                Address = "Ahmedabad",
                PinCode = "380001",
                CountryCode = "+91"
            });
            await context.SaveChangesAsync();

            var cache = new MemoryCache(new MemoryCacheOptions());
            var settings = Options.Create(new KycSettings { ProviderMode = "Sandbox" });
            var provider = new SandboxKycVerificationProvider(cache, settings, NullLogger<SandboxKycVerificationProvider>.Instance);
            var service = new KycVerificationService(context, provider, provider, settings, NullLogger<KycVerificationService>.Instance);

            // Aadhaar without consent
            var aadhaarResp = await service.InitiateAadhaarVerificationAsync(42, new AadhaarInitiateRequest
            {
                AadhaarNumber = "548291048261",
                ConsentGiven = false
            });
            Assert.False(aadhaarResp.Success);
            Assert.Equal("CONSENT_REQUIRED", aadhaarResp.ErrorCode);

            // PAN without consent
            var panResp = await service.VerifyPanAsync(42, new PanVerifyRequest
            {
                PanNumber = "BKRPK1049M",
                ConsentGiven = false
            });
            Assert.False(panResp.Success);
            Assert.Equal("CONSENT_REQUIRED", panResp.ErrorCode);
        }

        [Fact]
        public async Task CrossBorrower_MismatchedTransactionReference_IsRejected()
        {
            var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: "CrossBorrowerTestDb_" + Guid.NewGuid().ToString())
                .Options;

            using var context = new ApplicationDbContext(dbOptions);
            context.Customers.Add(new Customer
            {
                CustomerId = 101,
                Name = "Borrower One",
                Email = "one@example.com",
                Phone = "9876543201",
                DateOfBirth = DateTime.UtcNow.AddYears(-24),
                MonthlyIncome = 35000,
                Address = "Delhi",
                PinCode = "110001",
                CountryCode = "+91"
            });
            context.Customers.Add(new Customer
            {
                CustomerId = 102,
                Name = "Borrower Two",
                Email = "two@example.com",
                Phone = "9876543202",
                DateOfBirth = DateTime.UtcNow.AddYears(-26),
                MonthlyIncome = 45000,
                Address = "Delhi",
                PinCode = "110002",
                CountryCode = "+91"
            });
            await context.SaveChangesAsync();

            var cache = new MemoryCache(new MemoryCacheOptions());
            var settings = Options.Create(new KycSettings { ProviderMode = "Sandbox" });
            var provider = new SandboxKycVerificationProvider(cache, settings, NullLogger<SandboxKycVerificationProvider>.Instance);
            var service = new KycVerificationService(context, provider, provider, settings, NullLogger<KycVerificationService>.Instance);

            // Borrower 1 initiates challenge
            string base11 = "54829104826";
            string validAadhaar = base11 + FinGuard.Utilities.KycFormatHelper.GenerateVerhoeffCheckDigit(base11);

            var init1 = await service.InitiateAadhaarVerificationAsync(101, new AadhaarInitiateRequest
            {
                AadhaarNumber = validAadhaar,
                ConsentGiven = true
            });
            Assert.True(init1.Success);

            // Borrower 2 attempts to submit Borrower 1's transaction reference
            var crossAttempt = await service.VerifyAadhaarOtpAsync(102, new OtpVerifyRequest
            {
                TransactionReference = init1.TransactionReference!,
                Otp = "123456"
            });

            Assert.False(crossAttempt.Success);
            Assert.Equal("SESSION_MISMATCH", crossAttempt.ErrorCode);
        }
    }
}
