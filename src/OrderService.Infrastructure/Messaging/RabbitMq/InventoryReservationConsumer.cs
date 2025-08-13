using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderService.Application.Abstractions; // IInventoryStore
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderService.Infrastructure.Messaging.RabbitMq;

public sealed class InventoryReservationConsumer : BackgroundService
{
    private readonly RabbitMqOptions _opt;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InventoryReservationConsumer> _log;

    private IConnection? _conn;
    private IModel? _ch;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public InventoryReservationConsumer(
        RabbitMqOptions opt,
        IServiceScopeFactory scopeFactory,
        ILogger<InventoryReservationConsumer> log)
    {
        _opt = opt;
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(2);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                EnsureConnected();
                EnsureReady();

                // 1) Main exchange
                _ch!.ExchangeDeclare("order.created", ExchangeType.Fanout, durable: true);

                // 2) DLX + DLQ for inventory.reserve
                _ch.ExchangeDeclare("inventory.reserve.dlx", ExchangeType.Direct, durable: true);
                _ch.QueueDeclare("inventory.reserve.dlq", durable: true, exclusive: false, autoDelete: false, arguments: null);
                _ch.QueueBind("inventory.reserve.dlq", "inventory.reserve.dlx", routingKey: "inventory.reserve");

                // 3) Main queue with DLX and priority
                var args = new Dictionary<string, object>
                {
                    ["x-max-priority"] = (byte)10,
                    ["x-dead-letter-exchange"] = "inventory.reserve.dlx",
                    ["x-dead-letter-routing-key"] = "inventory.reserve"
                };
                var q = _ch.QueueDeclare("inventory.reserve", durable: true, exclusive: false, autoDelete: false, arguments: args);
                _ch.QueueBind(q.QueueName, "order.created", routingKey: "");

                var consumer = new AsyncEventingBasicConsumer(_ch);
                consumer.Received += async (_, ea) =>
                {
                    try
                    {
                        var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                        var created = JsonSerializer.Deserialize<OrderCreated>(json, JsonOpts);
                        if (created is null || created.Items is null || created.Items.Count == 0)
                        {
                            _log.LogWarning("order.created payload invalid or empty items. json={Json}", json);
                            _ch.BasicAck(ea.DeliveryTag, false);
                            return;
                        }

                        using var scope = _scopeFactory.CreateScope();
                        var store = scope.ServiceProvider.GetRequiredService<IInventoryStore>();

                        var reserved = new List<Guid>();
                        var allOk = true;

                        foreach (var it in created.Items)
                        {
                            var reservationId = Guid.NewGuid();
                            var ttlSeconds = Environment.GetEnvironmentVariable("INVENTORY_TTL_SECONDS");
                            var ttl = TimeSpan.FromSeconds(int.TryParse(ttlSeconds, out var s) ? s : 600);
                            var ok = await store.TryReserveAsync(
                                it.ProductId, it.Quantity, reservationId, created.CustomerId, created.OrderId, ttl, stoppingToken);

                            if (!ok) { allOk = false; break; }
                            reserved.Add(reservationId);
                        }

                        if (allOk)
                        {
                            // toplamı da publish edilir (fraud için)
                            Publish("stock.reserved", new { orderId = created.OrderId, total = created.Total, reservedAt = DateTime.UtcNow });
                            _log.LogInformation("inventory: reserved -> orderId={OrderId} total={Total}", created.OrderId, created.Total);
                            _ch.BasicAck(ea.DeliveryTag, false);
                        }
                        else
                        {
                            
                            foreach (var rid in reserved)
                                await store.ReleaseAsync(rid, stoppingToken);

                            Publish("stock.failed", new { orderId = created.OrderId, reason = "insufficient_or_rules" });
                            _log.LogWarning("inventory: reserve FAILED -> orderId={OrderId}", created.OrderId);
                            _ch.BasicAck(ea.DeliveryTag, false);
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "inventory.reserve consumer error; nack to DLQ");
                        _ch!.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                    }

                    await Task.CompletedTask;
                };

                _ch.BasicQos(0, 10, false);
                _ch.BasicConsume(q.QueueName, autoAck: false, consumer);

                // payment.failed -> stock.released 
                _ch.ExchangeDeclare("stock.released", ExchangeType.Fanout, durable: true);
                var qr = _ch.QueueDeclare("inventory.release", true, false, false, null);
                _ch.QueueBind(qr.QueueName, "stock.released", "");
                var rel = new AsyncEventingBasicConsumer(_ch);
                rel.Received += async (_, ea) =>
                {
                    try
                    {
                        var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                        var obj = JsonSerializer.Deserialize<StockReleased>(json, JsonOpts);
                        using var scope2 = _scopeFactory.CreateScope();
                        var store2 = scope2.ServiceProvider.GetRequiredService<IInventoryStore>();
                        if (obj is not null && obj.OrderId != Guid.Empty)
                        {
                            await store2.ReleaseByOrderAsync(obj.OrderId, CancellationToken.None);
                            _log.LogInformation("inventory: stock released by orderId={OrderId} (reason={Reason})", obj.OrderId, obj.Reason ?? "n/a");
                        }
                        _ch.BasicAck(ea.DeliveryTag, false);
                    }
                    catch (Exception ex2)
                    {
                        _log.LogError(ex2, "inventory.release handler error; nack to DLQ");
                        _ch.BasicNack(ea.DeliveryTag, false, false);
                    }

                    await Task.CompletedTask;
                };
                _ch.BasicConsume(qr.QueueName, false, rel);

                
                delay = TimeSpan.FromSeconds(2);
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "{Consumer} crashed; will retry in {Delay}s", GetType().Name, delay.TotalSeconds);

                try { _ch?.Close(); } catch { }
                try { _conn?.Close(); } catch { }
                _ch?.Dispose(); _conn?.Dispose();
                _ch = null; _conn = null;

                await Task.Delay(delay, stoppingToken);
                var next = delay.TotalSeconds * 2;
                delay = TimeSpan.FromSeconds(next > 30 ? 30 : next);
            }
        }
    }

    private void EnsureConnected()
    {
        if (_conn is { IsOpen: true }) return;

        var f = new ConnectionFactory
        {
            HostName = _opt.HostName,
            Port = _opt.Port,
            UserName = _opt.UserName,
            Password = _opt.Password,
            VirtualHost = _opt.VirtualHost,
            DispatchConsumersAsync = true,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(3)
        };
        _conn = f.CreateConnection(_opt.ClientProvidedName ?? "order-service");
    }

    private void EnsureReady()
    {
        if (_ch is { IsOpen: true }) return;
        _ch = _conn!.CreateModel();
        _ch.ConfirmSelect();
    }

    private void Publish<T>(string exchange, T evt)
    {
        EnsureConnected(); EnsureReady();
        _ch!.ExchangeDeclare(exchange, ExchangeType.Fanout, durable: true);
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(evt, JsonOpts));
        var props = _ch.CreateBasicProperties();
        props.Persistent = true;
        props.ContentType = "application/json";
        _ch.BasicPublish(exchange, routingKey: "", basicProperties: props, body: body);
        _ch.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
    }

    // ---- payload tipleri ----
    private sealed class OrderCreated
    {
        public Guid OrderId { get; set; }
        public Guid CustomerId { get; set; }
        public decimal Total { get; set; }
        public DateTime At { get; set; }
        public List<OrderItemPayload> Items { get; set; } = new();
    }

    private sealed class OrderItemPayload
    {
        public Guid ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }

    private sealed class StockReleased
    {
        public Guid OrderId { get; set; }
        public string? Reason { get; set; }
        public DateTime At { get; set; }
    }
}