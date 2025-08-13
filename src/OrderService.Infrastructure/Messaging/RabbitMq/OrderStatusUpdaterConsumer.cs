using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderService.Infrastructure.Persistence;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderService.Infrastructure.Messaging.RabbitMq;

public sealed class OrderStatusUpdaterConsumer : BackgroundService
{
    private readonly RabbitMqOptions _opt;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderStatusUpdaterConsumer> _log;

    private IConnection? _conn;
    private IModel? _ch;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public OrderStatusUpdaterConsumer(
        RabbitMqOptions opt,
        IServiceScopeFactory scopeFactory,
        ILogger<OrderStatusUpdaterConsumer> log)
    {
        _opt = opt;
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backoff = TimeSpan.FromSeconds(2);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                EnsureConnected();
                EnsureReady();

                // ---- Exchanges ----
                _ch!.ExchangeDeclare("payment.processed", ExchangeType.Fanout, durable: true);
                _ch.ExchangeDeclare("payment.failed",    ExchangeType.Fanout, durable: true);
                _ch.ExchangeDeclare("stock.failed",      ExchangeType.Fanout, durable: true);

                // ---- payment.processed → order.update.from.payment (+ DLQ) ----
                _ch.ExchangeDeclare("order.update.from.payment.dlx", ExchangeType.Direct, durable: true);
                _ch.QueueDeclare("order.update.from.payment.dlq", true, false, false, null);
                _ch.QueueBind("order.update.from.payment.dlq", "order.update.from.payment.dlx", "order.update.from.payment");

                var argsProcessed = new Dictionary<string, object> {
                    ["x-dead-letter-exchange"] = "order.update.from.payment.dlx",
                    ["x-dead-letter-routing-key"] = "order.update.from.payment"
                };
                var qProcessed = _ch.QueueDeclare("order.update.from.payment", true, false, false, argsProcessed);
                _ch.QueueBind(qProcessed.QueueName, "payment.processed", "");

                // ---- payment.failed → order.update.from.payment.failed (+ DLQ) ----
                _ch.ExchangeDeclare("order.update.from.payment.failed.dlx", ExchangeType.Direct, durable: true);
                _ch.QueueDeclare("order.update.from.payment.failed.dlq", true, false, false, null);
                _ch.QueueBind("order.update.from.payment.failed.dlq", "order.update.from.payment.failed.dlx", "order.update.from.payment.failed");

                var argsFailed = new Dictionary<string, object> {
                    ["x-dead-letter-exchange"] = "order.update.from.payment.failed.dlx",
                    ["x-dead-letter-routing-key"] = "order.update.from.payment.failed"
                };
                var qFailed = _ch.QueueDeclare("order.update.from.payment.failed", true, false, false, argsFailed);
                _ch.QueueBind(qFailed.QueueName, "payment.failed", "");

                // stock.failed → order.update.from.stock.failed (+ DLQ) ----
                _ch.ExchangeDeclare("order.update.from.stock.failed.dlx", ExchangeType.Direct, durable: true);
                _ch.QueueDeclare("order.update.from.stock.failed.dlq", true, false, false, null);
                _ch.QueueBind("order.update.from.stock.failed.dlq", "order.update.from.stock.failed.dlx", "order.update.from.stock.failed");

                var argsStockFail = new Dictionary<string, object> {
                    ["x-dead-letter-exchange"] = "order.update.from.stock.failed.dlx",
                    ["x-dead-letter-routing-key"] = "order.update.from.stock.failed"
                };
                var qStockFail = _ch.QueueDeclare("order.update.from.stock.failed", true, false, false, argsStockFail);
                _ch.QueueBind(qStockFail.QueueName, "stock.failed", "");

                _ch.BasicQos(0, 10, false);

                // -------- payment.processed → CONFIRMED --------
                var consOk = new AsyncEventingBasicConsumer(_ch);
                consOk.Received += async (_, ea) =>
                {
                    try
                    {
                        var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                        var msg = JsonSerializer.Deserialize<PaymentProcessed>(json, JsonOpts);
                        if (msg is null || msg.OrderId == Guid.Empty)
                        {
                            _log.LogWarning("order.update: processed invalid json={Json}", json);
                            _ch.BasicAck(ea.DeliveryTag, false);
                            return;
                        }

                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
                        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == msg.OrderId, stoppingToken);
                        if (order is null)
                        {
                            _log.LogWarning("order.update: processed but order not found id={OrderId}", msg.OrderId);
                            _ch.BasicAck(ea.DeliveryTag, false);
                            return;
                        }

                        
                        if (order.Status.ToString().Equals("Confirmed", StringComparison.OrdinalIgnoreCase))
                        {
                            _log.LogInformation("order.update: already confirmed id={OrderId}", msg.OrderId);
                            _ch.BasicAck(ea.DeliveryTag, false);
                            return;
                        }

                        order.Confirm();
                        await db.SaveChangesAsync(stoppingToken);

                        _log.LogInformation("order.update: CONFIRMED id={OrderId}", msg.OrderId);
                        _ch.BasicAck(ea.DeliveryTag, false);
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "order.update processed handler error; nack→DLQ");
                        _ch!.BasicNack(ea.DeliveryTag, false, false);
                    }
                    await Task.CompletedTask;
                };
                _ch.BasicConsume(qProcessed.QueueName, false, consOk);

                // -------- payment.failed → CANCELLED + stock.released --------
                var consFail = new AsyncEventingBasicConsumer(_ch);
                consFail.Received += async (_, ea) =>
                {
                    try
                    {
                        var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                        var msg = JsonSerializer.Deserialize<PaymentFailed>(json, JsonOpts);
                        if (msg is null || msg.OrderId == Guid.Empty)
                        {
                            _log.LogWarning("order.update: failed invalid json={Json}", json);
                            _ch.BasicAck(ea.DeliveryTag, false);
                            return;
                        }

                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
                        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == msg.OrderId, stoppingToken);
                        if (order is null)
                        {
                            _log.LogWarning("order.update: failed but order not found id={OrderId}", msg.OrderId);
                            _ch.BasicAck(ea.DeliveryTag, false);
                            return;
                        }

                        if (order.Status.ToString().Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
                        {
                            _log.LogInformation("order.update: already cancelled id={OrderId}", msg.OrderId);
                            _ch.BasicAck(ea.DeliveryTag, false);
                            return;
                        }

                        order.Cancel(msg.Reason ?? "payment_failed");
                        await db.SaveChangesAsync(stoppingToken);

                        
                        Publish("stock.released", new { orderId = msg.OrderId, reason = "payment_failed", at = DateTime.UtcNow });

                        _log.LogWarning("order.update: CANCELLED id={OrderId} reason={Reason}", msg.OrderId, msg.Reason ?? "payment_failed");
                        _ch.BasicAck(ea.DeliveryTag, false);
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "order.update failed handler error; nack→DLQ");
                        _ch!.BasicNack(ea.DeliveryTag, false, false);
                    }
                    await Task.CompletedTask;
                };
                _ch.BasicConsume(qFailed.QueueName, false, consFail);

                // -------- stock.failed → CANCELLED --------
                var consStockFail = new AsyncEventingBasicConsumer(_ch);
                consStockFail.Received += async (_, ea) =>
                {
                    try
                    {
                        var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                        var msg = JsonSerializer.Deserialize<StockFailedPayload>(json, JsonOpts);
                        if (msg is null || msg.OrderId == Guid.Empty)
                        {
                            _log.LogWarning("order.update: stock.failed invalid json={Json}", json);
                            _ch.BasicAck(ea.DeliveryTag, false);
                            return;
                        }

                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
                        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == msg.OrderId, stoppingToken);
                        if (order is null)
                        {
                            _log.LogWarning("order.update: stock.failed but order not found id={OrderId}", msg.OrderId);
                            _ch.BasicAck(ea.DeliveryTag, false);
                            return;
                        }

                        // yalnızca Pending ise CANCELLED yapılır
                        if (!order.Status.ToString().Equals("Cancelled", StringComparison.OrdinalIgnoreCase) &&
                            !order.Status.ToString().Equals("Confirmed", StringComparison.OrdinalIgnoreCase))
                        {
                            order.Cancel(msg.Reason ?? "inventory_failed");
                            await db.SaveChangesAsync(stoppingToken);
                            _log.LogWarning("order.update: CANCELLED by stock.failed id={OrderId} reason={Reason}", msg.OrderId, msg.Reason ?? "inventory_failed");
                        }

                        _ch.BasicAck(ea.DeliveryTag, false);
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "order.update stock.failed handler error; nack→DLQ");
                        _ch!.BasicNack(ea.DeliveryTag, false, false);
                    }
                    await Task.CompletedTask;
                };
                _ch.BasicConsume(qStockFail.QueueName, false, consStockFail);

                backoff = TimeSpan.FromSeconds(2);
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "{Consumer} crashed; will retry in {Sec}s", backoff.TotalSeconds);
                try { _ch?.Close(); } catch { }
                try { _conn?.Close(); } catch { }
                _ch?.Dispose(); _conn?.Dispose();
                _ch = null; _conn = null;
                await Task.Delay(backoff, stoppingToken);
                var next = backoff.TotalSeconds * 2;
                backoff = TimeSpan.FromSeconds(next > 30 ? 30 : next);
            }
        }
    }

    private void EnsureConnected()
    {
        if (_conn is { IsOpen: true }) return;
        var f = new ConnectionFactory
        {
            HostName = _opt.HostName, Port = _opt.Port,
            UserName = _opt.UserName, Password = _opt.Password,
            VirtualHost = _opt.VirtualHost, DispatchConsumersAsync = true,
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
        var props = _ch.CreateBasicProperties(); props.Persistent = true; props.ContentType = "application/json";
        _ch.BasicPublish(exchange, "", props, body);
        _ch.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
    }

    private sealed class PaymentProcessed { public Guid OrderId { get; set; } public DateTime PaidAt { get; set; } }
    private sealed class PaymentFailed    { public Guid OrderId { get; set; } public string? Reason { get; set; } public DateTime FailedAt { get; set; } }
    private sealed class StockFailedPayload { public Guid OrderId { get; set; } public string? Reason { get; set; } public DateTime At { get; set; } }
}