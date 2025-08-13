using System.Diagnostics.Metrics;

namespace OrderService.Api.Observability;

public interface IMetricsRegistry
{
    Counter<long> OrdersCreated { get; }
    Counter<long> OrdersConfirmed { get; }
    Counter<long> OrdersCancelled { get; }
    Counter<long> OrdersFailed { get; }
    Counter<double> RevenueTotal { get; }
    Histogram<double> ApiLatencyMs { get; }
}

public sealed class MetricsRegistry : IMetricsRegistry, IDisposable
{
    private readonly Meter _meter = new("OrderService", "1.0.0");

    public Counter<long> OrdersCreated { get; }
    public Counter<long> OrdersConfirmed { get; }
    public Counter<long> OrdersCancelled { get; }
    public Counter<long> OrdersFailed { get; }
    public Counter<double> RevenueTotal { get; }
    public Histogram<double> ApiLatencyMs { get; }

    public MetricsRegistry()
    {
        OrdersCreated  = _meter.CreateCounter<long>("orders_created_total");
        OrdersConfirmed= _meter.CreateCounter<long>("orders_confirmed_total");
        OrdersCancelled= _meter.CreateCounter<long>("orders_cancelled_total");
        OrdersFailed   = _meter.CreateCounter<long>("orders_failed_total");
        RevenueTotal   = _meter.CreateCounter<double>("revenue_total_tl");
        ApiLatencyMs   = _meter.CreateHistogram<double>("api_response_ms");
    }

    public void Dispose() => _meter.Dispose();
}