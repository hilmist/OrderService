using MediatR;
using Microsoft.EntityFrameworkCore;
using OrderService.Application.Abstractions;
using OrderService.Application.Dtos;
using OrderService.Domain.Entities;
using OrderService.Domain.ValueObjects;
using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.Persistence.Idempotency;

namespace OrderService.Infrastructure.Handlers.Commands;

public sealed class CreateOrderCommandHandler
    : IRequestHandler<OrderService.Application.Commands.CreateOrder.CreateOrderCommand, OrderDto>
{
    private readonly OrderDbContext _db;
    private readonly IdempotencyStore _idem;
    private readonly IEventPublisher _bus;

    public CreateOrderCommandHandler(OrderDbContext db, IdempotencyStore idem, IEventPublisher bus)
    {
        _db = db;
        _idem = idem;
        _bus = bus;
    }

    public async Task<OrderDto> Handle(
        OrderService.Application.Commands.CreateOrder.CreateOrderCommand request,
        CancellationToken ct)
    {
        // 1) Domain modelini kur
        var items = request.Items
            .Select(i => new OrderItem(i.ProductId, i.Quantity, new Money(i.UnitPrice, "TRY")))
            .ToList();

        var order = new Order(request.CustomerId, items);

        // 2) Idempotency kontrolü (aynı key ile geldiyse DB’den mevcut order’ı getir; EVENT PUBLISH YAPMA)
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existingId = await _idem.TryInsertAsync(request.IdempotencyKey!, order.Id, ct);

            if (existingId != order.Id)
            {
                var existing = await _db.Orders
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == existingId, ct)
                    ?? throw new InvalidOperationException("Duplicate idempotency key; resource not found (data drift)");

                // Sadece mevcut DTO’yu dön; event publish YOK
                return new OrderDto(existing.Id, existing.CustomerId, existing.TotalAmount, existing.Status.ToString(), existing.CreatedAt);
            }
        }

        // 3) Yeni siparişi persist et 
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct); // commit başarılı → artık publish edilir

        // 4) Sadece YENİ yaratımda event publish et!!!
        await _bus.PublishAsync("order.created", new
        {
            orderId    = order.Id,
            customerId = order.CustomerId,
            total      = order.TotalAmount,
            at         = DateTime.UtcNow,
            items      = order.Items.Select(it => new
            {
                productId = it.ProductId,
                quantity  = it.Quantity,
                unitPrice = it.UnitPrice.Amount
            })
        }, ct);

        
        return new OrderDto(order.Id, order.CustomerId, order.TotalAmount, order.Status.ToString(), order.CreatedAt);
    }
}