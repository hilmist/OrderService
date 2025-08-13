using Polly;

namespace OrderService.Api.Payments;

public sealed class MockPaymentProcessor : IPaymentProcessor
{
    private readonly Random _rnd = new();

    public async Task<(bool success, bool timeout, string? reason)> ProcessAsync(Guid orderId, decimal amount, string method, CancellationToken ct)
    {
        //  fraud kuralı: > 10.000 TL ise ek doğrulama gerekiyor (başarısız)
        if (amount > 10_000m) return (false, false, "fraud_verification_required");

        var policy = Policy.Handle<TimeoutException>()
            .WaitAndRetryAsync(3, i => TimeSpan.FromMilliseconds(150 * Math.Pow(2, i - 1) + _rnd.Next(0, 100)));

        try
        {
            await policy.ExecuteAsync(async () =>
            {
                var p = _rnd.Next(1, 101);
                if (p <= 5)  throw new Exception("processor_failure");
                if (p <= 15) throw new TimeoutException("processor_timeout");
                await Task.CompletedTask;
            });
            return (true, false, null);
        }
        catch (TimeoutException)
        {
            return (false, true, "timeout_after_retries");
        }
        catch (Exception ex)
        {
            return (false, false, ex.Message);
        }
    }

    public Task<bool> RefundAsync(Guid paymentId, CancellationToken ct) => Task.FromResult(true);
    public Task<bool> ValidateMethodAsync(string method, CancellationToken ct) => Task.FromResult(!string.IsNullOrWhiteSpace(method));
}