---
name: freightflow-domain
description: >
  FreightFlow domain model conventions and ubiquitous language reference. Use when writing or
  reviewing any domain layer code: aggregate roots, value objects, domain events, domain
  exceptions, strongly-typed IDs, or SharedKernel additions. Also use when unsure of the correct
  domain term, aggregate boundary, or invariant. Triggers on: "implement the RFP aggregate",
  "add a domain event", "create a value object", "what invariant applies here", "add to
  SharedKernel", "raise a domain exception", "domain event dispatch".
argument-hint: '[aggregate, value object, or domain concept to implement]'
user-invocable: false
---

# FreightFlow — Domain Conventions

For ubiquitous language glossary, full aggregate specs, bounded contexts, and the complete
invariant list, read [references/domain.md](./references/domain.md).

## Strongly-Typed IDs

All aggregate and entity IDs are strongly typed — **never pass raw `Guid`** at domain boundaries:

```csharp
public readonly record struct RfpId(Guid Value)
{
    public static RfpId New()             => new(Guid.NewGuid());
    public static RfpId From(Guid value)  => new(value);
    public override string ToString()     => Value.ToString();
}
// Apply the same pattern to: CarrierId, LaneId, BidId, ContractId, CapacityRecordId
```

## Domain Events

- Collected on the aggregate root in a `List<IDomainEvent>` — **no MediatR in domain classes**
- Dispatched by `DomainEventDispatcherInterceptor` (a `SaveChangesInterceptor`) after `SaveChanges` commits
- Pattern: interceptor reads `ChangeTracker`, drains and clears each aggregate's event list, dispatches via `IServiceProvider`

## Domain Exceptions

Throw domain-specific exceptions — **never** `ArgumentException` or `InvalidOperationException`:

```
DomainException (base)           → HTTP 422
  ├─ RfpNotOpenException
  ├─ MaxBidRoundsExceededException
  ├─ CarrierNotActiveException
  └─ InsufficientCapacityException

NotFoundException                → HTTP 404
  ├─ RfpNotFoundException
  └─ CarrierNotFoundException
```

All exceptions live in SharedKernel. `DomainExceptionMiddleware` in each API maps them to RFC 7807 responses — no try/catch in handlers.

## SharedKernel Rules

- **No infrastructure references** — zero EF Core, MassTransit, or Npgsql imports
- Contains: `IDomainEvent`, `AggregateRoot` base, strongly-typed IDs, value objects (`Money`, `ZipCode`, `FreightClass`, `DotNumber`), domain exception hierarchy, `OutboxDispatcher<TDbContext>`, message contracts
- All public types default to `internal`; expose to tests via `[assembly: InternalsVisibleTo("FreightFlow.Domain.Tests")]`
- Structure is NuGet-ready — keep the extraction cost near zero
