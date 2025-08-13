using System;
using System.Linq;
using FluentAssertions;
using OrderService.Domain.Entities;
using OrderService.Domain.Enums;
using OrderService.Domain.ValueObjects;
using Xunit;

public class OrderTests
{
    [Fact]
    public void TotalAmount_calculates_sum_of_items()
    {
        var items = new[]
        {
            new OrderItem(Guid.NewGuid(), 2, new Money(50m)),   // 100
            new OrderItem(Guid.NewGuid(), 1, new Money(125m)), // 125
        };
        var order = new Order(Guid.NewGuid(), items);
        order.TotalAmount.Should().Be(225m);
    }

    [Fact]
    public void Creating_order_without_items_should_fail()
    {
        Action act = () => new Order(Guid.NewGuid(), Array.Empty<OrderItem>());
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*at least one item*");
    }

    [Fact]
    public void Max_20_items_rule()
    {
        var items = Enumerable.Range(1, 21)
            .Select(_ => new OrderItem(Guid.NewGuid(), 1, new Money(10m))).ToArray();

        Action act = () => new Order(Guid.NewGuid(), items);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Max 20 items*");
    }

    [Theory]
    [InlineData(99)]
    [InlineData(50001)]
    public void Amount_range_rule_100_50000(decimal totalCandidate)
    {
        var unit = totalCandidate; // quantity=1
        var items = new[] { new OrderItem(Guid.NewGuid(), 1, new Money(unit)) };

        Action act = () => new Order(Guid.NewGuid(), items);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*between 100 and 50000*");
    }

    [Fact]
    public void Confirm_from_pending()
    {
        var order = new Order(Guid.NewGuid(),
            new[] { new OrderItem(Guid.NewGuid(), 1, new Money(200m)) });

        order.Status.Should().Be(OrderStatus.Pending);
        order.Confirm();
        order.Status.Should().Be(OrderStatus.Confirmed);
        order.ConfirmedAt.Should().NotBeNull();
    }

    [Fact]
    public void Ship_only_from_confirmed()
    {
        var order = new Order(Guid.NewGuid(),
            new[] { new OrderItem(Guid.NewGuid(), 1, new Money(200m)) });

        Action act = () => order.MarkShipped();
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*confirmed*");

        order.Confirm();
        order.MarkShipped();
        order.Status.Should().Be(OrderStatus.Shipped);
        order.ShippedAt.Should().NotBeNull();
    }

    [Fact]
    public void Deliver_only_from_shipped()
    {
        var order = new Order(Guid.NewGuid(),
            new[] { new OrderItem(Guid.NewGuid(), 1, new Money(200m)) });

        Action act = () => order.MarkDelivered();
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*shipped*");

        order.Confirm();
        order.MarkShipped();
        order.MarkDelivered();
        order.Status.Should().Be(OrderStatus.Delivered);
        order.DeliveredAt.Should().NotBeNull();
    }

    [Fact]
    public void Cancel_window_2h()
    {
        var order = new Order(Guid.NewGuid(),
            new[] { new OrderItem(Guid.NewGuid(), 1, new Money(200m)) });

        order.Cancel("user_request");
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancelReason.Should().Be("user_request");
    }
}