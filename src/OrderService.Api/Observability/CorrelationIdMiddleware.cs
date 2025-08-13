using Serilog.Context;

namespace OrderService.Api.Observability;

public sealed class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext ctx)
    {
        var cid = ctx.Request.Headers.TryGetValue(HeaderName, out var h) && !string.IsNullOrWhiteSpace(h)
            ? h.ToString()
            : Guid.NewGuid().ToString("N");

        ctx.Response.Headers[HeaderName] = cid;

        using (LogContext.PushProperty("CorrelationId", cid))
        {
            await _next(ctx);
        }
    }
}