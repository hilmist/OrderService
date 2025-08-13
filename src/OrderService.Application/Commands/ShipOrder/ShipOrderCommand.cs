using MediatR;
using OrderService.Application.Dtos;

namespace OrderService.Application.Commands.ShipOrder;

public sealed record ShipOrderCommand(Guid OrderId) : IRequest<OrderDto?>;