using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OrderService.Application.Abstractions;
using RabbitMQ.Client;

namespace OrderService.Infrastructure.Messaging.RabbitMq;

public sealed class RabbitMqEventPublisher : IEventPublisher, IDisposable
{
    private readonly RabbitMqOptions _opt;
    private readonly ILogger<RabbitMqEventPublisher> _log;

    private IConnection _conn = null!;
    private IModel _ch = null!;

    public RabbitMqEventPublisher(RabbitMqOptions opt, ILogger<RabbitMqEventPublisher> log)
    {
        _opt = opt;
        _log = log;
    }

    private void EnsureConnected()
    {
        if (_conn != null && _conn.IsOpen && _ch != null && _ch.IsOpen) return;

        var factory = new ConnectionFactory {
            HostName = _opt.HostName,
            Port     = _opt.Port,
            UserName = _opt.UserName,
            Password = _opt.Password,
            VirtualHost = _opt.VirtualHost,
            ClientProvidedName = _opt.ClientProvidedName,
            DispatchConsumersAsync = true
        };
        _conn = factory.CreateConnection();
        _ch   = _conn.CreateModel();
    }

    private void EnsureReady()
    {
        if (_conn == null || !_conn.IsOpen || _ch == null || !_ch.IsOpen)
            throw new InvalidOperationException("RabbitMQ channel is not initialized or already closed.");
    }

    public Task PublishAsync<T>(string exchange, T @event, CancellationToken ct = default)
    {
        EnsureConnected();
        EnsureReady();

        _ch.ExchangeDeclare(exchange, ExchangeType.Fanout, durable: true);
        var props = _ch.CreateBasicProperties();
        props.Persistent = true;
        props.ContentType = "application/json";

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(@event));
        _ch.BasicPublish(exchange, routingKey: string.Empty, basicProperties: props, body: body);
        _log.LogInformation("Published to {Exchange}", exchange);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        try { _ch?.Close(); } catch { }
        try { _conn?.Close(); } catch { }
        _ch?.Dispose();
        _conn?.Dispose();
    }
}