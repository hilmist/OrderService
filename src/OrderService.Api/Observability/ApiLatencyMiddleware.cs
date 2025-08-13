namespace OrderService.Api.Observability;

public sealed class ApiLatencyMiddleware
{
    private readonly RequestDelegate _next;
    public ApiLatencyMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext ctx, IMetricsRegistry metrics)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { await _next(ctx); }
        finally
        {
            sw.Stop();
            metrics.ApiLatencyMs.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("route", ctx.Request.Path.Value ?? "/"),
                new KeyValuePair<string, object?>("status_code", ctx.Response.StatusCode));
        }
    }
}