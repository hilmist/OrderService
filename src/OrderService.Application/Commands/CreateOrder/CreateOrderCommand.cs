using MediatR;
using OrderService.Application.Dtos;

namespace OrderService.Application.Commands.CreateOrder;

public sealed record CreateOrderItem(Guid ProductId, int Quantity, decimal UnitPrice);

public sealed record CreateOrderCommand(
    Guid CustomerId,
    IEnumerable<CreateOrderItem> Items,
    string? IdempotencyKey
) : IRequest<OrderDto>;