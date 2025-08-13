using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using OrderService.Infrastructure.Persistence;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderService.Infrastructure.Messaging.RabbitMq;

public sealed class PaymentProcessConsumer : BackgroundService
{
    private readonly RabbitMqOptions _opt;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentProcessConsumer> _log;

    private IConnection? _conn;
    private IModel? _ch;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly Random _rng = new();

    public PaymentProcessConsumer(
        RabbitMqOptions opt,
        IServiceScopeFactory scopeFactory,
        ILogger<PaymentProcessConsumer> log)
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

                // --- Exchanges ---
                // Giriş: stok rezerve edildi → ödeme başlat
                DeclareExchangeOrRecreate("stock.reserved", ExchangeType.Fanout, durable: true);

                // Çıkış: ödeme sonucu
                DeclareExchangeOrRecreate("payment.processed", ExchangeType.Fanout, durable: true);
                DeclareExchangeOrRecreate("payment.failed",   ExchangeType.Fanout, durable: true);

                // DLX for payment.process queue
                DeclareExchangeOrRecreate("payment.process.dlx", ExchangeType.Direct, durable: true);
                _ch!.QueueDeclare("payment.process.dlq", true, false, false, null);
                _ch.QueueBind("payment.process.dlq", "payment.process.dlx", "payment.process");

                // --- Main queue (DLX + bind to stock.reserved) ---
                var qArgs = new Dictionary<string, object> {
                    ["x-dead-letter-exchange"] = "payment.process.dlx",
                    ["x-dead-letter-routing-key"] = "payment.process"
                };
                ForceRecreateQueue("payment.process", qArgs);
                _ch.QueueBind("payment.process", "stock.reserved", "");

                _ch.BasicQos(0, 10, false);

                var consumer = new AsyncEventingBasicConsumer(_ch);
                consumer.Received += async (_, ea) =>
                {
                    try
                    {
                        var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                        var msg = JsonSerializer.Deserialize<StockReserved>(json, JsonOpts);

                        if (msg is null || msg.OrderId == Guid.Empty)
                        {
                            _log.LogWarning("payment.process: invalid payload json={Json}", json);
                            _ch.BasicAck(ea.DeliveryTag, false);
                            return;
                        }

                        // order total çek (fraud kuralı için)
                        decimal total = 0m;
                        using (var scope = _scopeFactory.CreateScope())
                        {
                            var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
                            var order = await db.Orders.AsNoTracking()
                                .FirstOrDefaultAsync(o => o.Id == msg.OrderId, cancellationToken: stoppingToken);
                            if (order is null)
                            {
                                _log.LogWarning("payment.process: order not found id={OrderId}", msg.OrderId);
                                _ch.BasicAck(ea.DeliveryTag, false);
                                return;
                            }
                            total = order.TotalAmount;
                        }

                        // Fraud rule: > 10.000 TL → verification required (fail)
                        if (total > 10_000m)
                        {
                            await PublishAsync("payment.failed", new {
                                orderId = msg.OrderId,
                                reason = "fraud_verification_required",
                                failedAt = DateTime.UtcNow
                            }, stoppingToken);

                            _log.LogWarning("payment.process: FRAUD flagged id={OrderId} total={Total}", msg.OrderId, total);
                            _ch.BasicAck(ea.DeliveryTag, false);
                            return;
                        }

                        // Retry ile ödeme simülasyonu
                        var processed = await TryProcessWithRetryAsync(msg.OrderId, total, stoppingToken);

                        if (processed)
                        {
                            await PublishAsync("payment.processed", new {
                                orderId = msg.OrderId,
                                paidAt = DateTime.UtcNow,
                                amount = total
                            }, stoppingToken);
                            _log.LogInformation("payment.process: PROCESSED id={OrderId}", msg.OrderId);
                        }
                        else
                        {
                            await PublishAsync("payment.failed", new {
                                orderId = msg.OrderId,
                                reason = "processor_error",
                                failedAt = DateTime.UtcNow
                            }, stoppingToken);
                            _log.LogWarning("payment.process: FAILED id={OrderId}", msg.OrderId);
                        }

                        _ch.BasicAck(ea.DeliveryTag, false);
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "payment.process handler error; nack→DLQ");
                        _ch!.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                    }

                    await Task.CompletedTask;
                };

                _ch.BasicConsume("payment.process", autoAck: false, consumer);

                
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

    // --- Payment Simulation with Retry ---
    private enum AttemptResult { Success, Timeout, Failure }

    private async Task<bool> TryProcessWithRetryAsync(Guid orderId, decimal amount, CancellationToken ct)
    {
        const int maxAttempts = 3;
        var delay = TimeSpan.FromMilliseconds(500);

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var res = SimulateProcessorOutcome();

            if (res == AttemptResult.Success)
                return true;

            if (res == AttemptResult.Failure)
                return false;

            // Timeout → retry (exponential backoff)
            _log.LogWarning("payment.process: TIMEOUT attempt={Attempt}/{Max} id={OrderId}", attempt, maxAttempts, orderId);
            if (attempt == maxAttempts) return false;

            try { await Task.Delay(delay, ct); } catch { }
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 4000));
        }

        return false;
    }

    private AttemptResult SimulateProcessorOutcome()
    {
        var roll = _rng.Next(0, 100);
        if (roll < 85) return AttemptResult.Success;    // %85
        if (roll < 95) return AttemptResult.Timeout;    // %10
        return AttemptResult.Failure;                   // %5
    }

    // --- RabbitMQ Helpers ---
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

    private void DeclareExchangeOrRecreate(string exchange, string type, bool durable = true, bool autoDelete = false, IDictionary<string, object>? args = null)
    {
        try
        {
            _ch!.ExchangeDeclare(exchange, type, durable: durable, autoDelete: autoDelete, arguments: args);
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException ex)
        {
            // 406: tip/argüman uyuşmazlığı → sil ve doğru şekilde yeniden oluştur
            if (ex.ShutdownReason?.ReplyCode == 406)
            {
                try { _ch.ExchangeDelete(exchange); } catch { }
                _ch.ExchangeDeclare(exchange, type, durable: durable, autoDelete: autoDelete, arguments: args);
                return;
            }
            throw;
        }
    }

    //  kuyrukları her başlatmada sil → doğru arglarla oluştur
    private void ForceRecreateQueue(string queue, IDictionary<string, object>? args)
    {
        try { _ch!.QueueDelete(queue); } catch { /* yoksa sorun değil */ }
        _ch!.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false, arguments: args);
    }

    private async Task PublishAsync<T>(string exchange, T evt, CancellationToken ct)
    {
        EnsureConnected(); EnsureReady();
        _ch!.ExchangeDeclare(exchange, ExchangeType.Fanout, durable: true);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(evt, JsonOpts));
        var props = _ch.CreateBasicProperties();
        props.Persistent = true;
        props.ContentType = "application/json";

        _ch.BasicPublish(exchange, routingKey: "", basicProperties: props, body: body);
        _ch.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));

        await Task.CompletedTask;
    }

    // --- Payload ---
    private sealed class StockReserved
    {
        public Guid OrderId { get; set; }
        public DateTime ReservedAt { get; set; }
    }
}