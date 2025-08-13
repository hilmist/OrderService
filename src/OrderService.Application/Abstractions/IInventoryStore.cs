namespace OrderService.Application.Abstractions;

public interface IInventoryStore
{
    Task<bool> TryReserveAsync(Guid productId, int quantity, Guid reservationId, Guid? customerId = null, CancellationToken ct = default);
    void SetFlashSaleProducts(IEnumerable<Guid> ids);
    void ReleaseExpired();
    Task BulkSetAsync(Dictionary<Guid,int> items, CancellationToken ct = default);
    Task<int> GetStockAsync(Guid productId, CancellationToken ct = default);
    Task SetStockAsync(Guid productId, int quantity, CancellationToken ct = default);
    Task<bool> TryReserveAsync(Guid productId, int quantity, Guid reservationId, CancellationToken ct = default);
    Task ReleaseAsync(Guid reservationId, CancellationToken ct = default);
    Task<Dictionary<Guid, int>> CheckAvailabilityAsync(IEnumerable<Guid> productIds, CancellationToken ct = default);
    Task<bool> TryReserveAsync(
        Guid productId,
        int quantity,
        Guid reservationId,
        Guid customerId,
        Guid orderId,
        TimeSpan ttl,
        CancellationToken ct);
    Task ReleaseByOrderAsync(Guid orderId, CancellationToken ct);
}