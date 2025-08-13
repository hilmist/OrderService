using MediatR;
using OrderService.Application.Dtos;

namespace OrderService.Application.Queries.GetOrdersByCustomer;

// sadece CustomerId ile bir liste dönüyoruz
public sealed record GetOrdersByCustomerQuery(Guid CustomerId)
    : IRequest<IReadOnlyList<OrderDto>>;