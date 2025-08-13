using MediatR;
using Microsoft.EntityFrameworkCore;
using OrderService.Application.Abstractions;
using OrderService.Application.Commands.RetryOrder;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Infrastructure.Handlers.Commands;

public class RetryOrderCommandHandler : IRequestHandler<RetryOrderCommand, bool>
{
    private readonly OrderDbContext _db;
    private readonly IEventPublisher _publisher;

    public RetryOrderCommandHandler(OrderDbContext db, IEventPublisher publisher)
    {
        _db = db; _publisher = publisher;
    }

    public async Task<bool> Handle(RetryOrderCommand req, CancellationToken ct)
    {
        var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == req.OrderId, ct);
        if (order is null) return false;

        var status = order.Status.ToString();
        if (status is "Shipped" or "Delivered") return false;

        await _publisher.PublishAsync("order.created", new {
            orderId = order.Id,
            customerId = order.CustomerId,
            total = order.TotalAmount,
            createdAt = order.CreatedAt,
            retry = true,
            retriedAt = DateTime.UtcNow
        }, ct);

        return true;
    }
}