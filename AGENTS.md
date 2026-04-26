# FreightFlow — Agent Instructions

FreightFlow is a **backend-only .NET 10 / C# 13 learning project** — a freight-procurement platform
covering ASP.NET Core, polyglot persistence, messaging, DDD, and distributed systems.
Everything runs via Docker Compose; no cloud account is required.

Full context: [FreightFlow-Proposal.md](FreightFlow-Proposal.md) · Build order: [BUILDPLAN.md](BUILDPLAN.md)

---

## Solution layout

```
FreightFlow.slnx
src/
  FreightFlow.RfpApi/          — Vertical Slice; Features/<SliceName>/{Endpoint,Command,Handler,Validator}.cs
  FreightFlow.CarrierApi/      — Minimal API; gRPC server; Outbox
  FreightFlow.WorkflowWorker/  — Clean Architecture: Domain/ Application/ Infrastructure/
  FreightFlow.Gateway/         — YARP reverse proxy; JWT; rate limiting
  FreightFlow.SharedKernel/    — Value objects, strongly-typed IDs, message contracts ONLY (no infra deps)
proto/
  capacity.proto               — single gRPC contract (owned here; referenced by both sides)
tests/
  FreightFlow.Domain.Tests/    — pure domain unit tests; zero infrastructure
docs/
  walkthrough.http             — VS Code REST Client; full happy-path sequence
docker-compose.stage1.yml
docker-compose.stage2.yml      — overlay (Kafka + analytics worker)
```

---

## Key commands

```bash
# Start everything (Stage 1)
docker compose -f docker-compose.stage1.yml up --build

# Health checks / verify milestone
docker compose ps
curl localhost:5000/health   # rfp-api
curl localhost:5001/health   # carrier-api
curl localhost:5002/health   # workflow-worker

# API explorer (via Gateway)
open http://localhost:8080/scalar

# RabbitMQ management UI  (guest / guest)
open http://localhost:15672

# Run unit tests (domain layer — no infra)
dotnet test tests/FreightFlow.Domain.Tests/
```

---

## Architecture rules

| Service | Style | Why |
|---|---|---|
| `FreightFlow.RfpApi` | Vertical Slice | High feature-change velocity; each slice is self-contained |
| `FreightFlow.WorkflowWorker` | Clean Architecture | Saga logic is stable and must be unit-testable without infrastructure |
| `FreightFlow.CarrierApi` | Minimal API (no strict style) | Fewer features; CRUD + gRPC server |

**Vertical Slice rule:** No service class may span two feature folders. A change to `SubmitBid` must never require touching `CreateRfp`.

**Clean Architecture rule:** `Domain/` and `Application/` layers must have zero references to `Infrastructure/`. Test domain logic with no infra.

**Aggregate roots:** `RFP` (RfpApi) · `Carrier` (CarrierApi) · `Contract` (WorkflowWorker).

**Messaging:** RabbitMQ (MassTransit + Outbox) for all async events and commands. gRPC for the single internal sync call: WorkflowWorker → CarrierApi (`ReserveCapacity`, contract in `proto/capacity.proto`). All external surfaces are REST.

**Resilience (WorkflowWorker outbound calls):** Polly v8 pipeline — retry (3×, exponential + jitter, max 8 s) → circuit breaker (5 failures → open, 30 s half-open) → timeout (5 s/attempt). MassTransit consumers: 3 retries then dead-letter queue.

---

## Skills — load these for implementation tasks

| When you are... | Load this skill |
|---|---|
| Writing any C# code, reviewing structure, wiring DI | `freightflow-coding-standards` |
| Touching domain models, aggregates, events, invariants | `freightflow-domain` |
| Writing EF Core config, migrations, Dapper, Outbox, idempotency | `freightflow-persistence` |
| Writing or reviewing tests | `freightflow-testing` |

---

## Port map (Stage 1)

| Service | Port |
|---|---|
| freight-gateway | 8080 |
| freight-rfp-api | 5000 |
| freight-carrier-api | 5001 (REST) · 5011 (gRPC) |
| freight-workflow-worker | 5002 (health only) |
| postgres-rfp | 5432 |
| postgres-carrier | 5433 |
| redis | 6379 |
| rabbitmq | 5672 · 15672 (mgmt) |
