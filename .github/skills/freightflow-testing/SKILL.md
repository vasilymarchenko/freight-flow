---
name: freightflow-testing
description: >
  FreightFlow testing conventions, patterns, and edge cases. Use when writing or reviewing unit
  tests, integration tests, or handler tests in this project. Triggers on: "write tests for",
  "add unit tests", "test this aggregate", "test this handler", "what should I test", "test the
  outbox", "test the saga", "test idempotency", "test concurrency", "test domain invariant".
argument-hint: '[class, aggregate, handler, or scenario to test]'
---

# FreightFlow — Testing Conventions

**Stack:** xUnit + Shouldly + Moq

## Test Layers

| Layer | Scope | Constraint |
|---|---|---|
| Domain | `FreightFlow.Domain.Tests` | Zero infrastructure; must complete in < 1 s total |
| Handler | Per-service test project | Mock the `DbContext` boundary; test orchestration logic |
| Integration | Per-service test project | `WebApplicationFactory` + Testcontainers for real DB and broker |

If a domain rule requires a DB or broker to test, it is in the wrong layer — move it to the domain first.

## Domain Test Pattern

Every invariant gets both a **positive case** (action succeeds) and a **negative case** (throws):

```csharp
public sealed class RfpAggregateTests
{
    [Fact]
    public void SubmitBid_WhenRfpNotOpen_ThrowsDomainException()
    {
        var rfp = RfpFactory.CreateDraft();
        var act = () => rfp.SubmitBid(CarrierId.New(), []);

        act.Should().Throw<DomainException>().WithMessage("*not open*");
    }

    [Fact]
    public void SubmitBid_WhenMaxRoundsExceeded_ThrowsDomainException()
    {
        var rfp = RfpFactory.CreateOpen(maxBidRounds: 1);
        rfp.SubmitBid(CarrierId.New(), SomeLanePrices());

        var act = () => rfp.SubmitBid(CarrierId.New(), SomeLanePrices());

        act.Should().Throw<DomainException>().WithMessage("*maximum bid rounds*");
    }
}
```

## Edge Cases — Always Cover These

| Scenario | What to assert |
|---|---|
| Outbox dispatcher crashes after publish but before `sent_at` update | Message is re-published on next poll (at-least-once delivery is acceptable) |
| Two concurrent mutations to the same aggregate | `DbUpdateConcurrencyException` surfaces correctly as HTTP 409 with `Retry-After` |
| Duplicate request with same `Idempotency-Key` | Cached response returned; handler body **not** re-executed |
| Saga step N fails after steps 1..N-1 succeeded | Compensation steps execute for all completed steps in reverse order |

## What NOT to Test

- Happy paths that merely confirm a method returns what it was given
- Private method implementations or internal field values
- Every property on a `record` DTO round-trips correctly (trust the compiler)
- Anything that requires infrastructure in a domain-layer test — fix the layering instead
