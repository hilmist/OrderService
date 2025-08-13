using Microsoft.AspNetCore.Mvc;
using MediatR;
using Microsoft.EntityFrameworkCore;
using OrderService.Infrastructure.Persistence;
using OrderService.Application.Abstractions;
using OrderService.Api.Observability;
using OrderService.Application.Commands.CreateOrder;   // CreateOrderCommand
using OrderService.Application.Dtos;                  // OrderDto

namespace OrderService.Api.Controllers;

[ApiController]
[Route("api/v1/orders")]
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly OrderDbContext _db;
    private readonly IEventPublisher _bus;
    private readonly IMetricsRegistry _metrics;

    public OrdersController(
        IMediator mediator,
        OrderDbContext db,
        IEventPublisher bus,
        IMetricsRegistry metrics)
    {
        _mediator = mediator;
        _db = db;
        _bus = bus;
        _metrics = metrics;
    }

    // =============== CREATE ===============
    // POST /api/v1/orders
    [HttpPost]
    public async Task<ActionResult<OrderDto>> Create(
        [FromBody] CreateOrderCommand body,
        [FromHeader(Name = "Idempotency-Key")] string? idemKey,
        CancellationToken ct)
    {
        
        var cmd = body with
        {
            IdempotencyKey = string.IsNullOrWhiteSpace(idemKey) ? body.IdempotencyKey : idemKey
        };

        // Handler: (1) idempotency kontrolünü tek transaction içinde yapar
        //          (2) ilk kez oluşturduysa save + commit sonra 'order.created' event’i publish eder
        //          (3) tekrar/yeniden gönderimde sadece mevcut DTO'yu döner (publish etmez)
        var dto = await _mediator.Send(cmd, ct);

        
        return Ok(dto);
    }

    // =============== READ ===============
    // GET /api/v1/orders/{orderId}
    [HttpGet("{orderId:guid}")]
    public async Task<ActionResult<OrderDto?>> GetById(Guid orderId, CancellationToken ct)
    {
        var o = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == orderId, ct);
        if (o is null) return NotFound();

        var dto = new OrderDto(o.Id, o.CustomerId, o.TotalAmount, o.Status.ToString(), o.CreatedAt);
        return Ok(dto);
    }

    // GET /api/v1/orders/customer/{customerId}
    [HttpGet("customer/{customerId:guid}")]
    public async Task<ActionResult<List<OrderDto>>> GetByCustomer(Guid customerId, CancellationToken ct)
    {
        var list = await _db.Orders.AsNoTracking()
            .Where(x => x.CustomerId == customerId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(o => new OrderDto(o.Id, o.CustomerId, o.TotalAmount, o.Status.ToString(), o.CreatedAt))
            .ToListAsync(ct);

        return Ok(list);
    }

    // =============== CANCEL ===============
    // PUT /api/v1/orders/{orderId}/cancel
    [HttpPut("{orderId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid orderId, [FromQuery] string? reason, CancellationToken ct)
    {
        try
        {
            var dto = await _mediator.Send(
                new OrderService.Infrastructure.Handlers.Commands.CancelOrderCommand(orderId, reason ?? "user_request"),
                ct);
            if (dto is null) return NotFound();

            await _bus.PublishAsync("order.cancelled",
                new { orderId, reason = reason ?? "user_request", at = DateTime.UtcNow }, ct);

            return Ok(dto);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("cancellation window", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new { message = ex.Message });
        }
    }

    // =============== SHIP ===============
    // PUT /api/v1/orders/{orderId}/ship
    [HttpPut("{orderId:guid}/ship")]
    public async Task<IActionResult> Ship(Guid orderId, CancellationToken ct)
    {
        var dto = await _mediator.Send(new OrderService.Application.Commands.ShipOrder.ShipOrderCommand(orderId), ct);
        if (dto is null) return NotFound();

        await _bus.PublishAsync("order.shipped", new { orderId, at = DateTime.UtcNow }, ct);
        return Ok(dto);
    }

    // =============== DELIVER ===============
    // PUT /api/v1/orders/{orderId}/deliver
    [HttpPut("{orderId:guid}/deliver")]
    public async Task<IActionResult> Deliver(Guid orderId, CancellationToken ct)
    {
        var dto = await _mediator.Send(new OrderService.Application.Commands.DeliverOrder.DeliverOrderCommand(orderId), ct);
        if (dto is null) return NotFound();

        await _bus.PublishAsync("order.delivered", new { orderId, at = DateTime.UtcNow }, ct);
        return Ok(dto);
    }

    // =============== RETRY ===============
    // POST /api/v1/orders/{orderId}/retry
    [HttpPost("{orderId:guid}/retry")]
    public async Task<IActionResult> Retry(Guid orderId, CancellationToken ct)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order is null) return NotFound();

        // Confirmed/Shipped/Delivered ise tekrar tetikleme
        var s = order.Status.ToString();
        if (s.Equals("Confirmed", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Shipped", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Delivered", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new { message = "Order is already processed; retry is not allowed." });
        }

        var payload = new
        {
            orderId = order.Id,
            customerId = order.CustomerId,
            total = order.TotalAmount,
            at = DateTime.UtcNow,
            items = order.Items.Select(it => new
            {
                productId = it.ProductId,
                quantity = it.Quantity,
                unitPrice = it.UnitPrice
            }).ToList()
        };

        await _bus.PublishAsync("order.created", payload, ct);
        return Accepted(new { orderId = order.Id, message = "Retry triggered" });
    }
}