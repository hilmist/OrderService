namespace OrderService.Api.Payments;

public enum PaymentStatus { Pending, Processed, Failed, Refunded, Unknown }

public sealed class PaymentRecord
{
    public Guid PaymentId { get; init; }
    public Guid OrderId { get; init; }
    public decimal Amount { get; init; }
    public string Method { get; init; } = "CreditCard";
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public string? FailureReason { get; set; }
}

public interface IPaymentStore
{
    Task<PaymentRecord> CreateAsync(Guid orderId, decimal amount, string method, CancellationToken ct = default);
    Task<PaymentRecord?> GetAsync(Guid paymentId, CancellationToken ct = default);
    Task UpdateAsync(PaymentRecord pr, CancellationToken ct = default);
}