using FluentMigrator.Runner;
using FluentValidation;
using FreightFlow.CarrierApi.Features.GetCarrier;
using FreightFlow.CarrierApi.Features.OnboardCarrier;
using FreightFlow.CarrierApi.Grpc;
using FreightFlow.CarrierApi.Infrastructure.Messaging;
using FreightFlow.CarrierApi.Infrastructure.Persistence;
using FreightFlow.CarrierApi.Middleware;
using FreightFlow.SharedKernel;
using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// ── Logging ───────────────────────────────────────────────────────────────────
builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.WithProperty("ServiceName", "freight-carrier-api")
       .Enrich.FromLogContext()
       .WriteTo.Console(new CompactJsonFormatter()));

// ── Config ────────────────────────────────────────────────────────────────────
var carrierDb  = builder.Configuration.GetConnectionString("CarrierDb")!;
var redisConn  = builder.Configuration.GetConnectionString("Redis")!;
var rabbitHost = builder.Configuration["RabbitMq:Host"]!;
var rabbitUser = builder.Configuration["RabbitMq:Username"]!;
var rabbitPass = builder.Configuration["RabbitMq:Password"]!;

// ── EF Core ───────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<CarrierDbContext>(options =>
    options.UseNpgsql(carrierDb));

// ── FluentMigrator ────────────────────────────────────────────────────────────
builder.Services
    .AddFluentMigratorCore()
    .ConfigureRunner(r => r
        .AddPostgres()
        .WithGlobalConnectionString(carrierDb)
        .ScanIn(typeof(Program).Assembly).For.Migrations());

// ── MassTransit / RabbitMQ ────────────────────────────────────────────────────
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((_, cfg) =>
    {
        cfg.Host(rabbitHost, "/", h =>
        {
            h.Username(rabbitUser);
            h.Password(rabbitPass);
        });
    });
});

// ── Outbox dispatcher ─────────────────────────────────────────────────────────
builder.Services.Configure<OutboxOptions>(
    builder.Configuration.GetSection("Outbox"));
builder.Services.AddHostedService<OutboxDispatcher>();

// ── Validation ────────────────────────────────────────────────────────────────
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// ── Feature handlers ──────────────────────────────────────────────────────────
builder.Services.AddTransient<OnboardCarrierHandler>();
builder.Services.AddTransient<GetCarrierHandler>();

// ── gRPC ──────────────────────────────────────────────────────────────────────
builder.Services.AddGrpc();

// ── Redis (idempotency + gRPC reservation cache) ──────────────────────────────
builder.Services.AddStackExchangeRedisCache(options =>
    options.Configuration = redisConn);

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddNpgSql(carrierDb, tags: ["carrier_db"])
    .AddRedis(redisConn, tags: ["redis"]);

// ── OpenTelemetry ─────────────────────────────────────────────────────────────
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAspNetCoreInstrumentation());

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

// ── Migrations ────────────────────────────────────────────────────────────────
// ⚠ TRADE-OFF: migrations run on every startup for local development convenience.
// NEVER do this in production — use a CI/CD pipeline step or init container.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
}

// ── Middleware pipeline ───────────────────────────────────────────────────────
app.UseMiddleware<DomainExceptionMiddleware>();
app.UseMiddleware<IdempotencyMiddleware>();

// ── gRPC endpoints ────────────────────────────────────────────────────────────
app.MapGrpcService<CapacityGrpcService>();

// ── REST endpoints ────────────────────────────────────────────────────────────
app.MapPost("/carriers", async (
    OnboardCarrierCommand command,
    IValidator<OnboardCarrierCommand> validator,
    OnboardCarrierHandler handler,
    CancellationToken ct) =>
{
    var result = await validator.ValidateAsync(command, ct);
    if (!result.IsValid)
        return Results.ValidationProblem(result.ToDictionary());

    var id = await handler.HandleAsync(command, ct);
    return Results.Created($"/carriers/{id.Value}", new { id = id.Value });
});

app.MapGet("/carriers/{id:guid}", async (
    Guid id,
    GetCarrierHandler handler,
    CancellationToken ct) =>
{
    var dto = await handler.HandleAsync(CarrierId.From(id), ct);
    return Results.Ok(dto);
});

// ── Health checks ─────────────────────────────────────────────────────────────
app.MapHealthChecks("/health");  // liveness — is the process alive?
app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Count > 0  // readiness — are all deps reachable?
});

app.Run();
