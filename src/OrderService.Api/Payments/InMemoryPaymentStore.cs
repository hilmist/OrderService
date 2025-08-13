using System.Collections.Concurrent;

namespace OrderService.Api.Payments;

public sealed class InMemoryPaymentStore : IPaymentStore
{
    private readonly ConcurrentDictionary<Guid, PaymentRecord> _db = new();

    public Task<PaymentRecord> CreateAsync(Guid orderId, decimal amount, string method, CancellationToken ct = default)
    {
        var pr = new PaymentRecord { PaymentId = Guid.NewGuid(), OrderId = orderId, Amount = amount, Method = method };
        _db[pr.PaymentId] = pr;
        return Task.FromResult(pr);
    }

    public Task<PaymentRecord?> GetAsync(Guid paymentId, CancellationToken ct = default)
        => Task.FromResult(_db.TryGetValue(paymentId, out var pr) ? pr : null);

    public Task UpdateAsync(PaymentRecord pr, CancellationToken ct = default)
    { _db[pr.PaymentId] = pr; return Task.CompletedTask; }
}