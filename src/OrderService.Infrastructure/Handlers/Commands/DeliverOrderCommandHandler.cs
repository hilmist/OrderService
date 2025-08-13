using MediatR;
using Microsoft.EntityFrameworkCore;
using OrderService.Application.Commands.DeliverOrder;
using OrderService.Application.Dtos;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Infrastructure.Handlers.Commands;

public sealed class DeliverOrderCommandHandler : IRequestHandler<DeliverOrderCommand, OrderDto?>
{
    private readonly OrderDbContext _db;
    public DeliverOrderCommandHandler(OrderDbContext db) => _db = db;

    public async Task<OrderDto?> Handle(DeliverOrderCommand request, CancellationToken ct)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == request.OrderId, ct);
        if (order is null) return null;

        // Case kuralı: yalnızca Shipped → Delivered
        order.MarkDelivered();

        await _db.SaveChangesAsync(ct);

        return new OrderDto(order.Id, order.CustomerId, order.TotalAmount, order.Status.ToString(), order.CreatedAt);
    }
}