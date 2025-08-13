namespace OrderService.Application.Dtos;

public sealed record OrderDto(
        Guid Id,
        Guid CustomerId,
        decimal TotalAmount,
        string Status,
        DateTime CreatedAt
    );