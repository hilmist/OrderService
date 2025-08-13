namespace OrderService.Infrastructure.Persistence.Idempotency;

public class IdempotencyEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = default!;
    public Guid ResourceId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}