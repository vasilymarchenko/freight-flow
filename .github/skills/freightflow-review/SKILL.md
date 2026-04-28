---
name: freightflow-review
description: >
  FreightFlow code review workflow. Use when asked to validate, audit, or review uncommitted
  changes, a milestone, a PR, or any set of files in this codebase. Triggers on: "validate
  these changes", "review milestone N", "are there any issues", "design smell", "anti-patterns",
  "is this well-designed", "what did I miss".
argument-hint: '[files, milestone, or area to review — e.g. "milestone 4 changes", "WorkflowWorker saga"]'
---

# FreightFlow — Review Workflow

## Process (always follow this order)

1. **Identify the source of truth** — BUILDPLAN.md milestone, user-stated goal, or ticket. If none, ask before proceeding.
2. **Read all changed files.** Do not form opinions from file names alone.
3. **Review in three passes** — Before (what existed), Changes (what moved), After (final state coherent with codebase?).
4. **Classify every finding** and report the complete list before touching any code.
5. **Wait for approval.** Fix only what the user confirms. Never bundle unrequested refactors.
6. **After each fix**, run `dotnet build` + `dotnet test tests/` (all test projects) and report before moving on.

## Severity

| Label | Meaning | Action |
|---|---|---|
| 🐛 Bug | Incorrect behaviour or data loss risk | Propose fix; implement on approval |
| 🏗️ Design | Architecture or SOLID violation | Explain + concrete fix; implement on approval |
| 🧹 Smell | Maintainability concern | Note it; fix only if asked |

## Pass 1 — Does It Solve the Problem?

- Map each changed file to a requirement from the source of truth.
- Flag any requirement with no corresponding change as 🐛 (missing implementation).
- Flag any change with no corresponding requirement as 🧹 (scope creep).

## Pass 2 — Architecture & Design

**Check by service style:**

| Service | Style | Key invariant |
|---|---|---|
| `RfpApi` | Vertical Slice | No type references across `Features/SliceA/` ↔ `Features/SliceB/`; each slice owns its own request/response/handler |
| `WorkflowWorker` | Clean Architecture | `Domain/` and `Application/` have zero `using` from `Infrastructure/`; state machine references activities via marker interfaces in `Application/ActivityContracts.cs` only |
| `CarrierApi` | Minimal API | No strict layering required; domain mutations stay inside aggregate methods |
| `SharedKernel` | Contracts only | No infrastructure `using` (EF, MassTransit, Grpc, Dapper) — value objects, IDs, and message contracts only |

**Aggregate ownership:** `RFP` mutated only in `RfpApi`; `Carrier` only in `CarrierApi`; `Contract` only in `WorkflowWorker`.

**SOLID — flag only non-obvious violations:**
- **SRP**: Consumer that persists + publishes + updates saga state in one method body.
- **OCP**: Handler with `if/switch` on message type instead of separate consumers per contract.
- **DIP**: State machine `new`-ing activities directly; `Program.cs` registering concrete→concrete instead of interface→concrete.
- **ISP**: Activity implementing both raw `IStateMachineActivity<T>` overloads instead of a single marker interface.

**Pattern applicability — check if missing:**
- **Outbox**: Any consumer that publishes without a transactional outbox is a dual-write risk.
- **Idempotency**: Any POST/PUT handler that creates or mutates without an idempotency key check.
- **Polly pipeline**: Any outbound gRPC or HTTP call not routed through `CarrierCapacityClient`'s resilience pipeline.
- **Saga intermediate state**: Any saga step that transitions directly to a final state before an external system acknowledges.

## Pass 3 — C# / .NET Bugs

| Pattern | Signal |
|---|---|
| Unguarded `.First()` / `.Single()` | On collections from domain state in handlers |
| Missing `CancellationToken` | `SaveChangesAsync()`, `*Async()` DB/gRPC calls without token propagation |
| Activity cast | `IBehavior<TInstance, TData>` downcast to `IBehavior<TInstance>` in `Execute` |
| Dead code contracts | `record` in `SagaEvents.cs` / `MessageContracts.cs` declared but never correlated or consumed |
| Consumer no-op | Consumer logs but never persists or publishes |
| Blocking calls | `.Result`, `.Wait()`, `Task.Run()` wrapping async work |

## Pass 4 — Tests

**Meaningful logic** — flag any test that only asserts framework behaviour (record round-trip, constructor field, simple getter). Every test must assert a business rule or an integration contract.

**Coverage gaps:**
| Gap | Signal |
|---|---|
| Saga compensation path | No test fires a timeout on a stuck saga and asserts `Compensated` |
| Duplicate message | No test re-delivers the same event and asserts idempotent outcome |
| Domain negative cases | A `throw new DomainException` with no corresponding negative `[Fact]` |
| Consumer side-effects | Consumer persists + publishes but test only asserts one side-effect |

**Test bugs:**
- `Task.Delay` for synchronization → `SagaHarness.Exists(corrId, sm => sm.TargetState, timeout)`
- Assertion without a failure message on non-obvious conditions → add a string description
- Test that passes vacuously (empty Act, no Assert, or always-true condition)

## Cross-Skill References

- Load **freightflow-coding-standards** before proposing any C# fix.
- Load **freightflow-testing** before proposing or writing any test.
- Load **freightflow-domain** if the finding touches an aggregate invariant, domain event, or value object.
