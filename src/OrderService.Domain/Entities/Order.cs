using OrderService.Domain.Enums;

namespace OrderService.Domain.Entities;

public class Order
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CustomerId { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.Pending;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string?   CancelReason { get; private set; }
    public DateTime? ShippedAt   { get; private set; }
    public DateTime? DeliveredAt { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();
    
    public decimal TotalAmount { get; private set; }

    private readonly List<OrderItem> _items = new();
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    private Order() { } // EF için

    public Order(Guid customerId, IEnumerable<OrderItem> items)
    {
        CustomerId = customerId;
        if (items != null) _items.AddRange(items);
        RecalculateTotal();    
        ValidateBusinessRules();
    }

    public void AddItem(OrderItem item)
    {
        _items.Add(item);
        RecalculateTotal();
        ValidateBusinessRules();
    }

    public void Confirm()
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException("Only pending orders can be confirmed.");
        Status = OrderStatus.Confirmed;
        ConfirmedAt = DateTime.UtcNow;
    }

    public void Cancel(string reason = "")
    {
        if (DateTime.UtcNow - CreatedAt > TimeSpan.FromHours(2))
            throw new InvalidOperationException("Cancellation window exceeded (2h)");

        if (Status is OrderStatus.Confirmed or OrderStatus.Pending)
        {
            Status = OrderStatus.Cancelled;
            CancelReason = reason;
            CancelledAt = DateTime.UtcNow;
        }
    }

    public void MarkShipped()
    {
        if (Status != OrderStatus.Confirmed)
            throw new InvalidOperationException("Only confirmed orders can be shipped.");
        Status = OrderStatus.Shipped;
        ShippedAt = DateTime.UtcNow;
    }

    public void MarkDelivered()
    {
        if (Status != OrderStatus.Shipped)
            throw new InvalidOperationException("Only shipped orders can be delivered.");
        Status = OrderStatus.Delivered;
        DeliveredAt = DateTime.UtcNow;
    }

    private void ValidateBusinessRules()
    {
        if (!_items.Any()) throw new InvalidOperationException("Order must have at least one item");
        if (_items.Count > 20) throw new InvalidOperationException("Max 20 items per order");
        if (TotalAmount < 100 || TotalAmount > 50000)
            throw new InvalidOperationException("Order amount must be between 100 and 50000 TL");
    }
    
    private void RecalculateTotal()
    {
        // OrderItem.Total() → Quantity * UnitPrice.Amount
        var sum = _items.Sum(i => i.Total());
        TotalAmount = Math.Round(sum, 2, MidpointRounding.AwayFromZero);
    }
}