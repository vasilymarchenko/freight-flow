using FluentMigrator.Runner;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.WithProperty("ServiceName", "freight-rfp-api")
       .Enrich.FromLogContext()
       .WriteTo.Console(new CompactJsonFormatter()));

builder.Services.AddHealthChecks();

builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAspNetCoreInstrumentation());

var connectionString = builder.Configuration.GetConnectionString("RfpDb");
if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services
        .AddFluentMigratorCore()
        .ConfigureRunner(r => r
            .AddPostgres()
            .WithGlobalConnectionString(connectionString)
            .ScanIn(typeof(Program).Assembly).For.Migrations());
}

var app = builder.Build();

// ⚠ TRADE-OFF: migrations run on every startup for local development convenience.
// This is intentional for this learning project — single instance, no concurrent deploys.
// NEVER do this in production: use a CI/CD pipeline step, a Kubernetes Job, or an
// init container that runs migrations once before the main container starts.
if (!string.IsNullOrEmpty(connectionString))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
}

app.MapHealthChecks("/health");
app.MapHealthChecks("/ready");

app.Run();
