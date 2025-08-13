using MediatR;
using Microsoft.EntityFrameworkCore;
using OrderService.Application.Dtos;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Infrastructure.Handlers.Commands;

public sealed record CancelOrderCommand(Guid OrderId, string Reason) : IRequest<OrderDto?>;

public sealed class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, OrderDto?>
{
    private readonly OrderDbContext _db;

    public CancelOrderCommandHandler(OrderDbContext db)
    {
        _db = db;
    }

    public async Task<OrderDto?> Handle(CancelOrderCommand request, CancellationToken ct)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == request.OrderId, ct);
        if (order is null) return null;

        // Case kuralı: "Order cancellation allowed within 2 hours"
        if (DateTime.UtcNow - order.CreatedAt > TimeSpan.FromHours(2))
            throw new InvalidOperationException("Cancellation window has expired (allowed within 2 hours).");

        order.Cancel(request.Reason);
        await _db.SaveChangesAsync(ct);

        return new OrderDto(order.Id, order.CustomerId, order.TotalAmount, order.Status.ToString(), order.CreatedAt);
    }
}