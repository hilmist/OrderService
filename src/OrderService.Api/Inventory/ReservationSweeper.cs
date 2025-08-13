using Microsoft.Extensions.Hosting;
using OrderService.Application.Abstractions;

namespace OrderService.Api.Inventory;

public sealed class ReservationSweeper : BackgroundService
{
    private readonly IInventoryStore _store;
    public ReservationSweeper(IInventoryStore store) => _store = store;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _store.ReleaseExpired();
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}