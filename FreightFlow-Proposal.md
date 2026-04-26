# FreightFlow — Revised Project Proposal

> **Freight Procurement Platform** — a backend-only learning project covering ASP.NET Core,
> polyglot persistence, messaging, distributed systems, DDD, and clean/vertical-slice architecture.
> Runs entirely in Docker Compose — no cloud account required.

---

## Table of Contents

1. [Problem Statement](#1-problem-statement)
2. [Build Stages](#2-build-stages)
3. [System Overview — Stage 1](#3-system-overview--stage-1)
4. [Core Domain](#4-core-domain)
5. [End-to-End Flow](#5-end-to-end-flow)
6. [Architecture](#6-architecture)
7. [Technology Stack](#7-technology-stack)
8. [Key Engineering Patterns](#8-key-engineering-patterns)
9. [Observability](#9-observability)
10. [Out of Scope](#10-out-of-scope)
11. [Stage 2 — Kafka Extension](#11-stage-2--kafka-extension)
12. [Quick Start](#12-quick-start)

---

## 1. Problem Statement

The freight procurement process in the trucking industry is broken at the workflow
level. When a company needs to ship goods, it must negotiate rates with dozens or hundreds of
trucking carriers. Today that process happens through emails, spreadsheets, and phone calls — it is
slow, opaque, and produces sub-optimal rates because neither shippers nor carriers have reliable
market benchmarks to anchor their decisions.

**Shippers** face three specific problems:

- No single place to run a structured Request-for-Proposal (RFP) across many carriers simultaneously.
- No reliable market-rate benchmark to know whether a bid is fair.
- Manual, error-prone award decisions once bids arrive.

**Carriers** face the mirror problems:

- Discovering relevant RFPs requires relationships rather than technology.
- No standardised digital channel to submit bids and receive awards.
- Fragmented onboarding — each shipper requires different documentation and compliance checks.

FreightFlow digitises this process end-to-end: structured RFP creation, carrier discovery and bid
submission, rate benchmarking, contract award, and post-award workflow execution.

---

## 2. Build Stages

The project is split into two explicit stages. Stage 1 is the interview-ready core and covers all
must-have competencies. Stage 2 adds Kafka-based event streaming and is explicitly optional —
to be built only if time permits after Stage 1 is solid.

### Stage 1 — Must Have (interview core)

| Service | Responsibility |
|---|---|
| `freight-rfp-api` | Shipper-facing HTTP API. RFPs, lanes, bid lifecycle. Vertical Slice + EF Core + Dapper + Outbox. |
| `freight-carrier-api` | Carrier-facing HTTP API. Onboarding, bid submission, idempotency. |
| `freight-workflow-worker` | Saga orchestrator. Award workflow with compensating transactions. Clean Architecture. |
| `freight-gateway` | YARP reverse proxy. JWT validation, rate limiting, routing. |

**Covers:** ASP.NET Core (Minimal APIs, middleware, DI, health checks, problem details), EF Core +
Dapper (polyglot ORM usage), PostgreSQL + Redis (relational + key-value), RabbitMQ + MassTransit
(messaging, outbox, DLQ, retry), gRPC (internal sync call), DDD aggregates, Vertical Slice,
Clean Architecture, Saga pattern, Polly resilience, OpenTelemetry.

### Stage 2 — If Time Permits (Kafka extension)

| Service | Responsibility |
|---|---|
| `freight-analytics-worker` | Consumes rate events from Kafka. Builds market benchmarking indices. |

Adds Kafka producer (in `freight-carrier-api`) and Kafka consumer + partitioned PostgreSQL analytics
store. Documented separately in [Section 11](#11-stage-2--kafka-extension).

> **Note on Kafka:** I have no production Kafka experience. Stage 2 exists to change that in a
> controlled learning context — not to fake familiarity in an interview. If asked, I will be honest:
> "I've worked through it in a learning project but have not operated it in production." That is
> a better answer than bluffing.

---

## 3. System Overview — Stage 1

### Services

```
                      ┌──────────────────────────────────────┐
                      │           freight-gateway             │
                      │       (YARP · JWT · rate limit)       │
                      └──────────┬───────────────┬───────────┘
                                 │               │
                    ┌────────────▼───┐   ┌───────▼────────────┐
                    │  freight-      │   │  freight-           │
                    │  rfp-api       │   │  carrier-api        │
                    │  (Minimal API) │   │  (Minimal API)      │
                    └────┬───────────┘   └──────┬─────────────┘
                         │                      │
              ┌──────────▼──────────────────────▼──────────┐
              │           RabbitMQ (MassTransit)            │
              │   commands · domain events · DLQ · Outbox   │
              └──────────────────┬──────────────────────────┘
                                 │
              ┌──────────────────▼──────────────────────────┐
              │         freight-workflow-worker              │
              │   Saga StateMachine · gRPC → carrier-api    │
              └─────────────────────────────────────────────┘
```

### Infrastructure (Stage 1 only)

| Container | Image | Port(s) |
|---|---|---|
| `postgres-rfp` | `postgres:16` | 5432 |
| `postgres-carrier` | `postgres:16` | 5433 |
| `redis` | `redis:7-alpine` | 6379 |
| `rabbitmq` | `rabbitmq:3.13-management` | 5672 / 15672 |
| `freight-rfp-api` | *(build from source)* | 5000 |
| `freight-carrier-api` | *(build from source)* | 5001 |
| `freight-workflow-worker` | *(build from source)* | 5002 (health only) |
| `freight-gateway` | *(build from source)* | 8080 |

Everything starts with a single command: `docker compose up --build`. No cloud account required.

---

## 4. Core Domain

The domain follows the freight-procurement ubiquitous language used in the industry.

| Term | Definition |
|---|---|
| **Shipper** | A company that needs to move goods. Creates RFPs and awards contracts. |
| **Carrier** | A trucking company. Onboards to the platform, discovers RFPs, and submits bids. |
| **RFP** | Request for Proposal. A structured solicitation covering one or more Lanes, with an open/close date and a maximum bid-round count. |
| **Lane** | A shipping route: origin ZIP → destination ZIP, freight class, expected volume. An RFP contains 1–500 lanes. |
| **Bid** | A carrier's price offer for one or more lanes in an open RFP. Immutable once submitted; a new bid round creates a new Bid. |
| **Award** | The shipper's decision to accept a specific Bid. Triggers the award workflow Saga. |
| **Contract** | The binding record produced by a completed award workflow. Links Shipper, Carrier, Lane, and agreed rate. |
| **Rate Event** | An immutable fact emitted for every bid submitted. In Stage 1 stored directly; in Stage 2 streamed through Kafka. |

### Aggregate boundaries (DDD)

```
RFP aggregate root
  └─ Lane[]      (value objects — origin, destination, freight class, volume)
  └─ Bid[]       (entities — carrier ref, round, per-lane prices, submitted-at)
  └─ Award       (entity — winning bid ref, awarded-at)

Carrier aggregate root
  └─ CapacityRecord   (entity — lane, available volume, reservation state)
  └─ ProfileDocument  (value object — variable-attribute jsonb, see §8.2)

Contract aggregate root  (created by Saga, read-only after creation)
```

---

## 5. End-to-End Flow

### 5.1 Carrier onboarding

A carrier registers via the Carrier API. The API:

1. Validates the FMCSA SAFER registration number (stub in local dev).
2. Stores structured fields (DOT number, authority status, insurance expiry) in PostgreSQL.
3. Stores the variable-attribute carrier profile as a `jsonb` column in the same PostgreSQL database
   (see §8.2 for the rationale and how this maps to Cosmos DB in production).
4. Publishes a `CarrierOnboarded` domain event to RabbitMQ via the Transactional Outbox.

### 5.2 RFP lifecycle

```
Shipper                RFP API              RabbitMQ            Carrier API
   │                      │                     │                    │
   │── POST /rfps ────────►│                     │                    │
   │                      │── write RFP + ──────►│                    │
   │                      │   Outbox row         │                    │
   │                      │   (one transaction)  │                    │
   │                      │                     │── RfpCreated ──────►│
   │                      │                     │   (fan-out to       │
   │                      │                     │    eligible         │
   │                      │                     │    carriers)        │
   │                      │                     │                    │
   │                      │◄── POST /bids ───────────────────────────│
   │                      │   (Idempotency-Key header checked → Redis)│
   │                      │── BidSubmitted ─────►│                    │
   │                      │                     │                    │
   │── POST /awards ──────►│                     │                    │
   │                      │── AwardIssued ───────►│                    │
```

### 5.3 Award workflow — Saga

When an Award is issued, the Workflow Worker runs an **orchestrated Saga** (MassTransit StateMachine)
with four steps and compensating actions on failure.

| Step | Action | Compensation on failure |
|---|---|---|
| 1 | **Reserve capacity** — call Carrier API via **gRPC** to lock the carrier's available volume for the lane. | Release the capacity reservation. |
| 2 | **Issue contract** — write Contract record to PostgreSQL. | Delete the draft contract. |
| 3 | **Notify shipper** — publish `ShipperNotified` event to RabbitMQ. | Log and skip (notification is best-effort). |
| 4 | **Mark RFP as Awarded** — update RFP aggregate status. | Revert RFP to `Closed` state. |

If the Saga does not complete within 30 seconds, it moves to `CompensationPending` and an alert is
emitted. All saga state is persisted in PostgreSQL, so the worker can restart without losing progress.

> **Why gRPC for Step 1?** The `ReserveCapacity` call is a synchronous internal command between two
> services we own. It has a strict SLA (the Saga will time out at 30s), requires a typed contract,
> and benefits from binary serialization under load. These are the canonical conditions that make
> gRPC preferable to REST for internal service-to-service calls. In Stage 1, the Workflow Worker
> holds the single gRPC client (Refit is replaced here); all external-facing APIs remain REST.

---

## 6. Architecture

### 6.1 Service communication map

| From | To | Protocol | When |
|---|---|---|---|
| Gateway | RFP API / Carrier API | HTTP/REST (YARP proxy) | All inbound client traffic. |
| RFP API | RabbitMQ | AMQP (MassTransit + Outbox) | `RfpCreated`, `RfpClosed`, `AwardIssued`. |
| Carrier API | RabbitMQ | AMQP (MassTransit) | `BidSubmitted` command to Workflow Worker. |
| Workflow Worker | Carrier API | **gRPC** | `ReserveCapacity` call in Saga step 1. |
| Workflow Worker | RabbitMQ | AMQP (MassTransit) | Consumes commands; publishes saga events. |

### 6.2 Data stores

| Store | Used by | Purpose |
|---|---|---|
| PostgreSQL `rfp_db` | RFP API, Workflow Worker | RFP, Lane, Bid, Award, Contract, Outbox, Saga state. |
| PostgreSQL `carrier_db` | Carrier API | Carrier registration, FMCSA data, capacity records, `jsonb` profile documents. |
| Redis | Carrier API, RFP API | Idempotency key store (`SET NX` + TTL), distributed cache, per-carrier rate limiter. |
| RabbitMQ | All services | Commands and domain events (topic exchange + DLQ). |

### 6.3 Architecture styles

Two different architecture styles are used deliberately — one per service character:

**Vertical Slice — RFP API.** The RFP API has high feature-change velocity: new bid-round rules,
new lane attributes, new award logic arrive frequently. Vertical Slice keeps each feature
self-contained so changes are local and don't ripple across layers.

```
src/FreightFlow.RfpApi/
  Features/
    CreateRfp/
      CreateRfpEndpoint.cs
      CreateRfpCommand.cs
      CreateRfpHandler.cs
      CreateRfpValidator.cs
    SubmitBid/
      SubmitBidEndpoint.cs
      SubmitBidCommand.cs
      SubmitBidHandler.cs
      SubmitBidValidator.cs
      ActiveBidsQuery.cs      ← Dapper hot-path read
    AwardCarrier/
      ...
```

**Clean Architecture — Workflow Worker.** The Saga domain is stable and must be unit-testable
without infrastructure. Clean Architecture enforces the dependency rule so the StateMachine logic
has zero infrastructure imports.

```
src/FreightFlow.WorkflowWorker/
  Domain/         ← Saga aggregate root, domain events, invariants. No infra deps.
  Application/    ← MassTransit StateMachine, use-case orchestration.
  Infrastructure/ ← EF Core persistence, gRPC client, MassTransit registration.
```

---

## 7. Technology Stack

### Runtime & framework

| Technology | Version | Role |
|---|---|---|
| .NET | 8 (LTS) | Target runtime for all services. |
| C# | 12 | Application language. |
| ASP.NET Core | 8 | HTTP layer — Minimal APIs with endpoint filters and OpenAPI. |
| YARP | 2.x | Reverse proxy / API gateway. |
| MassTransit | 8.x | RabbitMQ abstraction, Saga StateMachine, Outbox, DLQ retry. |
| gRPC / Grpc.AspNetCore | latest stable | Internal sync call: Workflow Worker → Carrier API. |
| Polly | 8.x | Resilience pipelines: retry + jitter, circuit breaker, timeout. |
| OpenTelemetry | 1.x | Distributed tracing across HTTP, RabbitMQ, and gRPC hops. |
| Serilog | 3.x | Structured logging with JSON console sink. |

### Data access

| Technology | Role |
|---|---|
| EF Core 8 | ORM for RFP, Bid, Award, Contract, Saga state. Optimistic concurrency via `xmin`. Compiled queries on hot paths. |
| Dapper | Raw SQL for the hot "active bids per lane" read query. Used deliberately alongside EF Core. |
| Npgsql | PostgreSQL ADO.NET driver (EF Core provider + Dapper). |
| StackExchange.Redis | Idempotency key store (`SET NX` + TTL), distributed cache, rate limiter. |
| FluentMigrator | Database migrations with explicit up/down. Expand-contract pattern for zero-downtime schema changes. |

---

## 8. Key Engineering Patterns

### 8.1 Transactional Outbox

When the RFP API writes an RFP or Award to PostgreSQL, it also writes an Outbox record in the
**same database transaction**. A hosted `OutboxDispatcher` polls the table, publishes messages to
RabbitMQ, and marks them sent. This guarantees a message is published if and only if the DB write
succeeded — without a two-phase commit.

```
┌─ Single PostgreSQL transaction ──────────────────┐
│  INSERT INTO rfps (...)                          │
│  INSERT INTO outbox (message_type, payload, ...) │
└──────────────────────────────────────────────────┘
         ▲
OutboxDispatcher (BackgroundService)
  polls outbox WHERE sent_at IS NULL
  → publishes to RabbitMQ
  → UPDATE outbox SET sent_at = NOW()
```

### 8.2 PostgreSQL jsonb for carrier profiles — and why not Cosmos DB

Carrier profiles have variable schemas: insurance document structure, certification fields, and
equipment types differ between flatbed, refrigerated, and hazmat carriers. In production this
variation is a natural fit for a document store (Azure Cosmos DB, MongoDB).

**In this project, a PostgreSQL `jsonb` column is used instead.** The rationale:

- The Cosmos DB Linux emulator is heavy (~2 GB RAM) and has documented reliability issues on ARM
  Macs and some Linux Docker setups. It adds significant friction to a learning project that should
  just run cleanly.
- The interview question behind this choice is about *when and why* you'd reach for a document
  store — not about Cosmos DB SDK mechanics. A `jsonb` column with a clear inline comment
  ("production: Cosmos DB / MongoDB — chosen here to keep Docker Compose self-contained") answers
  that question just as well.
- PostgreSQL `jsonb` supports JSON path queries, GIN indexing, and partial updates, making it a
  legitimate production-grade alternative for moderate document volumes anyway.

The `CarrierProfile` entity is modelled as a proper value object in the domain. The persistence
layer is the only place that knows about `jsonb`. This keeps the substitution cost to a single
infrastructure class if Cosmos DB is added in future.

### 8.3 Idempotency middleware

The Carrier API's bid-submission endpoint requires an `Idempotency-Key` header. A middleware layer
checks Redis (`SET NX + TTL`) before the handler runs. Duplicate requests within the TTL window
receive the cached response without re-processing — making the endpoint safe to retry from any
client.

### 8.4 gRPC internal contract

The `ReserveCapacity` call from Workflow Worker to Carrier API uses a `.proto`-defined contract:

```protobuf
service CapacityService {
  rpc ReserveCapacity (ReserveCapacityRequest) returns (ReserveCapacityReply);
}

message ReserveCapacityRequest {
  string carrier_id = 1;
  string lane_id    = 2;
  int32  volume     = 3;
}

message ReserveCapacityReply {
  bool   success      = 1;
  string reservation_id = 2;
}
```

This is the single gRPC surface in Stage 1. All other inter-service and client-facing calls remain
REST. The contrast is intentional — it demonstrates knowing *when* to apply gRPC (internal,
latency-sensitive, typed contract) vs. REST (external-facing, tooling breadth, cacheability).

### 8.5 Optimistic concurrency

The `Bid` aggregate uses PostgreSQL's `xmin` system column as an EF Core concurrency token. When
two bids arrive for the same RFP simultaneously, one succeeds and the other receives a
`DbUpdateConcurrencyException`, which the API maps to `HTTP 409 Conflict` with a `Retry-After`
hint.

### 8.6 Resilience

All outbound HTTP and gRPC calls from the Workflow Worker use a **Polly v8** resilience pipeline:

- Retry with exponential backoff and jitter — 3 attempts, max 8-second back-off.
- Circuit breaker — opens after 5 consecutive failures; half-open probe after 30 seconds.
- Timeout — 5 seconds per individual attempt.

RabbitMQ consumers use MassTransit's built-in retry + dead-letter queue. Poison messages move to a
dedicated DLQ after three delivery attempts.

---

## 9. Observability

| Signal | Implementation | Local sink |
|---|---|---|
| **Traces** | OpenTelemetry SDK; trace context propagated via HTTP headers, RabbitMQ message headers, and gRPC metadata. | Console exporter — a single trace ID visible across HTTP → RabbitMQ → gRPC hops. |
| **Logs** | Serilog structured logging, enriched with `TraceId`, `SpanId`, `ServiceName`. | Console JSON per container. |
| **Metrics** | ASP.NET Core built-in metrics + custom counters (bids/sec, saga completions, DLQ depth). | Console / optional Prometheus scrape endpoint. |
| **Health** | `/health` (liveness) and `/ready` (readiness) with tagged dependency checks for Postgres, RabbitMQ, and Redis. | Docker Compose `HEALTHCHECK` + curl probe. |

---

## 10. Out of Scope

Intentionally excluded to keep the project focused:

- **No frontend UI** — all interaction via REST API (Scalar / Swagger UI from OpenAPI).
- **No auth provider** — JWT tokens generated by a minimal token endpoint inside the Gateway for
  local testing. Production would use Azure AD B2C or Keycloak.
- **No ML model** — benchmarking (Stage 2) uses statistical aggregations. Architecture is designed
  for a model-serving sidecar to be added later.
- **No Kubernetes** — Docker Compose only. Services are stateless and health-probe-ready, making
  them Kubernetes-compatible by design.
- **No email / SMS delivery** — the Saga's notification step publishes an event; a real notification
  service would consume it.
- **No Cosmos DB** — replaced with PostgreSQL `jsonb` (see §8.2).
- **No Kafka in Stage 1** — see Stage 2 below.

---

## 11. Stage 2 — Kafka Extension

> Build this only after Stage 1 is complete and solid. If you run out of time before the interview,
> skip it entirely. Being honest about limited Kafka experience is better than a half-built service.

### What it adds

A fifth service, `freight-analytics-worker`, that consumes a `rate-stream` Kafka topic and builds
market-rate benchmarking indices (median, P25/P75 percentile bands, 30-day rolling average) from
every bid submitted.

The `freight-carrier-api` gains a Kafka producer: every `BidSubmitted` event is published to both
RabbitMQ (for the Saga command) and Kafka (for durable, replayable analytics ingest). This
demonstrates the canonical RabbitMQ vs. Kafka split: RabbitMQ for routable commands and
short-lived events; Kafka for the high-volume, replayable fact log.

### Additional infrastructure (Stage 2)

| Container | Image | Port(s) |
|---|---|---|
| `postgres-analytics` | `postgres:16` | 5434 |
| `kafka` | `confluentinc/cp-kafka:7.6` (KRaft mode — no Zookeeper) | 9092 |
| `freight-analytics-worker` | *(build from source)* | 5003 (health only) |

> **KRaft mode:** The Kafka image is configured with KRaft (Kafka's built-in consensus, stable
> since Kafka 3.3), eliminating the Zookeeper container. This is the current recommended
> single-node setup and reduces Docker Compose complexity.

### Analytics data model

```
PostgreSQL analytics_db
  rate_facts (partitioned by rfp_close_month)
    carrier_id, lane_id, bid_amount, submitted_at, rfp_id

  Materialized views (refreshed on schedule):
    mv_lane_median_rate
    mv_lane_percentile_bands
    mv_lane_rolling_30d_avg
```

### What this covers for the interview

- Kafka producer/consumer basics (Confluent.Kafka, no MassTransit abstraction — by design).
- RabbitMQ vs. Kafka trade-off in practice: command semantics vs. event log.
- Consumer group semantics, partition assignment, offset management.
- At-least-once delivery and idempotent analytics writes.
- PostgreSQL table partitioning for append-heavy time-series data.
- Materialized view refresh strategy without blocking writes.

### Honest interview framing

"I worked through Kafka in a learning project — producer/consumer, consumer groups, KRaft setup,
and partitioned analytics. I haven't operated it in production, so I can't speak to tuning a
broker cluster or handling schema evolution at scale. I know the conceptual trade-offs and have
hands-on basics."

That is a stronger answer than silence, and more credible than overclaiming.

---

## 12. Quick Start

### Stage 1

```bash
# 1. Start all containers
docker compose -f docker-compose.stage1.yml up --build

# 2. Check health
docker compose ps

# 3. API explorer
open http://localhost:8080/scalar

# 4. RabbitMQ management UI  (guest / guest)
open http://localhost:15672

# 5. Walk through the request sequence
code docs/walkthrough.http
```

### Stage 2 (when ready)

```bash
docker compose -f docker-compose.stage1.yml -f docker-compose.stage2.yml up --build
```

Stage 2 extends Stage 1 via Docker Compose override — no changes to Stage 1 services.

---

*FreightFlow — internal engineering reference · v1.1 · April 2026*
