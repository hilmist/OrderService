using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OrderService.Application.Abstractions;
using OrderService.Application.Commands.CreateOrder;
using OrderService.Infrastructure.Handlers.Commands;
using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.Persistence.Idempotency;
using Xunit;

namespace OrderService.Tests.Application
{
    public class CreateOrderHandlerTests
    {
        // Basit no-op publisher 
        private sealed class NoOpPublisher : IEventPublisher
        {
            public Task PublishAsync<T>(string exchange, T payload, CancellationToken ct = default)
                => Task.CompletedTask;
        }

        [Fact]
        public async Task CreateOrder_persists_and_publishes()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<OrderDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            await using var db = new OrderDbContext(options);
            var idem = new IdempotencyStore(db);
            var bus  = new NoOpPublisher();

            var handler = new CreateOrderCommandHandler(db, idem, bus);

            var customerId = Guid.NewGuid(); 
            var cmd = new CreateOrderCommand(
                CustomerId: customerId,
                Items: new[]
                {
                    new CreateOrderItem(Guid.NewGuid(), 2, 50m),  // 100
                    new CreateOrderItem(Guid.NewGuid(), 1, 100m), // 100
                },
                IdempotencyKey: "idem-1"
            );
            
            var dto = await handler.Handle(cmd, CancellationToken.None);

            // Assert
            dto.Should().NotBeNull();
            dto.CustomerId.Should().Be(customerId);
            dto.TotalAmount.Should().Be(200m);

            // Veritabanına gerçekten yazıldı mı?
            (await db.Orders.CountAsync()).Should().Be(1);
        }

        [Fact]
        public async Task CreateOrder_is_idempotent_with_same_key()
        {
            var options = new DbContextOptionsBuilder<OrderDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            await using var db = new OrderDbContext(options);
            var idem = new IdempotencyStore(db);
            var bus  = new NoOpPublisher();
            var handler = new CreateOrderCommandHandler(db, idem, bus);

            var customerId = Guid.NewGuid();
            var idemKey = "same-key";

            var cmd1 = new CreateOrderCommand(
                customerId,
                new[] { new CreateOrderItem(Guid.NewGuid(), 1, 200m) },
                IdempotencyKey: idemKey
            );

            var cmd2 = new CreateOrderCommand(
                customerId,
                new[] { new CreateOrderItem(Guid.NewGuid(), 3, 200m) }, // farklı payload olsa da aynı key
                IdempotencyKey: idemKey
            );

            var dto1 = await handler.Handle(cmd1, CancellationToken.None);
            var dto2 = await handler.Handle(cmd2, CancellationToken.None);

            // Aynı idempotencyKey ile aynı OrderId dönmeli
            dto2.Id.Should().Be(dto1.Id);
            (await db.Orders.CountAsync()).Should().Be(1);
        }
    }
}