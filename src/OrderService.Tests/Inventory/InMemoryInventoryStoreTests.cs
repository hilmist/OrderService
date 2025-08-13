using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using OrderService.Api.Inventory;
using Xunit;

public class InMemoryInventoryStoreTests
{
    [Fact]
    public async Task BulkSet_and_GetStock()
    {
        var store = new InMemoryInventoryStore();
        var pid = Guid.NewGuid();

        await store.BulkSetAsync(new Dictionary<Guid, int>{{pid, 100}});
        var s = await store.GetStockAsync(pid);
        s.Should().Be(100);
    }

    [Fact]
    public async Task TryReserve_should_decrease_stock_when_available()
    {
        var store = new InMemoryInventoryStore();
        var pid = Guid.NewGuid();
        await store.BulkSetAsync(new Dictionary<Guid, int>{{pid, 10}});

        var ok = await store.TryReserveAsync(pid, 3, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromMinutes(10), default);
        ok.Should().BeTrue();

        var left = await store.GetStockAsync(pid);
        left.Should().Be(7);
    }

    [Fact]
    public async Task TryReserve_should_fail_when_over_50_percent_of_available_for_order()
    {
        var store = new InMemoryInventoryStore();
        var pid = Guid.NewGuid();
        await store.BulkSetAsync(new Dictionary<Guid, int>{{pid, 10}});

        // %50 kuralı: 10'un %50'si = 5; 6 istersek fail
        var ok = await store.TryReserveAsync(pid, 6, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromMinutes(10), default);
        ok.Should().BeFalse();
    }

    [Fact]
    public async Task ReleaseByOrder_should_return_stock()
    {
        var store = new InMemoryInventoryStore();
        var pid = Guid.NewGuid();
        await store.BulkSetAsync(new Dictionary<Guid, int>{{pid, 10}});

        var orderId = Guid.NewGuid();
        var r1 = Guid.NewGuid();
        await store.TryReserveAsync(pid, 4, r1, Guid.NewGuid(), orderId, TimeSpan.FromMinutes(10), default);

        await store.ReleaseByOrderAsync(orderId, default);

        var s = await store.GetStockAsync(pid);
        s.Should().Be(10);
    }
}