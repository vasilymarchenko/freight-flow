---
name: freightflow-persistence
description: >
  FreightFlow persistence patterns, data access conventions, and infrastructure wiring rules. Use
  when writing EF Core configuration, Dapper queries, FluentMigrator migrations, Transactional
  Outbox logic, Redis idempotency keys, or Kestrel gRPC/REST port configuration. Triggers on:
  "add a migration", "configure EF Core", "write a Dapper query", "implement outbox", "add
  idempotency", "handle concurrency conflict", "configure gRPC port", "map xmin", "owned entity".
argument-hint: '[entity, migration, or data access pattern to implement]'
---

# FreightFlow — Persistence Patterns

## EF Core

- All **read** endpoints: `AsNoTracking()` + `.Select(projection)` — never return an EF entity directly
- Compiled queries on hot-path reads (pays the LINQ parse cost once at startup):

  ```csharp
  private static readonly Func<RfpDbContext, Guid, Task<RfpDto?>> GetRfpQuery =
      EF.CompileAsyncQuery((RfpDbContext db, Guid id) =>
          db.Rfps.AsNoTracking()
                 .Where(r => r.Id == id)
                 .Select(r => new RfpDto { ... })
                 .FirstOrDefault());
  ```

- Do **not** expose `DbSet<CapacityRecord>` — configure as `OwnsMany` on `Carrier` and access only through the aggregate. This enforces the aggregate boundary at the persistence level.

## Optimistic Concurrency — `xmin`

Use PostgreSQL's built-in `xmin` system column — do **not** add a `RowVersion` column:

```csharp
// EF Core Fluent API
Property(x => x.Version).IsRowVersion().HasColumnName("xmin");
```

Map `DbUpdateConcurrencyException` → `HTTP 409 Conflict` with `Retry-After: 1` header in the exception middleware.

## Dapper

Register `NpgsqlDataSource` as singleton and inject it — never instantiate `NpgsqlConnection` directly:

```csharp
// Program.cs
builder.Services.AddNpgsqlDataSource(connectionString); // Npgsql manages the pool

// Usage in feature (e.g. SubmitBid/ActiveBidsQuery.cs)
public sealed class ActiveBidsQuery(NpgsqlDataSource db)
{
    public async Task<IReadOnlyList<BidSummary>> ExecuteAsync(RfpId rfpId)
    {
        await using var conn = await db.OpenConnectionAsync();
        return [.. await conn.QueryAsync<BidSummary>(Sql, new { rfpId = rfpId.Value })];
    }
}
```

Dapper queries live **inside the feature folder they serve** (e.g. `Features/SubmitBid/ActiveBidsQuery.cs`). Use Dapper only for hot-path reads with known, static SQL — not for writes or complex queries.

## FluentMigrator

Every migration must have **both** `Up()` and `Down()`. Run `IMigrationRunner` in `Program.cs` **before** the app starts serving requests (`app.Run()`).

## Transactional Outbox

The Outbox row must be written in the **same `SaveChangesAsync()` call** as the domain entity — never call `SaveChanges` twice:

```csharp
// WRONG — message is lost if the process crashes between the two calls
await _db.SaveChangesAsync();
_db.OutboxMessages.Add(outboxRow);
await _db.SaveChangesAsync();

// CORRECT — atomic
_db.Rfps.Add(rfp);
_db.OutboxMessages.Add(outboxRow);
await _db.SaveChangesAsync();
```

`OutboxDispatcher<TDbContext>` in SharedKernel handles polling, publishing, and marking sent:
1. `SELECT WHERE sent_at IS NULL ORDER BY created_at LIMIT 50`
2. Publish each via `IPublishEndpoint` (MassTransit)
3. `UPDATE outbox SET sent_at = NOW() WHERE id = @id`

Poll interval is configurable via `IOptionsMonitor<OutboxOptions>` (default: 5 s).

## Redis Idempotency

Store the **full response** (status code + body as JSON) — not just a presence flag. Duplicate requests within the TTL window receive the exact original response:

```csharp
// Key pattern: idempotency:{idempotencyKey}   TTL: 86400 s
await cache.SetAsync(key, Serialize(new { status, body }),
    new DistributedCacheEntryOptions { AbsoluteExpireTime = TimeSpan.FromDays(1) });
```

## Kestrel — gRPC + REST Port Split

gRPC and REST **must** use separate ports with different HTTP protocols. HTTP/2-only on the REST port breaks `curl` health checks:

```json
// appsettings.json
"Kestrel": {
  "Endpoints": {
    "Rest":  { "Url": "http://*:5001", "Protocols": "Http1AndHttp2" },
    "Grpc":  { "Url": "http://*:5011", "Protocols": "Http2" }
  }
}
```
