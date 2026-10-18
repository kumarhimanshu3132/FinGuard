using System.Threading;
using System.Threading.Tasks;
using FinGuard.Models;

namespace FinGuard.Services.Kyc
{
    public interface IKycVerificationService
    {
        Task<KycDashboardViewModel> GetKycDashboardAsync(int customerId, CancellationToken cancellationToken = default);
        Task<AadhaarInitiateResponse> InitiateAadhaarVerificationAsync(int customerId, AadhaarInitiateRequest request, string? ipAddress = null, CancellationToken cancellationToken = default);
        Task<OtpVerifyResponse> VerifyAadhaarOtpAsync(int customerId, OtpVerifyRequest request, string? ipAddress = null, CancellationToken cancellationToken = default);
        Task<AadhaarInitiateResponse> ResendAadhaarOtpAsync(int customerId, string? ipAddress = null, CancellationToken cancellationToken = default);
        Task<PanVerifyResponse> VerifyPanAsync(int customerId, PanVerifyRequest request, string? ipAddress = null, CancellationToken cancellationToken = default);
        Task<KycStatusResponse> GetKycStatusAsync(int customerId, CancellationToken cancellationToken = default);
    }
}
