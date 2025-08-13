using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace OrderService.Api.Observability;

public sealed class RabbitMqHealthCheck : IHealthCheck
{
    private readonly OrderService.Infrastructure.Messaging.RabbitMq.RabbitMqOptions _opt;
    public RabbitMqHealthCheck(OrderService.Infrastructure.Messaging.RabbitMq.RabbitMqOptions opt) => _opt = opt;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var f = new ConnectionFactory
            {
                HostName = _opt.HostName,
                Port     = _opt.Port,
                UserName = _opt.UserName,
                Password = _opt.Password,
                VirtualHost = _opt.VirtualHost
            };
            using var conn = f.CreateConnection("hc-probe");
            using var ch = conn.CreateModel();
            return Task.FromResult(HealthCheckResult.Healthy("RabbitMQ reachable"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("RabbitMQ unreachable", ex));
        }
    }
}