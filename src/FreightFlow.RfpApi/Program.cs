using System.Text.Json.Serialization;
using FluentMigrator.Runner;
using FluentValidation;
using FreightFlow.RfpApi.Features.AddLane;
using FreightFlow.RfpApi.Features.AwardCarrier;
using FreightFlow.RfpApi.Features.CreateRfp;
using FreightFlow.RfpApi.Features.GetRfp;
using FreightFlow.RfpApi.Features.OpenRfp;
using FreightFlow.RfpApi.Features.SubmitBid;
using FreightFlow.RfpApi.Infrastructure.Messaging;
using FreightFlow.RfpApi.Infrastructure.Persistence;
using FreightFlow.RfpApi.Middleware;
using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// ── Logging ───────────────────────────────────────────────────────────────────
builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.WithProperty("ServiceName", "freight-rfp-api")
       .Enrich.FromLogContext()
       .WriteTo.Console(new CompactJsonFormatter()));

// ── Config ────────────────────────────────────────────────────────────────────
var rfpDb      = builder.Configuration.GetConnectionString("RfpDb")!;
var redisConn  = builder.Configuration.GetConnectionString("Redis")!;
var rabbitHost = builder.Configuration["RabbitMq:Host"]!;
var rabbitUser = builder.Configuration["RabbitMq:Username"]!;
var rabbitPass = builder.Configuration["RabbitMq:Password"]!;

// ── JSON ──────────────────────────────────────────────────────────────────────
// Accept enum values as strings (e.g. "Class70") as well as integers.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ── EF Core + NpgsqlDataSource ────────────────────────────────────────────────
// Singleton NpgsqlDataSource manages the Npgsql connection pool.
// EF Core and Dapper share the same pool — one source of truth for connections.
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(rfpDb));
builder.Services.AddDbContext<RfpDbContext>((sp, options) =>
    options.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>()));

// ── FluentMigrator ────────────────────────────────────────────────────────────
builder.Services
    .AddFluentMigratorCore()
    .ConfigureRunner(r => r
        .AddPostgres()
        .WithGlobalConnectionString(rfpDb)
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
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection("Outbox"));
builder.Services.AddHostedService<OutboxDispatcher>();

// ── Validation ────────────────────────────────────────────────────────────────
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// ── Feature handlers ──────────────────────────────────────────────────────────
builder.Services.AddTransient<CreateRfpHandler>();
builder.Services.AddTransient<AddLaneHandler>();
builder.Services.AddTransient<OpenRfpHandler>();
builder.Services.AddTransient<SubmitBidHandler>();
builder.Services.AddTransient<AwardCarrierHandler>();
builder.Services.AddTransient<GetRfpHandler>();
builder.Services.AddTransient<ActiveBidsQuery>();

// ── Redis (idempotency) ───────────────────────────────────────────────────────
builder.Services.AddStackExchangeRedisCache(options =>
    options.Configuration = redisConn);

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddNpgSql(rfpDb,    tags: ["rfp_db"])
    .AddRedis(redisConn, tags: ["redis"]);

// ── OpenTelemetry ─────────────────────────────────────────────────────────────
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAspNetCoreInstrumentation());

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

// ── Migrations ────────────────────────────────────────────────────────────────
// ⚠ TRADE-OFF: runs on every startup for local dev convenience.
// NEVER do this in production — use a CI/CD step or init container.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
}

// ── Middleware pipeline ───────────────────────────────────────────────────────
app.UseMiddleware<DomainExceptionMiddleware>();
app.UseMiddleware<IdempotencyMiddleware>();

// ── REST endpoints ────────────────────────────────────────────────────────────

// POST /rfps → 201 Created
app.MapPost("/rfps", async (
    CreateRfpCommand             command,
    IValidator<CreateRfpCommand> validator,
    CreateRfpHandler             handler,
    CancellationToken            ct) =>
{
    var result = await validator.ValidateAsync(command, ct);
    if (!result.IsValid)
        return Results.ValidationProblem(result.ToDictionary());

    var id = await handler.HandleAsync(command, ct);
    return Results.Created($"/rfps/{id.Value}", new { id = id.Value });
});

// POST /rfps/{rfpId}/lanes → 201 Created
app.MapPost("/rfps/{rfpId:guid}/lanes", async (
    Guid                        rfpId,
    AddLaneCommand              command,
    IValidator<AddLaneCommand>  validator,
    AddLaneHandler              handler,
    CancellationToken           ct) =>
{
    var result = await validator.ValidateAsync(command, ct);
    if (!result.IsValid)
        return Results.ValidationProblem(result.ToDictionary());

    var laneId = await handler.HandleAsync(rfpId, command, ct);
    return Results.Created($"/rfps/{rfpId}/lanes/{laneId.Value}", new { id = laneId.Value });
});

// POST /rfps/{rfpId}/open → 200 OK
app.MapPost("/rfps/{rfpId:guid}/open", async (
    Guid              rfpId,
    OpenRfpHandler    handler,
    CancellationToken ct) =>
{
    await handler.HandleAsync(rfpId, ct);
    return Results.Ok();
});

// POST /rfps/{rfpId}/bids → 201 Created  (Idempotency-Key checked by middleware)
app.MapPost("/rfps/{rfpId:guid}/bids", async (
    Guid                         rfpId,
    SubmitBidCommand             command,
    IValidator<SubmitBidCommand> validator,
    SubmitBidHandler             handler,
    CancellationToken            ct) =>
{
    var result = await validator.ValidateAsync(command, ct);
    if (!result.IsValid)
        return Results.ValidationProblem(result.ToDictionary());

    var bidId = await handler.HandleAsync(rfpId, command, ct);
    return Results.Created($"/rfps/{rfpId}/bids/{bidId.Value}", new { id = bidId.Value });
});

// GET /rfps/{rfpId}/bids → 200 OK  (Dapper hot path — lowest bid per lane first)
app.MapGet("/rfps/{rfpId:guid}/bids", async (
    Guid            rfpId,
    ActiveBidsQuery query,
    CancellationToken ct) =>
{
    var bids = await query.ExecuteAsync(rfpId, ct);
    return Results.Ok(bids);
});

// POST /rfps/{rfpId}/awards → 202 Accepted  (triggers Saga via Outbox)
app.MapPost("/rfps/{rfpId:guid}/awards", async (
    Guid                rfpId,
    AwardCarrierCommand command,
    AwardCarrierHandler handler,
    CancellationToken   ct) =>
{
    await handler.HandleAsync(rfpId, command, ct);
    return Results.Accepted();
});

// GET /rfps/{rfpId} → 200 OK  (compiled query — no LINQ parsing per call)
app.MapGet("/rfps/{rfpId:guid}", async (
    Guid              rfpId,
    GetRfpHandler     handler,
    CancellationToken ct) =>
{
    var dto = await handler.HandleAsync(rfpId, ct);
    return Results.Ok(dto);
});

// ── Health checks ─────────────────────────────────────────────────────────────
app.MapHealthChecks("/health");  // liveness — is the process alive?
app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Count > 0  // readiness — all deps reachable?
});

app.Run();

