using Microsoft.AspNetCore.Mvc;
using OrderService.Api.Payments;
using OrderService.Application.Abstractions;

namespace OrderService.Api.Controllers;

[ApiController]
[Route("api/v1/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentStore _store;
    private readonly IPaymentProcessor _processor;
    private readonly IEventPublisher _bus;

    public PaymentsController(IPaymentStore store, IPaymentProcessor processor, IEventPublisher bus)
    { _store = store; _processor = processor; _bus = bus; }

    // POST /api/v1/payments/process
    [HttpPost("process")]
    public async Task<ActionResult<ProcessPaymentResponse>> Process([FromBody] ProcessPaymentRequest req, CancellationToken ct)
    {
        var pr = await _store.CreateAsync(req.OrderId, req.Amount, req.Method, ct);

        var (success, timeout, reason) = await _processor.ProcessAsync(req.OrderId, req.Amount, req.Method, ct);
        if (success)
        {
            pr.Status = PaymentStatus.Processed; await _store.UpdateAsync(pr, ct);
            await _bus.PublishAsync("payment.processed", new { orderId = req.OrderId, paidAt = DateTime.UtcNow }, ct);
            return Ok(new ProcessPaymentResponse(pr.PaymentId, pr.Status.ToString()));
        }
        if (timeout)
        {
            pr.Status = PaymentStatus.Failed; pr.FailureReason = "timeout"; await _store.UpdateAsync(pr, ct);
            await _bus.PublishAsync("payment.failed", new { orderId = req.OrderId, reason = "timeout" }, ct);
            return StatusCode(504, new ProcessPaymentResponse(pr.PaymentId, pr.Status.ToString()));
        }
        // failure
        pr.Status = PaymentStatus.Failed; pr.FailureReason = reason; await _store.UpdateAsync(pr, ct);
        await _bus.PublishAsync("payment.failed", new { orderId = req.OrderId, reason = reason ?? "unknown" }, ct);
        return BadRequest(new ProcessPaymentResponse(pr.PaymentId, pr.Status.ToString()));
    }

    // POST /api/v1/payments/refund
    [HttpPost("refund")]
    public async Task<IActionResult> Refund([FromBody] RefundRequest req, CancellationToken ct)
    {
        var pr = await _store.GetAsync(req.PaymentId, ct);
        if (pr is null) return NotFound();

        var ok = await _processor.RefundAsync(req.PaymentId, ct);
        if (!ok) return BadRequest("refund_failed");

        pr.Status = PaymentStatus.Refunded;
        await _store.UpdateAsync(pr, ct);
        return Ok();
    }

    // GET /api/v1/payments/{paymentId}
    [HttpGet("{paymentId:guid}")]
    public async Task<ActionResult<PaymentRecord>> Get(Guid paymentId, CancellationToken ct)
    {
        var pr = await _store.GetAsync(paymentId, ct);
        return pr is null ? NotFound() : Ok(pr);
    }

    // POST /api/v1/payments/validate
    [HttpPost("validate")]
    public async Task<IActionResult> Validate([FromBody] ValidateRequest req, CancellationToken ct)
    {
        var ok = await _processor.ValidateMethodAsync(req.Method, ct);
        return ok ? Ok() : BadRequest("invalid_method");
    }
}