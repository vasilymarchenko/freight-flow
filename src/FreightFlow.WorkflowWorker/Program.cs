using FluentMigrator.Runner;
using FreightFlow.CarrierApi.Grpc;
using FreightFlow.WorkflowWorker.Application;
using FreightFlow.WorkflowWorker.Infrastructure.Activities;
using FreightFlow.WorkflowWorker.Infrastructure.GrpcClients;
using FreightFlow.WorkflowWorker.Infrastructure.Persistence;
using Grpc.Net.Client;
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
       .Enrich.WithProperty("ServiceName", "freight-workflow-worker")
       .Enrich.FromLogContext()
       .WriteTo.Console(new CompactJsonFormatter()));

// ── Config ────────────────────────────────────────────────────────────────────
var rfpDb         = builder.Configuration.GetConnectionString("RfpDb")!;
var rabbitHost    = builder.Configuration["RabbitMq:Host"]!;
var rabbitUser    = builder.Configuration["RabbitMq:Username"]!;
var rabbitPass    = builder.Configuration["RabbitMq:Password"]!;
var grpcEndpoint  = builder.Configuration["CarrierGrpc:Endpoint"]!;

// ── EF Core (rfp_db — saga state + contracts) ─────────────────────────────────
builder.Services.AddDbContext<WorkflowDbContext>(options =>
    options.UseNpgsql(rfpDb));

// ── FluentMigrator (runs M003 + M004 on rfp_db) ───────────────────────────────
builder.Services
    .AddFluentMigratorCore()
    .ConfigureRunner(r => r
        .AddPostgres()
        .WithGlobalConnectionString(rfpDb)
        .ScanIn(typeof(Program).Assembly).For.Migrations());

// ── gRPC client + CarrierCapacityClient ──────────────────────────────────────
// gRPC in Docker uses HTTP/2 without TLS on the internal network.
builder.Services.AddSingleton(_ =>
{
    var channel = GrpcChannel.ForAddress(grpcEndpoint, new GrpcChannelOptions
    {
        HttpHandler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true
        }
    });
    return new CapacityService.CapacityServiceClient(channel);
});
builder.Services.AddTransient<CarrierCapacityClient>();

// ── State machine activities (resolved from DI by MassTransit via interface) ─
builder.Services.AddTransient<IReserveCapacityActivity,  ReserveCapacityActivity>();
builder.Services.AddTransient<IIssueContractActivity,    IssueContractActivity>();
builder.Services.AddTransient<INotifyShipperActivity,    NotifyShipperActivity>();
builder.Services.AddTransient<IMarkRfpAwardedActivity,   MarkRfpAwardedActivity>();
builder.Services.AddTransient<ICompensateWorkflowActivity, CompensateWorkflowActivity>();

// ── MassTransit / RabbitMQ / Saga ────────────────────────────────────────────
builder.Services.AddMassTransit(x =>
{
    // Saga state machine backed by the EF Core repository.
    x.AddSagaStateMachine<AwardWorkflowStateMachine, AwardWorkflowState>()
        .EntityFrameworkRepository(r =>
        {
            r.ConcurrencyMode = ConcurrencyMode.Optimistic;
            r.AddDbContext<DbContext, WorkflowDbContext>((sp, opt) =>
                opt.UseNpgsql(rfpDb));
        });

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(rabbitHost, "/", h =>
        {
            h.Username(rabbitUser);
            h.Password(rabbitPass);
        });

        // Retry 3× before dead-lettering a failed consumer message.
        cfg.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4)));

        cfg.ConfigureEndpoints(ctx);
    });
});

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddNpgSql(rfpDb, tags: ["rfp_db"])
    .AddRabbitMQ(
        rabbitConnectionString: $"amqp://{rabbitUser}:{rabbitPass}@{rabbitHost}/",
        tags: ["rabbitmq"]);

// ── OpenTelemetry ─────────────────────────────────────────────────────────────
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAspNetCoreInstrumentation());

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

// ── Migrations ────────────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
}

// ── Health endpoints ──────────────────────────────────────────────────────────
app.MapHealthChecks("/health");
app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Count > 0
});

app.Run();

