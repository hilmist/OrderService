using System.Reflection;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;

using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.Persistence.Idempotency;

using OrderService.Application.Abstractions;
using OrderService.Infrastructure.Messaging.RabbitMq;

using OrderService.Api.Inventory;
using OrderService.Api.Payments;
using OrderService.Api.Observability;

using FluentValidation;
using FluentValidation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((ctx, lc) =>
    lc.ReadFrom.Configuration(ctx.Configuration).Enrich.FromLogContext());

// EF Core
builder.Services.AddDbContext<OrderDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// MediatR (Application + Infrastructure)
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(Assembly.Load("OrderService.Application"));
    cfg.RegisterServicesFromAssembly(Assembly.Load("OrderService.Infrastructure"));
});

// FluentValidation
builder.Services.AddControllers();
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssembly(Assembly.Load("OrderService.Application"));

// Idempotency
builder.Services.AddScoped<IdempotencyStore>();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Hosted service’leri kapatma bayrağı (CI/EF komutları için)
var disableHosted =
    (Environment.GetEnvironmentVariable("DISABLE_HOSTED_SERVICES") == "1") ||
    builder.Configuration.GetValue<bool>("DisableHostedServices");

// RabbitMQ  (POCO)
builder.Services.AddSingleton(new RabbitMqOptions
{
    HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost",
    Port = int.TryParse(Environment.GetEnvironmentVariable("RABBITMQ_PORT"), out var p) ? p : 5672,
    UserName = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "guest",
    Password = Environment.GetEnvironmentVariable("RABBITMQ_PASS") ?? "guest",
    VirtualHost = "/",
    ClientProvidedName = "order-service"
});

// Publisher
builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();

// Hosted Consumers (bayrak kapalıysa aktif et)
if (!disableHosted)
{
    builder.Services.AddHostedService<InventoryReservationConsumer>();
    builder.Services.AddHostedService<PaymentProcessConsumer>();
    builder.Services.AddHostedService<OrderStatusUpdaterConsumer>();
}

// In-memory stok & ödeme katmanı (mock)
builder.Services.AddSingleton<IInventoryStore, InMemoryInventoryStore>();
builder.Services.AddSingleton<IPaymentStore, InMemoryPaymentStore>();
builder.Services.AddSingleton<IPaymentProcessor, MockPaymentProcessor>();
// Rezervasyon süpürücüsü (stok TTL temizliği) — hosted olsa bile hafif bir servis
if (!disableHosted)
{
    builder.Services.AddHostedService<ReservationSweeper>();
}

// Observability
builder.Services.AddSingleton<IMetricsRegistry, MetricsRegistry>();

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
    .AddCheck("ready", () => HealthCheckResult.Healthy(), tags: new[] { "ready" });

var app = builder.Build();

// DB migrate + idempotency index
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    db.Database.Migrate();

    
    try
    {
        await db.Database.ExecuteSqlRawAsync(@"
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE c.relname = 'uq_idempotency_key' AND c.relkind = 'i'
                ) THEN
                    CREATE UNIQUE INDEX uq_idempotency_key ON idempotency(key);
                END IF;
            END $$;
        ");
    }
    catch
    {
    }
}

// Demo seed 
using (var scope = app.Services.CreateScope())
{
    var inv = scope.ServiceProvider.GetRequiredService<IInventoryStore>();
    await inv.SetStockAsync(Guid.Parse("22222222-2222-2222-2222-222222222222"), 100);
    await inv.SetStockAsync(Guid.Parse("33333333-3333-3333-3333-333333333333"), 50);
    if (inv is InMemoryInventoryStore mem)
    {
        mem.SetFlashSaleProducts(new[] { Guid.Parse("33333333-3333-3333-3333-333333333333") });
    }
}

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Observability middleware’leri (CorrelationId + Latency)
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ApiLatencyMiddleware>();

// Health endpoints
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = r => r.Name == "self" });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = _ => true });

// Controllers
app.MapControllers();

app.Run();
