using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderService.Infrastructure.Persistence.DesignTime;

public sealed class OrderDbContextDesignFactory : IDesignTimeDbContextFactory<OrderDbContext>
{
    public OrderDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(
                Environment.GetEnvironmentVariable("ORDERS_CONN") 
                ?? "Host=localhost;Port=5433;Database=orders;Username=postgres;Password=postgres")
            .Options;

        return new OrderDbContext(options);
    }
}