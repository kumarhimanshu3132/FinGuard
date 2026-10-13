using System.Threading.Tasks;
using FinGuard.Models;

namespace FinGuard.Services
{
    public interface IRazorpayPaymentService
    {
        /// <summary>
        /// Creates a server-side Razorpay order for an eligible loan and customer.
        /// </summary>
        Task<CreateOrderResponse> CreateOrderAsync(int customerId, CreateOrderRequest request);

        /// <summary>
        /// Constant-time verification of payment HMAC-SHA256 signature using official Razorpay algorithm.
        /// </summary>
        bool VerifyPaymentSignature(string orderId, string paymentId, string signature);

        /// <summary>
        /// Constant-time verification of raw webhook body against Razorpay Webhook Secret.
        /// </summary>
        bool VerifyWebhookSignature(string rawBody, string signature);

        /// <summary>
        /// Verifies payment credentials and idempotently applies the repayment ledger update.
        /// Prevents overpayment, duplicate callback deductions, and race conditions.
        /// </summary>
        Task<PaymentVerificationResponse> ProcessSuccessfulPaymentAsync(int customerId, PaymentVerificationRequest request);

        /// <summary>
        /// Processes an incoming asynchronous webhook event idempotently.
        /// </summary>
        Task<(bool Success, string Message)> ProcessWebhookPaymentAsync(string rawBody, string signature);

        /// <summary>
        /// Gets public checkout configuration (KeyId, Currency, Theme) without exposing KeySecret.
        /// </summary>
        RazorpaySettings GetPublicSettings();
    }
}
