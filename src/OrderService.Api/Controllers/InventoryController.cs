using Microsoft.AspNetCore.Mvc;
using OrderService.Application.Abstractions;

namespace OrderService.Api.Controllers;

[ApiController]
[Route("api/v1/inventory")]
public class InventoryController : ControllerBase
{
    private readonly IInventoryStore _store;
    public InventoryController(IInventoryStore store) => _store = store;

    // ========== PUBLIC ENDPOINTS (case listesi) ==========

    // POST /api/v1/inventory/check-availability
    [HttpPost("check-availability")]
    public async Task<ActionResult<CheckAvailabilityResponse>> CheckAvailability([FromBody] CheckAvailabilityRequest req, CancellationToken ct)
    {
        var dict = await _store.CheckAvailabilityAsync(req.ProductIds, ct);
        return Ok(new CheckAvailabilityResponse(dict));
    }

    // POST /api/v1/inventory/reserve
    [HttpPost("reserve")]
    public async Task<ActionResult<ReserveResponse>> Reserve([FromBody] ReserveRequest req, CancellationToken ct)
    {
        // Not: Bu manuel endpoint, event-driven akıştan bağımsız hızlı test için.
        var ok = await _store.TryReserveAsync(req.ProductId, req.Quantity, req.ReservationId, req.CustomerId, ct);
        return ok ? Ok(new ReserveResponse(true)) : BadRequest(new ReserveResponse(false, "rules violated or not enough stock"));
    }

    // POST /api/v1/inventory/release
    [HttpPost("release")]
    public async Task<IActionResult> Release([FromBody] ReleaseRequest req, CancellationToken ct)
    {
        await _store.ReleaseAsync(req.ReservationId, ct);
        return Ok();
    }

    // GET /api/v1/inventory/products/{productId}/stock
    [HttpGet("products/{productId:guid}/stock")]
    public async Task<ActionResult<int>> GetCurrentStock(Guid productId, CancellationToken ct)
        => Ok(await _store.GetStockAsync(productId, ct));


    // ========== ADMIN / TEST YARDIMCI ENDPOINTLERI ==========

    // POST /api/v1/inventory/admin/bulk-set
    // Body: { "items": { "<productId>": 100, "<productId2>": 50 } }
    [HttpPost("admin/bulk-set")]
    public async Task<IActionResult> AdminBulkSet([FromBody] BulkSetRequest req, CancellationToken ct)
    {
        await _store.BulkSetAsync(req.Items, ct);
        return Ok(new { updated = req.Items.Count });
    }

    // POST /api/v1/inventory/admin/{productId}/set-stock
    // Body: { "quantity": 100 }
    [HttpPost("admin/{productId:guid}/set-stock")]
    public async Task<IActionResult> AdminSetStock(Guid productId, [FromBody] SetStockRequest req, CancellationToken ct)
    {
        await _store.SetStockAsync(productId, req.Quantity, ct);
        return Ok(new { productId, req.Quantity });
    }

    // GET /api/v1/inventory/admin/{productId}/stock  (admin kısa yol)
    [HttpGet("admin/{productId:guid}/stock")]
    public async Task<IActionResult> AdminGetStock(Guid productId, CancellationToken ct)
    {
        var q = await _store.GetStockAsync(productId, ct);
        return Ok(new { productId, quantity = q });
    }

    // POST /api/v1/inventory/admin/flash-set
    // Body: { "ids": ["guid1","guid2"] }
    [HttpPost("admin/flash-set")]
    public IActionResult AdminSetFlash([FromBody] FlashSetRequest req)
    {
        _store.SetFlashSaleProducts(req.Ids);
        return Ok(new { count = req.Ids.Count });
    }

    // POST /api/v1/inventory/admin/release-expired
    [HttpPost("admin/release-expired")]
    public IActionResult AdminReleaseExpired()
    {
        _store.ReleaseExpired();
        return Ok(new { status = "ok" });
    }
    
    // POST /api/v1/inventory/admin/reset-state
    [HttpPost("admin/reset-state")]
    public IActionResult ResetState()
    {
        if (_store is OrderService.Api.Inventory.InMemoryInventoryStore mem)
        {
            mem.AdminResetState();
            return Ok(new { reset = true });
        }
        return BadRequest(new { message = "InMemory store değil" });
    }
}




// ========= Request/Response modelleri =========

public sealed class CheckAvailabilityRequest
{
    public List<Guid> ProductIds { get; set; } = new();
}
public sealed class CheckAvailabilityResponse
{
    public Dictionary<Guid, int> Availability { get; }
    public CheckAvailabilityResponse(Dictionary<Guid, int> availability) => Availability = availability;
}

public sealed class ReserveRequest
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public Guid ReservationId { get; set; } = Guid.NewGuid();
    public Guid? CustomerId { get; set; }
}
public sealed class ReserveResponse
{
    public bool Ok { get; }
    public string? Error { get; }
    public ReserveResponse(bool ok, string? error = null) { Ok = ok; Error = error; }
}

public sealed class ReleaseRequest
{
    public Guid ReservationId { get; set; }
}

public sealed class BulkUpdateRequest 
{
    public Dictionary<Guid,int> Items { get; set; } = new();
}

// Admin modelleri
public sealed class BulkSetRequest
{
    public Dictionary<Guid,int> Items { get; set; } = new();
}
public sealed class SetStockRequest
{
    public int Quantity { get; set; }
}
public sealed class FlashSetRequest
{
    public List<Guid> Ids { get; set; } = new();
}