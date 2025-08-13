using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OrderService.Infrastructure.Persistence.Idempotency;

public class IdempotencyConfiguration : IEntityTypeConfiguration<IdempotencyEntry>
{
    public void Configure(EntityTypeBuilder<IdempotencyEntry> b)
    {
        b.ToTable("idempotency");
        b.HasKey(x => x.Id);
        b.Property(x => x.Key).IsRequired();
        b.HasIndex(x => x.Key).IsUnique(); // <-- duplicate engellemek için unique
        b.Property(x => x.ResourceId).IsRequired();
        b.Property(x => x.CreatedAt).IsRequired();
    }
}