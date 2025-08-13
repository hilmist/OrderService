using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderService.Domain.Entities;
using OrderService.Domain.ValueObjects;

namespace OrderService.Infrastructure.Persistence.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.ToTable("order_items");
        b.HasKey(x => x.Id);

        b.Property(x => x.OrderId).IsRequired();
        b.Property(x => x.ProductId).IsRequired();
        b.Property(x => x.Quantity).IsRequired();
        
        b.OwnsOne<Money>(x => x.UnitPrice, nb =>
        {
            nb.Property(p => p.Amount)
                .HasColumnName("unit_price")
                .HasPrecision(18, 2)
                .IsRequired();

            nb.Property(p => p.Currency)
                .HasColumnName("currency")
                .HasMaxLength(3)
                .IsRequired();
        });
    }
}