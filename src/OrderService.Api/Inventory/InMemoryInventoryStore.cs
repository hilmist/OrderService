using System.Collections.Concurrent;
using OrderService.Application.Abstractions;

namespace OrderService.Api.Inventory
{
    /// <summary>
    /// Thread-safe, in-memory stok & rezervasyon.
    /// Case kuralları:
    /// - TTL: rezervasyonlar süre sonunda ReleaseExpired ile geri bırakılır (10 dk varsayılan)
    /// - Low stock alert: stok < 10 ise console'a uyarı
    /// - %50 kuralı: tek bir order, mevcut stoğun %50'sinden fazlasını rezerve edemez
    /// - Flash sale: işaretli ürünler için müşteri başına max 2 adet
    /// - Idempotency: aynı (orderId, productId) için tekrar mesaj gelse bile stok yeniden düşmez
    /// </summary>
    public sealed class InMemoryInventoryStore : IInventoryStore
    {
        private readonly ConcurrentDictionary<Guid, int> _stock = new();
        // reservationId -> (orderId, productId, qty, expiresAt, customerId)
        private readonly ConcurrentDictionary<Guid, (Guid orderId, Guid productId, int qty, DateTime expiresAt, Guid customerId)> _reservations = new();
        // orderId -> reservationId list
        private readonly ConcurrentDictionary<Guid, ConcurrentBag<Guid>> _orderToReservations = new();
        // flash sale product set
        private readonly ConcurrentDictionary<Guid, byte> _flashSale = new();
        // (customerId, productId) -> toplam rezerve edilen miktar (flash sale limiti için)
        private readonly ConcurrentDictionary<(Guid customerId, Guid productId), int> _customerProductReserved = new();
        // ürün bazlı kilitler
        private readonly ConcurrentDictionary<Guid, object> _locks = new();

        // IDEMPOTENCY: (orderId, productId) -> flag
        private readonly ConcurrentDictionary<(Guid orderId, Guid productId), byte> _idempotentKeys = new();

        private const int LowStockThreshold = 10;
        private const int FlashSalePerCustomerLimit = 2;
        private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

        private object LockOf(Guid productId) => _locks.GetOrAdd(productId, _ => new object());

        private void EnsureProductExists(Guid productId)
            => _stock.AddOrUpdate(productId, 0, (_, cur) => cur);

        private void OnStockDecreased(Guid productId, int newQty)
        {
            if (newQty < LowStockThreshold)
                Console.WriteLine($"[inventory] LOW STOCK alert: product={productId} qty={newQty}");
        }

        private void ReturnStock(Guid productId, int qty)
        {
            var after = _stock.AddOrUpdate(productId, qty, (_, cur) => cur + qty);
            if (after < LowStockThreshold)
                Console.WriteLine($"[inventory] LOW STOCK alert: product={productId} qty={after}");
        }

        private bool IsFlashSale(Guid productId) => _flashSale.ContainsKey(productId);

        private bool CheckFlashSaleLimit(Guid productId, Guid customerId, int wantQty)
        {
            if (customerId == Guid.Empty) return true;
            if (!IsFlashSale(productId)) return true;

            var key = (customerId, productId);
            var cur = _customerProductReserved.GetOrAdd(key, 0);
            return (cur + wantQty) <= FlashSalePerCustomerLimit;
        }

        private void IncreaseCustomerReserved(Guid productId, Guid customerId, int qty)
        {
            if (customerId == Guid.Empty) return;
            if (!IsFlashSale(productId)) return;

            var key = (customerId, productId);
            _customerProductReserved.AddOrUpdate(key, qty, (_, cur) => cur + qty);
        }

        private void DecreaseCustomerReserved(Guid productId, Guid customerId, int qty)
        {
            if (customerId == Guid.Empty) return;
            if (!IsFlashSale(productId)) return;

            var key = (customerId, productId);
            _customerProductReserved.AddOrUpdate(key, 0, (_, cur) =>
            {
                var next = cur - qty;
                return next <= 0 ? 0 : next;
            });
        }

        private void AddReservationIndex(Guid orderId, Guid reservationId)
        {
            if (orderId == Guid.Empty) return;
            var bag = _orderToReservations.GetOrAdd(orderId, _ => new ConcurrentBag<Guid>());
            bag.Add(reservationId);
        }

        // -------- Interface implementations --------

        // Overload: customerId opsiyonel
        public Task<bool> TryReserveAsync(Guid productId, int quantity, Guid reservationId, Guid? customerId = null, CancellationToken ct = default)
            => TryReserveAsync(productId, quantity, reservationId, customerId ?? Guid.Empty, orderId: Guid.Empty, ttl: DefaultTtl, ct);

        // Overload: sadece reservationId
        public Task<bool> TryReserveAsync(Guid productId, int quantity, Guid reservationId, CancellationToken ct = default)
            => TryReserveAsync(productId, quantity, reservationId, customerId: Guid.Empty, orderId: Guid.Empty, ttl: DefaultTtl, ct);

        // (TTL/order/customer + idempotency)
        public Task<bool> TryReserveAsync(Guid productId, int quantity, Guid reservationId, Guid customerId, Guid orderId, TimeSpan ttl, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return Task.FromResult(false);
            if (quantity <= 0) return Task.FromResult(false);

            EnsureProductExists(productId);

            var expiresAt = DateTime.UtcNow.Add(ttl <= TimeSpan.Zero ? DefaultTtl : ttl);
            var ok = false;

            lock (LockOf(productId))
            {
                // IDEMPOTENCY: aynı (orderId, productId) için tekrar mesaj → NO-OP (başarılı say)
                if (orderId != Guid.Empty && _idempotentKeys.ContainsKey((orderId, productId)))
                    return Task.FromResult(true);

                var available = _stock[productId];

                // %50 kuralı (orderId varsa uygula)
                if (orderId != Guid.Empty)
                {
                    var maxAllowed = (int)Math.Floor(available * 0.5);
                    if (maxAllowed < 1) maxAllowed = 1;
                    if (quantity > maxAllowed)
                        return Task.FromResult(false);
                }

                // flash sale limiti
                if (!CheckFlashSaleLimit(productId, customerId, quantity))
                    return Task.FromResult(false);

                // stok yeterli mi
                if (available < quantity)
                    return Task.FromResult(false);

                // Kurallar OK → stok düş + rezervasyon yaz
                _stock[productId] = available - quantity;
                _reservations[reservationId] = (orderId, productId, quantity, expiresAt, customerId);
                AddReservationIndex(orderId, reservationId);
                IncreaseCustomerReserved(productId, customerId, quantity);

                // Idempotency anahtarını işaretle
                if (orderId != Guid.Empty)
                    _idempotentKeys[(orderId, productId)] = 1;

                ok = true;
                OnStockDecreased(productId, _stock[productId]);
            }

            return Task.FromResult(ok);
        }

        public Task ReleaseAsync(Guid reservationId, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested) return Task.CompletedTask;

            if (_reservations.TryRemove(reservationId, out var info))
            {
                lock (LockOf(info.productId))
                {
                    ReturnStock(info.productId, info.qty);
                    DecreaseCustomerReserved(info.productId, info.customerId, info.qty);

                    // idempotency temizliği (retry'lere izin ver)
                    if (info.orderId != Guid.Empty)
                        _idempotentKeys.TryRemove((info.orderId, info.productId), out _);
                }
            }
            return Task.CompletedTask;
        }

        public Task ReleaseByOrderAsync(Guid orderId, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested) return Task.CompletedTask;

            if (!_orderToReservations.TryRemove(orderId, out var bag))
                return Task.CompletedTask;

            foreach (var rid in bag)
            {
                if (_reservations.TryRemove(rid, out var info))
                {
                    lock (LockOf(info.productId))
                    {
                        ReturnStock(info.productId, info.qty);
                        DecreaseCustomerReserved(info.productId, info.customerId, info.qty);

                        // idempotency temizliği
                        if (info.orderId != Guid.Empty)
                            _idempotentKeys.TryRemove((info.orderId, info.productId), out _);
                    }
                }
            }
            return Task.CompletedTask;
        }

        public void SetFlashSaleProducts(IEnumerable<Guid> ids)
        {
            _flashSale.Clear();
            foreach (var id in ids) _flashSale[id] = 1;
        }

        public void ReleaseExpired()
        {
            var now = DateTime.UtcNow;
            foreach (var kv in _reservations.ToArray())
            {
                var rid = kv.Key;
                var (orderId, productId, qty, expiresAt, customerId) = kv.Value;
                if (expiresAt <= now)
                {
                    if (_reservations.TryRemove(rid, out var info))
                    {
                        lock (LockOf(info.productId))
                        {
                            ReturnStock(info.productId, info.qty);
                            DecreaseCustomerReserved(info.productId, info.customerId, info.qty);

                            // idempotency temizliği
                            if (info.orderId != Guid.Empty)
                                _idempotentKeys.TryRemove((info.orderId, info.productId), out _);
                        }
                    }
                }
            }
        }

        public Task BulkSetAsync(Dictionary<Guid, int> items, CancellationToken ct = default)
        {
            foreach (var (pid, qty) in items)
            {
                var q = Math.Max(0, qty);
                _stock.AddOrUpdate(pid, q, (_, __) => q);
                if (q < LowStockThreshold)
                    Console.WriteLine($"[inventory] LOW STOCK alert: product={pid} qty={q}");
            }
            return Task.CompletedTask;
        }

        public Task<int> GetStockAsync(Guid productId, CancellationToken ct = default)
        {
            EnsureProductExists(productId);
            return Task.FromResult(_stock[productId]);
        }

        public Task SetStockAsync(Guid productId, int quantity, CancellationToken ct = default)
        {
            var q = Math.Max(0, quantity);
            _stock.AddOrUpdate(productId, q, (_, __) => q);
            if (q < LowStockThreshold)
                Console.WriteLine($"[inventory] LOW STOCK alert: product={productId} qty={q}");
            return Task.CompletedTask;
        }

        public Task<Dictionary<Guid, int>> CheckAvailabilityAsync(IEnumerable<Guid> productIds, CancellationToken ct = default)
        {
            var result = new Dictionary<Guid, int>();
            foreach (var pid in productIds)
            {
                EnsureProductExists(pid);
                result[pid] = _stock[pid];
            }
            return Task.FromResult(result);
        }
        public void AdminResetState()
        {
            _reservations.Clear();
            _orderToReservations.Clear();
            _customerProductReserved.Clear();
            _idempotentKeys.Clear();
        }
    }
    
    
}