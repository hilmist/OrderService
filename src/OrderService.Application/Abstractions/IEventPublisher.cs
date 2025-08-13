namespace OrderService.Application.Abstractions;

public interface IEventPublisher
{
    Task PublishAsync<T>(string exchange, T @event, CancellationToken ct = default);
}