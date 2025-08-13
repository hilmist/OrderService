namespace OrderService.Api.Payments;

public interface IPaymentProcessor
{
    // case: %85 success, %10 timeout (retry), %5 failure; >10.000 TL fraud extra check
    Task<(bool success, bool timeout, string? reason)> ProcessAsync(Guid orderId, decimal amount, string method, CancellationToken ct);
    Task<bool> RefundAsync(Guid paymentId, CancellationToken ct);
    Task<bool> ValidateMethodAsync(string method, CancellationToken ct);
}