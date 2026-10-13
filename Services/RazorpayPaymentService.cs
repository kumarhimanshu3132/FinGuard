using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FinGuard.Data;
using FinGuard.Models;
using Razorpay.Api;

namespace FinGuard.Services
{
    public class RazorpayPaymentService : IRazorpayPaymentService
    {
        private readonly ApplicationDbContext _context;
        private readonly RazorpaySettings _settings;
        private readonly ILogger<RazorpayPaymentService> _logger;

        public RazorpayPaymentService(
            ApplicationDbContext context,
            IOptions<RazorpaySettings> options,
            ILogger<RazorpayPaymentService> logger)
        {
            _context = context;
            _settings = options.Value;
            _logger = logger;
        }

        public RazorpaySettings GetPublicSettings()
        {
            // Return public information ONLY - NEVER expose KeySecret or WebhookSecret
            return new RazorpaySettings
            {
                KeyId = _settings.KeyId,
                KeySecret = string.Empty,
                WebhookSecret = string.Empty,
                Currency = _settings.Currency,
                CompanyName = _settings.CompanyName,
                ThemeColor = _settings.ThemeColor,
                IsTestMode = true
            };
        }

        public async Task<CreateOrderResponse> CreateOrderAsync(int customerId, CreateOrderRequest request)
        {
            if (request.Amount <= 0)
            {
                return new CreateOrderResponse
                {
                    Success = false,
                    Message = "Payment amount must be greater than zero."
                };
            }

            var customer = await _context.Customers.FindAsync(customerId);
            if (customer == null)
            {
                return new CreateOrderResponse
                {
                    Success = false,
                    Message = "Borrower profile not found."
                };
            }

            var loan = await _context.Loans
                .FirstOrDefaultAsync(l => l.LoanId == request.LoanId && l.CustomerId == customerId);

            if (loan == null)
            {
                return new CreateOrderResponse
                {
                    Success = false,
                    Message = "Loan facility not found or does not belong to your account."
                };
            }

            if (loan.Status != "Active" || loan.RemainingBalance <= 0)
            {
                return new CreateOrderResponse
                {
                    Success = false,
                    Message = "This loan facility is not active or has already been fully paid and closed."
                };
            }

            // Cap amount to remaining balance to prevent overpayment
            decimal payableAmount = Math.Min(request.Amount, loan.RemainingBalance);
            long amountInPaise = (long)Math.Round(payableAmount * 100m, MidpointRounding.AwayFromZero);

            string receipt = $"RCPT_LN{loan.LoanId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            string orderId = string.Empty;

            bool isRealCredentials = !string.IsNullOrWhiteSpace(_settings.KeyId) &&
                                     !string.IsNullOrWhiteSpace(_settings.KeySecret) &&
                                     !_settings.KeyId.Contains("YourTestKeyId", StringComparison.OrdinalIgnoreCase);

            if (isRealCredentials)
            {
                try
                {
                    var client = new RazorpayClient(_settings.KeyId, _settings.KeySecret);
                    var orderInput = new Dictionary<string, object>
                    {
                        { "amount", amountInPaise },
                        { "currency", _settings.Currency ?? "INR" },
                        { "receipt", receipt },
                        { "notes", new Dictionary<string, string>
                            {
                                { "loanId", loan.LoanId.ToString() },
                                { "customerId", customerId.ToString() },
                                { "isTestMode", "true" }
                            }
                        }
                    };

                    Order rzpOrder = client.Order.Create(orderInput);
                    orderId = rzpOrder["id"]?.ToString() ?? string.Empty;

                    _logger.LogInformation("Created live Razorpay test order {OrderId} for Loan #{LoanId}, Amount {AmountPaise} paise",
                        orderId, loan.LoanId, amountInPaise);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Razorpay API call failed with configured keys. Falling back to local test sandbox simulation.");
                    orderId = $"order_test_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{Guid.NewGuid().ToString("N")[..8]}";
                }
            }
            else
            {
                // Local Test Sandbox Mode (when user has not yet configured actual rzp_test keys)
                orderId = $"order_test_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{Guid.NewGuid().ToString("N")[..8]}";
                _logger.LogInformation("Generated sandbox Razorpay test order {OrderId} for Loan #{LoanId}", orderId, loan.LoanId);
            }

            // Record order in database
            var orderRecord = new RazorpayOrderRecord
            {
                CustomerId = customerId,
                LoanId = loan.LoanId,
                RazorpayOrderId = orderId,
                AmountInPaise = amountInPaise,
                Amount = payableAmount,
                Currency = _settings.Currency ?? "INR",
                Receipt = receipt,
                Status = "Created",
                IsTestMode = true,
                CreatedAtUtc = DateTime.UtcNow
            };

            _context.RazorpayOrders.Add(orderRecord);
            await _context.SaveChangesAsync();

            return new CreateOrderResponse
            {
                Success = true,
                OrderId = orderId,
                AmountInPaise = amountInPaise,
                Amount = payableAmount,
                Currency = _settings.Currency ?? "INR",
                KeyId = _settings.KeyId,
                CompanyName = _settings.CompanyName,
                ThemeColor = _settings.ThemeColor,
                BorrowerName = customer.Name ?? "Valued Borrower",
                BorrowerEmail = customer.Email ?? string.Empty,
                BorrowerPhone = customer.Phone ?? string.Empty,
                IsTestMode = true
            };
        }

        public bool VerifyPaymentSignature(string orderId, string paymentId, string signature)
        {
            if (string.IsNullOrWhiteSpace(orderId) ||
                string.IsNullOrWhiteSpace(paymentId) ||
                string.IsNullOrWhiteSpace(signature))
            {
                return false;
            }

            // In simulated sandbox fallback mode (dummy keys):
            if (string.IsNullOrWhiteSpace(_settings.KeySecret) ||
                _settings.KeySecret.Contains("YourTestKeySecret", StringComparison.OrdinalIgnoreCase))
            {
                if (signature.StartsWith("simulated_test_sig_", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // Official Razorpay HMAC-SHA256 Signature Algorithm:
            // payload = order_id + "|" + payment_id
            try
            {
                string payload = $"{orderId}|{paymentId}";
                byte[] secretBytes = Encoding.UTF8.GetBytes(_settings.KeySecret ?? string.Empty);
                using var hmac = new HMACSHA256(secretBytes);
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                string expectedHex = Convert.ToHexString(hash).ToLowerInvariant();

                byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedHex);
                byte[] receivedBytes = Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant());

                // Constant-time comparison to prevent timing attacks
                return CryptographicOperations.FixedTimeEquals(expectedBytes, receivedBytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying payment signature for OrderId {OrderId}", orderId);
                return false;
            }
        }

        public bool VerifyWebhookSignature(string rawBody, string signature)
        {
            if (string.IsNullOrWhiteSpace(rawBody) || string.IsNullOrWhiteSpace(signature))
            {
                return false;
            }

            string webhookSecret = _settings.WebhookSecret;
            if (string.IsNullOrWhiteSpace(webhookSecret))
            {
                _logger.LogWarning("Razorpay WebhookSecret is not configured. Webhook rejected.");
                return false;
            }

            try
            {
                byte[] secretBytes = Encoding.UTF8.GetBytes(webhookSecret);
                using var hmac = new HMACSHA256(secretBytes);
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
                string expectedHex = Convert.ToHexString(hash).ToLowerInvariant();

                byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedHex);
                byte[] receivedBytes = Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant());

                return CryptographicOperations.FixedTimeEquals(expectedBytes, receivedBytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying Razorpay webhook signature.");
                return false;
            }
        }

        public async Task<PaymentVerificationResponse> ProcessSuccessfulPaymentAsync(
            int customerId, PaymentVerificationRequest request)
        {
            // 1. Idempotency Check: Check if this paymentId was already processed
            var existingRepayment = await _context.Repayments
                .FirstOrDefaultAsync(r => r.RazorpayPaymentId == request.RazorpayPaymentId);

            if (existingRepayment != null)
            {
                _logger.LogInformation("Idempotent hit: PaymentId {PaymentId} already processed as Repayment #{RepaymentId}",
                    request.RazorpayPaymentId, existingRepayment.RepaymentId);

                var existingLoan = await _context.Loans.FindAsync(existingRepayment.LoanId);
                return new PaymentVerificationResponse
                {
                    Success = true,
                    Message = "Payment already processed successfully (Idempotent).",
                    RepaymentId = existingRepayment.RepaymentId,
                    PaymentId = existingRepayment.RazorpayPaymentId,
                    OrderId = existingRepayment.RazorpayOrderId,
                    AmountPaid = existingRepayment.AmountPaid,
                    RemainingBalance = existingLoan?.RemainingBalance ?? 0,
                    LoanStatus = existingLoan?.Status ?? "Active"
                };
            }

            // 2. Cryptographic signature check
            bool isValid = VerifyPaymentSignature(
                request.RazorpayOrderId, request.RazorpayPaymentId, request.RazorpaySignature);

            if (!isValid)
            {
                _logger.LogWarning("Signature verification failed for OrderId {OrderId}, PaymentId {PaymentId}",
                    request.RazorpayOrderId, request.RazorpayPaymentId);

                return new PaymentVerificationResponse
                {
                    Success = false,
                    Message = "Payment signature verification failed. Untrusted transaction response."
                };
            }

            // 3. Order & Loan validation
            var orderRecord = await _context.RazorpayOrders
                .FirstOrDefaultAsync(o => o.RazorpayOrderId == request.RazorpayOrderId && o.CustomerId == customerId);

            if (orderRecord == null)
            {
                return new PaymentVerificationResponse
                {
                    Success = false,
                    Message = "No corresponding order record found for this transaction."
                };
            }

            var loan = await _context.Loans
                .FirstOrDefaultAsync(l => l.LoanId == orderRecord.LoanId && l.CustomerId == customerId);

            if (loan == null)
            {
                return new PaymentVerificationResponse
                {
                    Success = false,
                    Message = "Target loan account does not exist or access is forbidden."
                };
            }

            // 4. Atomic Database Transaction to guarantee ledger consistency
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                decimal amountToCredit = Math.Min(orderRecord.Amount, loan.RemainingBalance);
                if (amountToCredit <= 0)
                {
                    amountToCredit = orderRecord.Amount; // In case of final settlement check
                }

                int nextRepId = (await _context.Repayments.MaxAsync(r => (int?)r.RepaymentId) ?? 1000) + 1;

                var repayment = new Repayment
                {
                    RepaymentId = nextRepId,
                    LoanId = loan.LoanId,
                    AmountPaid = amountToCredit,
                    PaymentDate = DateTime.UtcNow,
                    CollectedBy = $"Borrower (Razorpay Test Mode)",
                    RazorpayOrderId = request.RazorpayOrderId,
                    RazorpayPaymentId = request.RazorpayPaymentId,
                    RazorpaySignature = request.RazorpaySignature,
                    PaymentStatus = "Success",
                    PaymentMethod = string.IsNullOrWhiteSpace(request.PaymentMethod) ? "Razorpay_Test" : request.PaymentMethod,
                    IsTestMode = true
                };

                _context.Repayments.Add(repayment);

                // Update Loan balance and status
                loan.RemainingBalance = Math.Max(0, loan.RemainingBalance - amountToCredit);
                loan.NextDueDate = loan.NextDueDate.AddMonths(1);

                if (loan.RemainingBalance <= 0)
                {
                    loan.RemainingBalance = 0;
                    loan.Status = "Closed";

                    var customer = await _context.Customers.FindAsync(customerId);
                    if (customer != null)
                    {
                        customer.CreditStatus = "Eligible";
                        _context.Customers.Update(customer);
                    }
                }

                _context.Loans.Update(loan);

                // Update Order Record
                orderRecord.Status = "Paid";
                orderRecord.RazorpayPaymentId = request.RazorpayPaymentId;
                orderRecord.CompletedAtUtc = DateTime.UtcNow;
                _context.RazorpayOrders.Update(orderRecord);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Recorded verified repayment #{RepaymentId} for Loan #{LoanId}, Amount ₹{Amount}",
                    repayment.RepaymentId, loan.LoanId, amountToCredit);

                return new PaymentVerificationResponse
                {
                    Success = true,
                    Message = $"EMI of ₹{amountToCredit:N2} paid successfully via Razorpay Test Mode!",
                    RepaymentId = repayment.RepaymentId,
                    PaymentId = repayment.RazorpayPaymentId,
                    OrderId = repayment.RazorpayOrderId,
                    AmountPaid = amountToCredit,
                    RemainingBalance = loan.RemainingBalance,
                    LoanStatus = loan.Status
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error processing verified payment ledger for OrderId {OrderId}", request.RazorpayOrderId);

                return new PaymentVerificationResponse
                {
                    Success = false,
                    Message = "An error occurred while updating the institutional repayment ledger."
                };
            }
        }

        public async Task<(bool Success, string Message)> ProcessWebhookPaymentAsync(string rawBody, string signature)
        {
            if (!VerifyWebhookSignature(rawBody, signature))
            {
                return (false, "Invalid webhook signature.");
            }

            try
            {
                using var doc = JsonDocument.Parse(rawBody);
                var root = doc.RootElement;
                string? eventType = root.GetProperty("event").GetString();

                if (eventType != "payment.captured" && eventType != "order.paid")
                {
                    return (true, $"Ignored event: {eventType}");
                }

                var payload = root.GetProperty("payload");
                var paymentObj = payload.GetProperty("payment").GetProperty("entity");

                string? paymentId = paymentObj.GetProperty("id").GetString();
                string? orderId = paymentObj.GetProperty("order_id").GetString();
                string? method = paymentObj.TryGetProperty("method", out var m) ? m.GetString() : "Razorpay_Webhook";

                if (string.IsNullOrEmpty(paymentId) || string.IsNullOrEmpty(orderId))
                {
                    return (false, "Missing payment or order ID in webhook payload.");
                }

                // Check idempotency
                var exists = await _context.Repayments.AnyAsync(r => r.RazorpayPaymentId == paymentId);
                if (exists)
                {
                    return (true, "Payment already recorded (Idempotent).");
                }

                var orderRecord = await _context.RazorpayOrders.FirstOrDefaultAsync(o => o.RazorpayOrderId == orderId);
                if (orderRecord == null)
                {
                    return (false, $"Order {orderId} not found in database.");
                }

                var req = new PaymentVerificationRequest
                {
                    LoanId = orderRecord.LoanId,
                    RazorpayOrderId = orderId,
                    RazorpayPaymentId = paymentId,
                    RazorpaySignature = signature, // Webhook signature verified
                    PaymentMethod = method
                };

                var res = await ProcessSuccessfulPaymentAsync(orderRecord.CustomerId, req);
                return (res.Success, res.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing or processing Razorpay webhook.");
                return (false, "Webhook processing exception.");
            }
        }
    }
}
