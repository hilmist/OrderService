using MediatR;

namespace OrderService.Application.Commands.CancelOrder;

public sealed record CancelOrderCommand(Guid OrderId, string? Reason = null) : IRequest<bool>;