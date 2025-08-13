using MediatR;
using OrderService.Application.Dtos;

namespace OrderService.Application.Queries.GetOrderById;

public sealed record GetOrderByIdQuery(Guid OrderId) : IRequest<OrderDto?>;