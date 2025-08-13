using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace OrderService.Infrastructure.Persistence.Idempotency
{
    public sealed class IdempotencyStore
    {
        private readonly OrderDbContext _db;

        public IdempotencyStore(OrderDbContext db) => _db = db;

        /// <summary>
        /// Aynı key ile ilk çağrıda kayıt ekler ve gönderdiğimiz resourceId'i döner.
        /// Key zaten varsa, var olan resourceId'yi döner.
        /// </summary>
        public async Task<Guid> TryInsertAsync(string key, Guid resourceId, CancellationToken ct = default)
        {
            _db.IdempotencyEntries.Add(new IdempotencyEntry
            {
                Key = key,
                ResourceId = resourceId,
                CreatedAt = DateTime.UtcNow
            });

            try
            {
                await _db.SaveChangesAsync(ct);
                // Ekleme başarılı → bu isteğin resourceId'si döner
                return resourceId;
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // key zaten var → mevcut resourceId’i oku ve onu döndür
                var existing = await _db.IdempotencyEntries
                    .AsNoTracking()
                    .Where(x => x.Key == key)
                    .Select(x => x.ResourceId)
                    .SingleAsync(ct);

                return existing;
            }
        }

        private static bool IsUniqueViolation(DbUpdateException ex)
            => ex.InnerException is PostgresException pg && pg.SqlState == "23505"; // unique ihlali
    }
}