using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Polly;
using Polly.Retry;

namespace OrderService.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// order.cancelled geldikten sonra refund sürecini simüle eder.
/// Başarılıysa refund.processed ve stok iadesi için stock.released publish eder.
/// Hatalı durumda refund.failed publish edilir. DLX/DLQ tanımlıdır.
/// </summary>
public sealed class PaymentRefundConsumer : BackgroundService
{
    private readonly RabbitMqOptions _opt;
    private readonly ILogger<PaymentRefundConsumer> _log;

    private IConnection? _conn;
    private IModel? _ch;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly Random _rnd = new();

    public PaymentRefundConsumer(RabbitMqOptions opt, ILogger<PaymentRefundConsumer> log)
    {
        _opt = opt;
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

                // Dinlenecek exchange
                _ch!.ExchangeDeclare("order.cancelled", ExchangeType.Fanout, durable: true);

                // DLX/DLQ
                _ch.ExchangeDeclare("payment.refund.dlx", ExchangeType.Direct, durable: true);
                _ch.QueueDeclare("payment.refund.dlq", durable: true, exclusive: false, autoDelete: false, arguments: null);
                _ch.QueueBind("payment.refund.dlq", "payment.refund.dlx", "payment.refund");

                // Ana kuyruk
                var args = new Dictionary<string, object> {
                    ["x-dead-letter-exchange"] = "payment.refund.dlx",
                    ["x-dead-letter-routing-key"] = "payment.refund"
                };
                var q = _ch.QueueDeclare("payment.refund", durable: true, exclusive: false, autoDelete: false, arguments: args);
                _ch.QueueBind(q.QueueName, "order.cancelled", "");

                // Çıkış exchange’leri
                _ch.ExchangeDeclare("refund.processed", ExchangeType.Fanout, durable: true);
                _ch.ExchangeDeclare("refund.failed",    ExchangeType.Fanout, durable: true);
                _ch.ExchangeDeclare("stock.released",   ExchangeType.Fanout, durable: true);

                _ch.BasicQos(0, 10, false);

                var consumer = new AsyncEventingBasicConsumer(_ch);
                consumer.Received += async (_, ea) =>
                {
                    try
                    {
                        var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                        var msg = JsonSerializer.Deserialize<OrderCancelled>(json, JsonOpts);
                        if (msg is null || msg.OrderId == Guid.Empty)
                        {
                            _log.LogWarning("refund: invalid payload json={Json}", json);
                            _ch.BasicAck(ea.DeliveryTag, false);
                            return;
                        }
                        
                        AsyncRetryPolicy retry = Policy
                            .Handle<TimeoutException>()
                            .Or<InvalidOperationException>()
                            .WaitAndRetryAsync(3,
                                attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1) + _rnd.Next(0, 100)),
                                (ex, d, attempt, ctx) => _log.LogWarning(ex, "refund.retry attempt={Attempt} delay={Delay}ms orderId={OrderId}", attempt, (int)d.TotalMilliseconds, msg.OrderId));

                        bool success = false;

                        try
                        {
                            await retry.ExecuteAsync(async () =>
                            {
                                // %95 başarı, %5 failure (timeout/decline)
                                int dice = Random.Shared.Next(0, 100);
                                if (dice <= 94) { success = true; await Task.Delay(50); return; }
                                if (dice <= 97) throw new TimeoutException("refund timeout");
                                throw new InvalidOperationException("refund declined");
                            });
                        }
                        catch (Exception ex)
                        {
                            Publish("refund.failed", new { orderId = msg.OrderId, reason = ex.Message, at = DateTime.UtcNow });
                            _log.LogWarning("refund: FAILED orderId={OrderId} reason={Reason}", msg.OrderId, ex.Message);
                            _ch.BasicAck(ea.DeliveryTag, false);
                            return;
                        }

                        if (success)
                        {
                            Publish("refund.processed", new { orderId = msg.OrderId, at = DateTime.UtcNow });
                            
                            Publish("stock.released", new { orderId = msg.OrderId, reason = "order_cancelled_refund", at = DateTime.UtcNow });

                            _log.LogInformation("refund: PROCESSED orderId={OrderId}", msg.OrderId);
                            _ch.BasicAck(ea.DeliveryTag, false);
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "refund: unexpected error; nack→DLQ");
                        _ch!.BasicNack(ea.DeliveryTag, false, false);
                    }

                    await Task.CompletedTask;
                };

                _ch.BasicConsume(q.QueueName, false, consumer);

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
                delay = TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));
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
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(evt, JsonOpts));
        var props = _ch!.CreateBasicProperties();
        props.Persistent = true;
        props.ContentType = "application/json";
        _ch.BasicPublish(exchange, "", props, body);
        _ch.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
    }

    private sealed class OrderCancelled
    {
        public Guid OrderId { get; set; }
        public string? Reason { get; set; }
        public DateTime At { get; set; }
    }
}