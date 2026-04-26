---
name: freightflow-coding-standards
description: >
  FreightFlow C# coding standards, structural conventions, and DI wiring rules. Use when writing,
  reviewing, or scaffolding any C# code in this codebase: handlers, endpoints, domain models,
  infrastructure classes, or Program.cs wiring. Triggers on: "implement a feature", "create a
  service class", "add an endpoint", "how should I structure", "review this code", "new project
  file". Also triggers before writing any .NET 10 / ASP.NET Core / EF Core API call.
argument-hint: '[service, handler, or file to create/review]'
---

# FreightFlow — C# Coding Standards

## Before Writing Any Code

1. Verify any ASP.NET Core, EF Core, or Npgsql API you're about to use via the **Microsoft Learn MCP tool** — .NET 10 API coverage in training data may be incomplete (e.g. `IExceptionHandler` suppression behaviour changed, `dotnet new sln` now defaults to `.slnx`).
2. If the code touches a domain concept, open [freightflow-domain](..freightflow-domain/SKILL.md) for exact ubiquitous language and invariants.
3. Check the milestone dependency graph in [BUILDPLAN.md](../../../BUILDPLAN.md) — don't implement anything that depends on an unverified upstream milestone.

## Non-Negotiables

| Concern | Convention |
|---|---|
| Runtime | .NET 10, C# 13 |
| Solution file | `.slnx` (not `.sln`) — default in .NET 10 |
| Class default | `sealed` unless designed for inheritance |
| Value objects / DTOs | `record` or `readonly record struct` |
| Aggregates / services | `class` |
| Constructors | Classic only — **no primary constructor syntax** inside a class body |
| Namespaces | File-scoped: `namespace FreightFlow.RfpApi.Features.CreateRfp;` |
| Private fields | `_camelCase` |
| Async suffix | Always: `HandleAsync`, `ExecuteAsync`, `SaveChangesAsync` |
| Allowed abbreviations | `Id`, `Dto`, `Rfp`, `Tms` — no others |

## Logging

Inject `ILogger<T>` — never `Serilog.ILogger` directly. Use **named** placeholders (never positional `{0}`):

```csharp
_logger.LogInformation("Bid {BidId} submitted for RFP {RfpId} by carrier {CarrierId}",
    bid.Id, rfpId, carrierId);
```

Do not log inside domain methods — they are infrastructure-free. Log at handler entry/exit for commands that matter.

Wire Serilog once in `Program.cs`:

```csharp
builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.WithProperty("ServiceName", "freight-rfp-api")
       .Enrich.FromLogContext()
       .WriteTo.Console(new JsonFormatter()));
```

## Error Handling

Use `DomainExceptionMiddleware` — **not** `IExceptionHandler` (explicit learning-project choice). It maps `DomainException` → 422 and `NotFoundException` → 404 as RFC 7807 Problem Details. Register **before** `app.UseRouting()`. Never add try/catch in handlers for domain exceptions.

## DI Lifetimes

| Type | Lifetime | Note |
|---|---|---|
| `DbContext` | Scoped | Never capture in a Singleton |
| `BackgroundService` | Singleton | Use `IServiceScopeFactory` to resolve scoped deps |
| `NpgsqlDataSource` | Singleton | Manages connection pool; resolves Dapper connections |
| `IProducer<K,V>` (Kafka) | Singleton | Expensive to build; shared across requests |
| Handlers / use-case classes | Transient | Stateless; created per request |
