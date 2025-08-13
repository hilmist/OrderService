using OrderService.Application.Abstractions;

namespace OrderService.Infrastructure.Messaging;

public sealed class NoOpEventPublisher : IEventPublisher
{
    public Task PublishAsync<T>(string exchange, T @event, CancellationToken ct = default)
        => Task.CompletedTask; // Hiçbir şey yapmaz
}