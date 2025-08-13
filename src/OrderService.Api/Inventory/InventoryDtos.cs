namespace OrderService.Api.Inventory;

public sealed record CheckAvailabilityRequest(List<Guid> ProductIds);
public sealed record CheckAvailabilityResponse(Dictionary<Guid, int> Available);

public sealed record ReserveResponse(bool Success, string? Message = null);

public sealed record ReleaseRequest(Guid ReservationId);

public sealed record ReserveRequest(Guid ProductId, int Quantity, Guid ReservationId, Guid? CustomerId);
public sealed record BulkUpdateRequest(Dictionary<Guid,int> Items);