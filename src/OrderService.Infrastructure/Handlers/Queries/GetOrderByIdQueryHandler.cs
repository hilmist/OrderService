using MediatR;
using Microsoft.EntityFrameworkCore;
using OrderService.Application.Dtos;
using OrderService.Application.Queries.GetOrderById; // Query burada
using OrderService.Infrastructure.Persistence;

namespace OrderService.Infrastructure.Handlers.Queries;

public class GetOrderByIdQueryHandler : IRequestHandler<GetOrderByIdQuery, OrderDto?>
{
    private readonly OrderDbContext _db;
    public GetOrderByIdQueryHandler(OrderDbContext db) => _db = db;

    public async Task<OrderDto?> Handle(GetOrderByIdQuery request, CancellationToken ct)
    {
        var dto = await _db.Orders
            .AsNoTracking()
            .Where(o => o.Id == request.OrderId)
            .Select(o => new OrderDto(
                o.Id,
                o.CustomerId, 
                o.Items.Sum(i => i.UnitPrice.Amount * i.Quantity),
                o.Status.ToString(),
                o.CreatedAt
            ))
            .FirstOrDefaultAsync(ct);

        return dto;
    }
}