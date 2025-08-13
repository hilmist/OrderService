using MediatR;

namespace OrderService.Application.Commands.RetryOrder;

public sealed record RetryOrderCommand(Guid OrderId) : IRequest<bool>;