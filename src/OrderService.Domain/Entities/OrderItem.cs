using OrderService.Domain.ValueObjects;

namespace OrderService.Domain.Entities;

public class OrderItem
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
    
    public Money UnitPrice { get; private set; }

    private OrderItem() { } // EF

    public OrderItem(Guid productId, int quantity, Money unitPrice)
    {
        ProductId = productId;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }
    
    public decimal Total()
        => Math.Round(UnitPrice.Amount * Quantity, 2, MidpointRounding.AwayFromZero);
}