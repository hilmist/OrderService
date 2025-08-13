using MediatR;
using OrderService.Application.Dtos;

namespace OrderService.Application.Commands.DeliverOrder;

public sealed record DeliverOrderCommand(Guid OrderId) : IRequest<OrderDto?>;