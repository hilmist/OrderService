namespace OrderService.Api.Payments;

public sealed record ProcessPaymentRequest(Guid OrderId, decimal Amount, string Method);
public sealed record ProcessPaymentResponse(Guid PaymentId, string Status);

public sealed record RefundRequest(Guid PaymentId);
public sealed record ValidateRequest(string Method);