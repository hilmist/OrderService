using MediatR;
using Microsoft.EntityFrameworkCore;
using OrderService.Application.Dtos;
using OrderService.Application.Queries.GetOrdersByCustomer;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Infrastructure.Handlers.Queries;

public class GetOrdersByCustomerQueryHandler
    : IRequestHandler<GetOrdersByCustomerQuery, IReadOnlyList<OrderDto>>
{
    private readonly OrderDbContext _db;
    public GetOrdersByCustomerQueryHandler(OrderDbContext db) => _db = db;

    public async Task<IReadOnlyList<OrderDto>> Handle(GetOrdersByCustomerQuery req, CancellationToken ct)
    {
        
        var items = await _db.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == req.CustomerId)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new OrderDto(
                o.Id,
                o.CustomerId,
                decimal.Round(
                    o.Items.Sum(i => i.UnitPrice.Amount * i.Quantity),
                    2,
                    MidpointRounding.AwayFromZero
                ),
                o.Status.ToString(),
                o.CreatedAt
            ))
            .ToListAsync(ct);

        return items;
    }
}