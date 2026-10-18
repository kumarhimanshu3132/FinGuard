using System;
using System.Threading.Tasks;
using FinGuard.Data;
using FinGuard.Models;
using FinGuard.Services.Kyc;
using FinGuard.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinGuard.Tests
{
    public class KycOverallStatusAndWorkflowTests
    {
        private (ApplicationDbContext Context, KycVerificationService Service, SandboxKycVerificationProvider Provider) CreateService()
        {
            var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: "KycTestDb_" + Guid.NewGuid().ToString())
                .Options;

            var context = new ApplicationDbContext(dbOptions);

            var cache = new MemoryCache(new MemoryCacheOptions());
            var settings = Options.Create(new KycSettings
            {
                ProviderMode = "Sandbox",
                OtpResendCooldownSeconds = 60,
                OtpExpiryMinutes = 10,
                MaxOtpAttempts = 3
            });

            var provider = new SandboxKycVerificationProvider(cache, settings, NullLogger<SandboxKycVerificationProvider>.Instance);
            var service = new KycVerificationService(
                context,
                provider,
                provider,
                settings,
                NullLogger<KycVerificationService>.Instance);

            return (context, service, provider);
        }

        private async Task<Customer> SeedCustomerAsync(ApplicationDbContext context, int customerId = 1)
        {
            var customer = new Customer
            {
                CustomerId = customerId,
                Name = "Rohan Sharma",
                Email = $"rohan{customerId}@example.com",
                Phone = "9876543210",
                DateOfBirth = DateTime.UtcNow.AddYears(-25),
                MonthlyIncome = 45000,
                Address = "Plot 12, Indiranagar, Bangalore",
                PinCode = "560038",
                CountryCode = "+91",
                KycStatus = "Pending"
            };

            context.Customers.Add(customer);
            await context.SaveChangesAsync();
            return customer;
        }

        private string GetValidAadhaar()
        {
            string base11 = "54829104826";
            int checkDigit = KycFormatHelper.GenerateVerhoeffCheckDigit(base11);
            return base11 + checkDigit;
        }

        [Fact]
        public async Task GetDashboard_InitialState_CalculatesIncompleteStatus()
        {
            var (context, service, _) = CreateService();
            var customer = await SeedCustomerAsync(context);

            var vm = await service.GetKycDashboardAsync(customer.CustomerId);

            Assert.Equal(KycStageStatus.NotStarted, vm.AadhaarStatus);
            Assert.Equal(KycStageStatus.NotStarted, vm.PanStatus);
            Assert.Equal(KycStageStatus.NotStarted, vm.OtpStatus);
            Assert.Equal(OverallKycStatusConstants.Incomplete, vm.OverallKycStatus);
            Assert.False(vm.IsOverallVerified);
            Assert.Equal(0, vm.ProgressPercentage);
        }

        [Fact]
        public async Task AadhaarVerifiedAlone_LeavesOverallKycPending_NeverMarksCustomerVerified()
        {
            var (context, service, _) = CreateService();
            var customer = await SeedCustomerAsync(context);
            string validAadhaar = GetValidAadhaar();

            // 1. Initiate Aadhaar
            var initResp = await service.InitiateAadhaarVerificationAsync(customer.CustomerId, new AadhaarInitiateRequest
            {
                AadhaarNumber = validAadhaar,
                ConsentGiven = true
            });
            Assert.True(initResp.Success);

            // Extract demo OTP
            string msg = initResp.Message;
            string otp = msg.Substring(msg.IndexOf("Demo code: ") + 11, 6);

            // 2. Verify Aadhaar OTP
            var otpResp = await service.VerifyAadhaarOtpAsync(customer.CustomerId, new OtpVerifyRequest
            {
                TransactionReference = initResp.TransactionReference!,
                Otp = otp
            });
            Assert.True(otpResp.Success);

            // Verify Dashboard Status
            var vm = await service.GetKycDashboardAsync(customer.CustomerId);
            Assert.Equal(KycStageStatus.Verified, vm.AadhaarStatus);
            Assert.Equal(KycStageStatus.Verified, vm.OtpStatus);
            Assert.Equal(KycStageStatus.NotStarted, vm.PanStatus);

            // Overall KYC must remain Pending and NOT Verified
            Assert.Equal(OverallKycStatusConstants.Pending, vm.OverallKycStatus);
            Assert.False(vm.IsOverallVerified);

            var dbCustomer = await context.Customers.FindAsync(customer.CustomerId);
            Assert.Equal("Pending", dbCustomer!.KycStatus);
            Assert.False(dbCustomer.IsEligibleForLoan);
        }

        [Fact]
        public async Task PanVerifiedAlone_LeavesOverallKycPending_NeverMarksCustomerVerified()
        {
            var (context, service, _) = CreateService();
            var customer = await SeedCustomerAsync(context);

            var panResp = await service.VerifyPanAsync(customer.CustomerId, new PanVerifyRequest
            {
                PanNumber = "BKRPK1049M",
                ConsentGiven = true
            });
            Assert.True(panResp.Success);

            var vm = await service.GetKycDashboardAsync(customer.CustomerId);
            Assert.Equal(KycStageStatus.Verified, vm.PanStatus);
            Assert.Equal(KycStageStatus.NotStarted, vm.AadhaarStatus);
            Assert.Equal(OverallKycStatusConstants.Pending, vm.OverallKycStatus);
            Assert.False(vm.IsOverallVerified);

            var dbCustomer = await context.Customers.FindAsync(customer.CustomerId);
            Assert.Equal("Pending", dbCustomer!.KycStatus);
            Assert.False(dbCustomer.IsEligibleForLoan);
        }

        [Fact]
        public async Task BothAadhaarAndPanVerified_TransitionsToOverallVerifiedAndUnlocksLoan()
        {
            var (context, service, _) = CreateService();
            var customer = await SeedCustomerAsync(context);
            string validAadhaar = GetValidAadhaar();

            // 1. Verify Aadhaar & OTP
            var initResp = await service.InitiateAadhaarVerificationAsync(customer.CustomerId, new AadhaarInitiateRequest
            {
                AadhaarNumber = validAadhaar,
                ConsentGiven = true
            });
            string otp = initResp.Message.Substring(initResp.Message.IndexOf("Demo code: ") + 11, 6);
            await service.VerifyAadhaarOtpAsync(customer.CustomerId, new OtpVerifyRequest
            {
                TransactionReference = initResp.TransactionReference!,
                Otp = otp
            });

            // 2. Verify PAN
            var panResp = await service.VerifyPanAsync(customer.CustomerId, new PanVerifyRequest
            {
                PanNumber = "BKRPK1049M",
                ConsentGiven = true
            });
            Assert.True(panResp.Success);

            // 3. Check Overall Status
            var vm = await service.GetKycDashboardAsync(customer.CustomerId);
            Assert.Equal(KycStageStatus.Verified, vm.AadhaarStatus);
            Assert.Equal(KycStageStatus.Verified, vm.PanStatus);
            Assert.Equal(KycStageStatus.Verified, vm.OtpStatus);
            Assert.Equal(OverallKycStatusConstants.Verified, vm.OverallKycStatus);
            Assert.True(vm.IsOverallVerified);
            Assert.Equal(100, vm.ProgressPercentage);

            // Database Customer state must be synchronized
            var dbCustomer = await context.Customers.FindAsync(customer.CustomerId);
            Assert.Equal("Verified", dbCustomer!.KycStatus);
            Assert.True(dbCustomer.IsEligibleForLoan);
            Assert.NotNull(dbCustomer.EncryptedPan);
            Assert.NotNull(dbCustomer.EncryptedIdDocument);
        }

        [Fact]
        public async Task ResendOtp_WithinCooldown_IsRejected()
        {
            var (context, service, _) = CreateService();
            var customer = await SeedCustomerAsync(context);
            string validAadhaar = GetValidAadhaar();

            // Initial send
            await service.InitiateAadhaarVerificationAsync(customer.CustomerId, new AadhaarInitiateRequest
            {
                AadhaarNumber = validAadhaar,
                ConsentGiven = true
            });

            // Immediate resend attempt (within 60s cooldown)
            var resendResp = await service.ResendAadhaarOtpAsync(customer.CustomerId);

            Assert.False(resendResp.Success);
            Assert.Equal("COOLDOWN_ACTIVE", resendResp.ErrorCode);
            Assert.True(resendResp.CooldownSeconds > 0);
        }

        [Fact]
        public async Task CustomerSelfServiceKyc_WhenCompleted_MarksVerifiedWithoutAdminIntervention()
        {
            var (context, service, _) = CreateService();
            var customer = await SeedCustomerAsync(context);

            // Customer verifies Aadhaar directly via self-service
            var initRes = await service.InitiateAadhaarVerificationAsync(customer.CustomerId, new AadhaarInitiateRequest
            {
                AadhaarNumber = "548291048261",
                ConsentGiven = true
            });
            await service.VerifyAadhaarOtpAsync(customer.CustomerId, new OtpVerifyRequest
            {
                TransactionReference = initRes.TransactionReference!,
                Otp = "123456"
            });

            // Customer verifies PAN directly via self-service
            await service.VerifyPanAsync(customer.CustomerId, new PanVerifyRequest
            {
                PanNumber = "ABCDE1234F",
                ConsentGiven = true
            });

            var vm = await service.GetKycDashboardAsync(customer.CustomerId);
            Assert.Equal(OverallKycStatusConstants.Verified, vm.OverallKycStatus);
            Assert.True(vm.IsOverallVerified);

            var dbCustomer = await context.Customers.FindAsync(customer.CustomerId);
            Assert.Equal("Verified", dbCustomer!.KycStatus);
        }
    }
}
