namespace FinGuard.Models
{
    public class RazorpaySettings
    {
        public const string SectionName = "Razorpay";

        /// <summary>
        /// Razorpay Key ID (Safe for client-side checkout initialization)
        /// E.g.: rzp_test_xxxxxxxxxxxxxx
        /// </summary>
        public string KeyId { get; set; } = string.Empty;

        /// <summary>
        /// Razorpay Key Secret (STRICTLY SERVER-SIDE - Never expose to client/logs/API)
        /// </summary>
        public string KeySecret { get; set; } = string.Empty;

        /// <summary>
        /// Optional Webhook Secret for HMAC verification of asynchronous webhooks
        /// </summary>
        public string WebhookSecret { get; set; } = string.Empty;

        /// <summary>
        /// Default currency (INR)
        /// </summary>
        public string Currency { get; set; } = "INR";

        /// <summary>
        /// Company display name on Razorpay Checkout modal
        /// </summary>
        public string CompanyName { get; set; } = "FinGuard Microfinance";

        /// <summary>
        /// Brand accent color for Checkout UI
        /// </summary>
        public string ThemeColor { get; set; } = "#4f46e5";

        /// <summary>
        /// Explicit flag to denote test mode operation
        /// </summary>
        public bool IsTestMode { get; set; } = true;
    }
}
