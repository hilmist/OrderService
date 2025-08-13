using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderService.Domain.Entities;

namespace OrderService.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("orders");
        b.HasKey(x => x.Id);

        // Temel alanlar
        b.Property(x => x.CustomerId).IsRequired();
        b.Property(x => x.Status).HasConversion<int>().IsRequired();

        b.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        b.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at");
        b.Property(x => x.CancelledAt).HasColumnName("cancelled_at");
        b.Property(x => x.ShippedAt).HasColumnName("shipped_at");
        b.Property(x => x.DeliveredAt).HasColumnName("delivered_at");

        b.Property(x => x.CancelReason)
            .HasColumnName("cancel_reason")
            .HasMaxLength(200);

        // Concurrency token (rowversion)
        b.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        // Toplam tutar — DB'de saklanır
        b.Property(x => x.TotalAmount)
            .HasColumnName("total_amount")
            .HasPrecision(18, 2)
            .IsRequired();

        // İlişki: Order -> OrderItems
        b.HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey(i => i.OrderId)              
            .OnDelete(DeleteBehavior.Cascade);

        // Backing field ve erişim modu (field-based access)
        b.Navigation(o => o.Items)
            .HasField("_items")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        
        b.HasIndex(x => x.CustomerId).HasDatabaseName("ix_orders_customer_id");
        b.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_orders_created_at");
    }
}