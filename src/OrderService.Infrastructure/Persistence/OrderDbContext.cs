using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Persistence.Idempotency;
using OrderService.Domain.ValueObjects;

namespace OrderService.Infrastructure.Persistence;

public class OrderDbContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<IdempotencyEntry> IdempotencyEntries => Set<IdempotencyEntry>();

    public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}