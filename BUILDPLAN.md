# FreightFlow — Build Plan

> Single source of truth for what to build, in what order, and why each decision was made.
> Check off tasks as you go. Do not skip milestones out of order — each one is a dependency
> for the next.

---

## How to read this file

- **Milestone** = a shippable, runnable checkpoint. After each milestone the system does something end-to-end.
- **Task** = one focused session, roughly 1–3 hours. Small enough to finish, large enough to matter.
- **Note** = a non-obvious decision baked into the task. Read before you start coding, not after.

Build order follows the dependency graph, not alphabetical service order:

```
Milestone 0 — Repo + Infra skeleton
  → Milestone 1 — Domain models
    → Milestone 2 — freight-carrier-api
      → Milestone 3 — freight-rfp-api
        → Milestone 4 — freight-workflow-worker
          → Milestone 5 — freight-gateway + integration smoke test
            → Milestone 6 — Stage 2 (Kafka / analytics) [optional]
```

---

## Milestone 0 — Repo & Infrastructure Skeleton

**Goal:** `docker compose up` starts all containers and every health check goes green.
No application code yet — just wiring.

- [ ] **0.1 — Repo structure**
  Create the solution and project layout:
  ```
  FreightFlow.sln
  src/
    FreightFlow.RfpApi/
    FreightFlow.CarrierApi/
    FreightFlow.WorkflowWorker/
    FreightFlow.Gateway/
    FreightFlow.SharedKernel/        ← value objects, domain events, common contracts
  proto/
    capacity.proto                   ← single gRPC contract, owned here, referenced by both sides
  docs/
    walkthrough.http                 ← VS Code REST Client request sequence
  docker-compose.stage1.yml
  docker-compose.stage2.yml         ← overlay, empty for now
  ```
  > **Note:** `SharedKernel` is a project reference, not a NuGet package. Keep it thin —
  > only things that genuinely cross service boundaries go here (message contracts, value
  > objects like `Money`, `ZipCode`, `FreightClass`). Domain logic stays inside each service.

- [ ] **0.2 — docker-compose.stage1.yml**
  Define all infrastructure containers with health checks:
  - `postgres-rfp` (port 5432) — healthcheck: `pg_isready`
  - `postgres-carrier` (port 5433) — healthcheck: `pg_isready`
  - `redis` (port 6379) — healthcheck: `redis-cli ping`
  - `rabbitmq` (port 5672 + 15672) — healthcheck: `rabbitmq-diagnostics -q ping`
  - Application service stubs (`freight-rfp-api`, `freight-carrier-api`,
    `freight-workflow-worker`, `freight-gateway`) with `depends_on: { condition: service_healthy }`.
  > **Note:** Use named volumes for Postgres data so `docker compose down` doesn't wipe the DB.
  > Use `docker compose down -v` explicitly when you want a clean slate.

- [ ] **0.3 — Base project setup for each service**
  For each of the four application projects:
  - Add `Microsoft.NET.Sdk.Web` (or `Microsoft.NET.Sdk.Worker` for the two workers).
  - Add Serilog console sink, OpenTelemetry SDK, health checks middleware.
  - Add a minimal `/health` and `/ready` endpoint that returns 200.
  - Add a `Dockerfile` (multi-stage: `sdk` build → `aspnet` runtime, non-root user).
  > **Note:** The two workers (`WorkflowWorker`, future `AnalyticsWorker`) use `Worker SDK`,
  > not `Web SDK`. They expose health only — no HTTP API. Use `Microsoft.Extensions.Hosting`
  > with `IHostedService`.

- [ ] **0.4 — FluentMigrator setup (rfp_db and carrier_db)**
  - Add FluentMigrator to each API project (or a companion `*.Migrations` project).
  - Write a single `InitialSchema` migration per DB (empty tables, just to prove the runner works).
  - Run migrations on startup via `IMigrationRunner` in `Program.cs` — before the app starts
    serving requests.
  > **Note:** Use explicit `Up()` and `Down()` on every migration. You will need `Down()` during
  > development when you need to roll back a schema change without dropping the whole DB.

- [ ] **0.5 — Verify milestone**
  ```bash
  docker compose -f docker-compose.stage1.yml up --build
  docker compose ps          # all containers should show "healthy"
  curl localhost:5000/health  # rfp-api → 200
  curl localhost:5001/health  # carrier-api → 200
  curl localhost:5002/health  # workflow-worker → 200
  ```

---

## Milestone 1 — Domain Models

**Goal:** Pure C# domain — no infrastructure, no HTTP, fully unit-testable.
This is the most important milestone to get right. Everything else is plumbing around it.

- [ ] **1.1 — Value objects in SharedKernel**
  Implement as `readonly record struct` or `record class` with validation in the constructor:
  - `Money(decimal Amount, string Currency)` — guard: amount ≥ 0, currency is 3-char ISO.
  - `ZipCode(string Value)` — guard: 5-digit US ZIP (regex).
  - `FreightClass` — enum: `Class50`, `Class55`, ..., `Class500` (NMFC classes).
  - `DotNumber(string Value)` — guard: non-empty, numeric string.
  - `LaneId`, `RfpId`, `CarrierId`, `BidId`, `ContractId` — strongly-typed IDs as
    `readonly record struct` wrapping `Guid`. Prevents passing a `CarrierId` where a `LaneId`
    is expected.
  > **Note:** Strongly-typed IDs are worth the boilerplate. They catch a whole class of bugs at
  > compile time and make method signatures self-documenting. You'll be asked about this.

- [ ] **1.2 — RFP aggregate (in FreightFlow.RfpApi)**
  ```
  RFP (aggregate root)
    Id: RfpId
    ShipperId: Guid
    Status: RfpStatus  { Draft, Open, Closed, Awarded }
    OpenAt: DateTimeOffset
    CloseAt: DateTimeOffset
    MaxBidRounds: int
    Lanes: IReadOnlyList<Lane>
    Bids: IReadOnlyList<Bid>
    Award: Award?
  ```
  Invariants to enforce (throw domain exceptions, not ArgumentException):
  - Cannot add a Lane to an RFP that is not in `Draft`.
  - Cannot submit a Bid to an RFP that is not `Open`.
  - Cannot submit more than `MaxBidRounds` rounds of bids.
  - Cannot award an RFP that has no bids.
  - Cannot award an RFP that is already `Awarded`.

  Domain events to raise (collected on the aggregate, dispatched by the handler):
  - `RfpCreated`, `RfpOpened`, `RfpClosed`, `BidSubmitted`, `AwardIssued`.
  > **Note:** Do not use MediatR `IDomainEvent` here. Keep the domain project infrastructure-free.
  > Use a simple `List<IDomainEvent>` on the aggregate root base class; the EF Core `SaveChanges`
  > interceptor (or the handler) drains the list after the transaction commits.

- [ ] **1.3 — Carrier aggregate (in FreightFlow.CarrierApi)**
  ```
  Carrier (aggregate root)
    Id: CarrierId
    DotNumber: DotNumber
    Name: string
    AuthorityStatus: AuthorityStatus  { Active, Inactive, Revoked }
    InsuranceExpiry: DateOnly
    Profile: CarrierProfile           ← jsonb in Postgres, document store in prod
    CapacityRecords: IReadOnlyList<CapacityRecord>
  ```
  Invariants:
  - Cannot reserve capacity if `AuthorityStatus != Active`.
  - Cannot reserve capacity below zero.
  - Releasing a reservation that doesn't exist throws a domain exception.

- [ ] **1.4 — Contract aggregate (in FreightFlow.WorkflowWorker)**
  Simple read-mostly aggregate — created by the Saga, never mutated after creation:
  ```
  Contract (aggregate root)
    Id: ContractId
    RfpId: RfpId
    CarrierId: CarrierId
    LaneId: LaneId
    AgreedRate: Money
    IssuedAt: DateTimeOffset
    Status: ContractStatus  { Draft, Active, Void }
  ```

- [ ] **1.5 — Unit tests for all invariants**
  One test project (`FreightFlow.Domain.Tests`) using xUnit. Test every invariant with a positive
  and a negative case. These tests should run in < 1 second with zero infrastructure.
  > **Note:** If you can't test a domain rule without spinning up a DB or a message broker,
  > the rule is in the wrong layer.

---

## Milestone 2 — freight-carrier-api

**Goal:** A working carrier-facing API: onboard a carrier, get their profile back.
RabbitMQ publish via Outbox. gRPC server for `ReserveCapacity`.

- [ ] **2.1 — Database schema (carrier_db)**
  FluentMigrator migration:
  ```sql
  carriers (id uuid PK, dot_number text UNIQUE, name text, authority_status int,
            insurance_expiry date, profile jsonb, created_at timestamptz)
  capacity_records (id uuid PK, carrier_id uuid FK, lane_id uuid, available_volume int,
                    reserved_volume int, version xmin)   ← xmin is a system column, not a real column
  outbox (id uuid PK, message_type text, payload jsonb, created_at timestamptz, sent_at timestamptz)
  ```
  > **Note:** `xmin` is PostgreSQL's built-in row version — you don't create it, you just tell
  > EF Core to use it as a concurrency token: `Property(x => x.Version).IsRowVersion().HasColumnName("xmin")`.
  > This is cleaner than adding a `row_version` column and avoids the extra write.

- [ ] **2.2 — EF Core DbContext + FluentAPI configuration**
  - Map `Carrier` aggregate. Use `OwnsOne` for `CarrierProfile` backed by a `jsonb` column
    (Npgsql has built-in jsonb support — use `HasColumnType("jsonb")`).
  - Map `CapacityRecord` as an owned entity collection (`OwnsMany`).
  - Map `Outbox` table.
  - Configure `xmin` as a concurrency token on `CapacityRecord`.
  > **Note:** Do not expose `DbSet<CapacityRecord>` directly. Access it only through the
  > `Carrier` aggregate. This enforces the aggregate boundary at the persistence level.

- [ ] **2.3 — Carrier onboarding endpoint**
  `POST /carriers`
  - Validate request with FluentValidation (DOT number format, insurance expiry in the future).
  - Create `Carrier` aggregate via factory method.
  - Write `Carrier` + `Outbox` row in a single `SaveChangesAsync()`.
  - Return `201 Created` with `Location` header.
  > **Note:** The Outbox row must go into the same `SaveChanges` transaction as the Carrier insert.
  > Do not call `SaveChanges` twice. If the second call fails, you have a Carrier in the DB
  > with no outbox message — which is the exact problem Outbox solves.

- [ ] **2.4 — GET /carriers/{id} endpoint**
  Simple read — use `AsNoTracking()` and project to a response DTO directly in the query.
  Do not load the full aggregate just to return a flat response.

- [ ] **2.5 — Outbox dispatcher (BackgroundService)**
  Polls `outbox WHERE sent_at IS NULL`, publishes each message to RabbitMQ via MassTransit,
  sets `sent_at`. Use `IServiceScopeFactory` to get a scoped `DbContext` from the singleton
  `BackgroundService`.
  > **Note:** Poll interval should be configurable (default: 5 seconds). In production you'd
  > replace polling with Postgres `LISTEN/NOTIFY` — but polling is correct, simple, and
  > explainable in an interview.

- [ ] **2.6 — gRPC server: ReserveCapacity**
  - Add `Grpc.AspNetCore` package.
  - Implement `CapacityService.ReserveCapacityAsync`:
    - Load the `Carrier` aggregate.
    - Call `carrier.ReserveCapacity(laneId, volume)` — domain logic.
    - `SaveChangesAsync()` — EF Core concurrency token (`xmin`) guards concurrent reservations.
    - If `DbUpdateConcurrencyException`, return `success: false` with a reason.
  > **Note:** gRPC in ASP.NET Core requires HTTP/2. In Docker Compose, configure Kestrel
  > with `Protocols = Http2` on the gRPC port (e.g. 5011) and `Http1AndHttp2` on the REST
  > port (5001). Do not use HTTP/2 for everything — it breaks health check curls.

- [ ] **2.7 — Idempotency middleware**
  For bid-submission (will be called by rfp-api via message, but also add to the HTTP surface
  for direct testing):
  - Read `Idempotency-Key` header.
  - Check Redis: `SET idempotency:{key} "" NX EX 86400`.
  - If key exists, return the cached response (store response body + status in Redis).
  - If not, execute the handler, cache the response, return it.
  > **Note:** Store the full response (status code + body as JSON) in Redis, not just a flag.
  > This way retries get the original response, not a generic 200.

- [ ] **2.8 — Health checks**
  Tag-based checks: Postgres (`carrier_db`), Redis, RabbitMQ. Expose on `/health` (liveness)
  and `/ready` (readiness — only healthy if all deps are up).

- [ ] **2.9 — Verify milestone**
  ```bash
  # Onboard a carrier
  POST http://localhost:5001/carriers
  { "dotNumber": "1234567", "name": "Acme Trucking", ... }
  # → 201, Location header

  # Get carrier back
  GET http://localhost:5001/carriers/{id}
  # → 200, profile included

  # Check RabbitMQ management UI — CarrierOnboarded should appear in the exchange
  open http://localhost:15672
  ```

---

## Milestone 3 — freight-rfp-api

**Goal:** Shippers can create RFPs, carriers can submit bids, shippers can issue awards.
Outbox publishes domain events. Dapper hot-path read. Optimistic concurrency on bids.

- [ ] **3.1 — Database schema (rfp_db)**
  FluentMigrator migration:
  ```sql
  rfps (id uuid PK, shipper_id uuid, status int, open_at timestamptz,
        close_at timestamptz, max_bid_rounds int, created_at timestamptz)
  lanes (id uuid PK, rfp_id uuid FK, origin_zip text, dest_zip text,
         freight_class int, volume int)
  bids (id uuid PK, rfp_id uuid FK, carrier_id uuid, round int,
        submitted_at timestamptz, xmin xid)           ← xmin for optimistic concurrency
  bid_lane_prices (bid_id uuid FK, lane_id uuid FK, amount numeric, currency char(3))
  awards (id uuid PK, rfp_id uuid FK, bid_id uuid FK, awarded_at timestamptz)
  outbox (id uuid PK, message_type text, payload jsonb, created_at timestamptz, sent_at timestamptz)
  ```
  > **Note:** `bid_lane_prices` is a separate table, not a jsonb column, because you need to
  > query "what's the lowest bid for lane X across all carriers" efficiently. Put a covering
  > index on `(lane_id, amount)`.

- [ ] **3.2 — Vertical Slice structure**
  ```
  Features/
    CreateRfp/
      CreateRfpEndpoint.cs
      CreateRfpCommand.cs
      CreateRfpHandler.cs
      CreateRfpValidator.cs
    AddLane/
      ...
    OpenRfp/
      ...
    SubmitBid/
      SubmitBidEndpoint.cs
      SubmitBidCommand.cs
      SubmitBidHandler.cs
      SubmitBidValidator.cs
      ActiveBidsQuery.cs      ← Dapper, see 3.5
    AwardCarrier/
      ...
    GetRfp/
      GetRfpEndpoint.cs
      GetRfpQuery.cs          ← EF Core AsNoTracking projection
  ```
  > **Note:** MediatR dispatches from endpoint to handler. Each handler is responsible for
  > loading the aggregate, calling domain logic, and persisting. No service classes that span
  > features — changes to `SubmitBid` should never require touching `CreateRfp`.

- [ ] **3.3 — CreateRfp, AddLane, OpenRfp slices**
  Straightforward CRUD against the `RFP` aggregate. Key decisions:
  - `CreateRfp` creates in `Draft` status. Returns `201`.
  - `AddLane` is only allowed in `Draft`. Returns `409` with Problem Details if the RFP is `Open`.
  - `OpenRfp` transitions to `Open`, writes `RfpOpened` to Outbox.
  > **Note:** Map domain exceptions to HTTP Problem Details (RFC 7807) in a global exception
  > handler middleware, not in each handler. Add a `DomainException` base class and a
  > `switch` on exception type → HTTP status in the middleware.

- [ ] **3.4 — SubmitBid slice — EF Core path**
  - Load `RFP` aggregate with its `Bids` (tracking, because you'll mutate).
  - Call `rfp.SubmitBid(carrierId, laneprices)` — domain logic validates round count, open status.
  - `SaveChangesAsync()` — EF Core's `xmin` concurrency token handles two simultaneous bids
    for the same RFP: one succeeds, one gets `DbUpdateConcurrencyException` → `409 Conflict`
    with `Retry-After: 1`.
  - Write `BidSubmitted` to Outbox in the same transaction.
  - Check `Idempotency-Key` via Redis middleware (same middleware as carrier-api).

- [ ] **3.5 — ActiveBidsQuery — Dapper hot path**
  The "show all bids for this RFP with the current best price per lane" query is read-heavy and
  accessed on every page load of the bidding UI. This is the canonical Dapper use case.
  ```csharp
  // ActiveBidsQuery.cs — inside the SubmitBid feature folder
  public sealed class ActiveBidsQuery(NpgsqlDataSource db)
  {
      public async Task<IReadOnlyList<BidSummary>> ExecuteAsync(RfpId rfpId)
      {
          const string sql = """
              SELECT b.id, b.carrier_id, b.round, blp.lane_id,
                     blp.amount, blp.currency
              FROM   bids b
              JOIN   bid_lane_prices blp ON blp.bid_id = b.id
              WHERE  b.rfp_id = @rfpId
              ORDER  BY blp.lane_id, blp.amount
          """;
          await using var conn = await db.OpenConnectionAsync();
          return (await conn.QueryAsync<BidSummary>(sql, new { rfpId = rfpId.Value }))
              .ToList();
      }
  }
  ```
  > **Note:** Register `NpgsqlDataSource` as a singleton in DI (Npgsql's recommended pattern).
  > Use it for Dapper. Do not create `NpgsqlConnection` directly — `NpgsqlDataSource` handles
  > pooling correctly.

- [ ] **3.6 — AwardCarrier slice**
  - Load `RFP` aggregate.
  - Call `rfp.IssueAward(bidId)` — validates the bid exists and RFP is `Closed`.
  - Write `Award` entity + `AwardIssued` Outbox row in one transaction.
  - Return `202 Accepted` — the actual award workflow (Saga) is async.

- [ ] **3.7 — Outbox dispatcher**
  Same pattern as carrier-api. `BackgroundService`, polls, publishes via MassTransit.
  > **Note:** You can extract the dispatcher to `SharedKernel` as a generic
  > `OutboxDispatcher<TDbContext>` — it's identical in both services. One less class to maintain.

- [ ] **3.8 — GetRfp read endpoint**
  `GET /rfps/{id}` — use EF Core with `AsNoTracking()` and a projection (`.Select(r => new RfpDto {...})`).
  Do not return the full EF entity; projections skip loading data you don't need.

- [ ] **3.9 — Compiled query for GetRfp**
  ```csharp
  private static readonly Func<RfpDbContext, Guid, Task<RfpDto?>> GetRfpQuery =
      EF.CompileAsyncQuery((RfpDbContext db, Guid id) =>
          db.Rfps
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new RfpDto { ... })
            .FirstOrDefault());
  ```
  > **Note:** This is worth adding for the one or two endpoints that get the most traffic.
  > The compilation overhead is paid once at startup. Be ready to explain what it buys you
  > (skips the LINQ expression tree parsing on every call).

- [ ] **3.10 — Verify milestone**
  ```
  POST /rfps                          → 201
  POST /rfps/{id}/lanes               → 201
  POST /rfps/{id}/open                → 200
  POST /rfps/{id}/bids (with Idempotency-Key header)  → 201
  POST /rfps/{id}/bids (same Idempotency-Key again)   → same 201 (cached)
  POST /rfps/{id}/bids (concurrent, same RFP)         → one 201, one 409
  POST /rfps/{id}/awards              → 202
  GET  /rfps/{id}                     → 200
  # Check outbox: AwardIssued should appear in RabbitMQ exchange
  ```

---

## Milestone 4 — freight-workflow-worker

**Goal:** Saga orchestrates the 4-step award workflow. Compensating transactions on failure.
Clean Architecture. Consumes `AwardIssued` from RabbitMQ; calls carrier-api via gRPC.

- [ ] **4.1 — Clean Architecture folder structure**
  ```
  src/FreightFlow.WorkflowWorker/
    Domain/
      SagaAggregate/
        AwardWorkflowState.cs      ← EF Core entity, MassTransit saga state
        AwardWorkflowStateMachine.cs
      Events/
        AwardIssued.cs             ← shared contract from SharedKernel
      Exceptions/
        SagaTimeoutException.cs
    Application/
      AwardWorkflowStateMachine.cs ← orchestration logic, no infra imports
    Infrastructure/
      Persistence/
        WorkflowDbContext.cs
        Migrations/
      GrpcClients/
        CarrierCapacityClient.cs   ← wraps the generated gRPC client + Polly pipeline
      Messaging/
        MassTransitConfiguration.cs
    Program.cs
  ```
  > **Note:** `Application/` imports only `Domain/`. `Infrastructure/` imports `Application/`
  > and `Domain/`. Nothing in `Domain/` or `Application/` references any NuGet package that
  > touches a database, broker, or HTTP client. Enforce this with a unit test that checks
  > assembly references.

- [ ] **4.2 — Database schema (rfp_db — saga state)**
  Add a migration to `rfp_db` (same DB as rfp-api, different schema):
  ```sql
  CREATE SCHEMA saga;
  saga.award_workflow_state (
    correlation_id uuid PK,
    current_state  text,
    rfp_id         uuid,
    carrier_id     uuid,
    bid_id         uuid,
    reservation_id uuid,
    contract_id    uuid,
    created_at     timestamptz,
    updated_at     timestamptz,
    row_version    int  -- MassTransit uses this for optimistic concurrency
  )
  ```
  > **Note:** MassTransit's EF Core Saga repository manages the saga state table. You provide
  > the `DbContext` and the mapping; MassTransit handles the read-modify-write under its own
  > locking. Do not write to this table directly.

- [ ] **4.3 — AwardWorkflowStateMachine**
  States: `Initial` → `CapacityReserving` → `ContractIssuing` → `ShipperNotifying`
          → `RfpAwarding` → `Completed`
  Parallel compensation path: any state → `CompensationPending` → `Compensated` / `Failed`

  Steps:
  1. `ReserveCapacity` — gRPC call to carrier-api. On failure: compensate (no-op, nothing written yet).
  2. `IssueContract` — write `Contract` to Postgres. On failure: delete draft contract.
  3. `NotifyShipper` — publish `ShipperNotified` to RabbitMQ. On failure: log + skip (best-effort).
  4. `MarkRfpAwarded` — publish `RfpMarkAsAwarded` command to RabbitMQ → rfp-api handles it.
     On failure: revert RFP to `Closed`.

  Timeout: if the Saga does not reach `Completed` within 30 seconds, transition to
  `CompensationPending` and emit a `SagaTimedOut` alert event.

  > **Note:** Compensating transactions are the hardest part to get right. Write them first,
  > test them in isolation (inject a fault after step 2, verify step 1 is rolled back), then
  > write the happy path. Not the other way around.

- [ ] **4.4 — gRPC client: CarrierCapacityClient**
  Wrap the generated `CapacityService.CapacityServiceClient` with Polly v8:
  ```csharp
  ResiliencePipeline pipeline = new ResiliencePipelineBuilder()
      .AddRetry(new RetryStrategyOptions {
          MaxRetryAttempts = 3,
          Delay = TimeSpan.FromSeconds(1),
          BackoffType = DelayBackoffType.Exponential,
          UseJitter = true })
      .AddCircuitBreaker(new CircuitBreakerStrategyOptions {
          FailureRatio = 0.5,
          SamplingDuration = TimeSpan.FromSeconds(30),
          MinimumThroughput = 5,
          BreakDuration = TimeSpan.FromSeconds(30) })
      .AddTimeout(TimeSpan.FromSeconds(5))
      .Build();
  ```
  > **Note:** Order matters in Polly v8 pipelines: outer to inner is Timeout → CircuitBreaker
  > → Retry. If you put Retry outermost, a timed-out call gets retried. You almost never want
  > that for a Saga step with a 30-second global timeout.

- [ ] **4.5 — MassTransit consumer registration**
  Register the saga with the EF Core repository:
  ```csharp
  services.AddMassTransit(x => {
      x.AddSagaStateMachine<AwardWorkflowStateMachine, AwardWorkflowState>()
       .EntityFrameworkRepository(r => {
           r.ConcurrencyMode = ConcurrencyMode.Optimistic;
           r.AddDbContext<WorkflowDbContext>(...);
       });
      x.UsingRabbitMq((ctx, cfg) => {
          cfg.ReceiveEndpoint("award-workflow", e => {
              e.ConfigureSaga<AwardWorkflowState>(ctx);
          });
      });
  });
  ```

- [ ] **4.6 — Unit tests for the StateMachine**
  MassTransit provides `MassTransitStateMachineHarness` for in-memory saga testing — no RabbitMQ
  required:
  ```csharp
  await using var harness = new InMemoryTestHarness();
  var sagaHarness = harness.StateMachineSaga<AwardWorkflowStateMachine, AwardWorkflowState>();
  await harness.Start();
  await harness.Bus.Publish(new AwardIssued { ... });
  // assert state transitions
  ```
  Test cases: happy path end-to-end, timeout compensation, gRPC failure → compensation.

- [ ] **4.7 — Verify milestone**
  ```bash
  # Trigger the full flow via REST Client:
  # 1. POST /carriers     → onboard a carrier
  # 2. POST /rfps + lanes → create an RFP
  # 3. POST /rfps/{id}/open
  # 4. POST /rfps/{id}/bids
  # 5. POST /rfps/{id}/awards  → triggers Saga
  # Watch workflow-worker logs — should show state transitions
  # Check RabbitMQ management UI — award-workflow queue consumed
  # Query rfp_db: saga.award_workflow_state → status = Completed
  # Query rfp_db: contracts table → one row created
  ```

---

## Milestone 5 — freight-gateway + Integration Smoke Test

**Goal:** Single entry point. JWT validation. Rate limiting. End-to-end walkthrough works
through port 8080 only.

- [ ] **5.1 — YARP routing**
  Add `Yarp.ReverseProxy` package. Configure routes in `appsettings.json`:
  ```json
  {
    "ReverseProxy": {
      "Routes": {
        "rfp-route":     { "ClusterId": "rfp-cluster",     "Match": { "Path": "/rfps/{**catch-all}" } },
        "carrier-route": { "ClusterId": "carrier-cluster", "Match": { "Path": "/carriers/{**catch-all}" } }
      },
      "Clusters": {
        "rfp-cluster":     { "Destinations": { "rfp":     { "Address": "http://freight-rfp-api:5000" } } },
        "carrier-cluster": { "Destinations": { "carrier": { "Address": "http://freight-carrier-api:5001" } } }
      }
    }
  }
  ```

- [ ] **5.2 — JWT token endpoint (local dev only)**
  A minimal endpoint `POST /token` that accepts `{ "sub": "shipper-1", "role": "shipper" }` and
  returns a signed JWT. The signing key is in `appsettings.json` (not a secret — this is local dev).
  > **Note:** In the interview, be explicit: "In production this is replaced by Azure AD B2C or
  > Keycloak — this is a local test stub." The point is that JWT validation is wired correctly
  > in the gateway, not that you built an auth server.

- [ ] **5.3 — JWT validation middleware**
  Validate the token in the gateway before proxying. Downstream services trust the gateway and
  do not re-validate (internal network, Docker Compose).
  > **Note:** This is the `mTLS between internal services` vs `gateway-validates-JWT` tradeoff.
  > You've chosen the simpler model. Be ready to explain when you'd add internal mTLS (zero-trust
  > network, regulatory requirement, services exposed outside the cluster).

- [ ] **5.4 — Rate limiting**
  ASP.NET Core's built-in `RateLimiter` (no extra package needed in .NET 8):
  - 100 requests/minute per IP for anonymous endpoints.
  - 1000 requests/minute per authenticated `sub` claim.
  > **Note:** Use `AddTokenBucketLimiter` or `AddFixedWindowLimiter` — both are fine. The point
  > is knowing that rate limiting belongs at the gateway, not in each downstream service.

- [ ] **5.5 — docs/walkthrough.http**
  A complete VS Code REST Client sequence that exercises the full happy path through the gateway:
  ```
  ### 1. Get a JWT
  POST http://localhost:8080/token
  ### 2. Onboard a carrier
  POST http://localhost:8080/carriers
  Authorization: Bearer {{token}}
  ### 3. Create + open an RFP
  ### 4. Submit a bid
  ### 5. Award the carrier
  ### 6. Poll saga state (optional — direct DB query)
  ```

- [ ] **5.6 — Verify milestone (full end-to-end)**
  Run the full `walkthrough.http` from scratch against a clean `docker compose up --build`.
  Every step should succeed. This is your "demo" for the interview.

---

## Milestone 6 — Stage 2: Kafka / Analytics Worker [Optional]

**Goal:** Add Kafka as a durable, replayable event log. Benchmarking indices from bid facts.
Build this only if Milestone 5 is complete and solid with time to spare.

- [ ] **6.1 — docker-compose.stage2.yml overlay**
  Add to the overlay (not the base file):
  - `kafka` container (`confluentinc/cp-kafka:7.6`, KRaft mode — no Zookeeper)
  - `postgres-analytics` container (port 5434)
  - `freight-analytics-worker` service

  KRaft config for the Kafka container:
  ```yaml
  environment:
    KAFKA_NODE_ID: 1
    KAFKA_PROCESS_ROLES: broker,controller
    KAFKA_CONTROLLER_QUORUM_VOTERS: 1@kafka:9093
    KAFKA_LISTENERS: PLAINTEXT://0.0.0.0:9092,CONTROLLER://0.0.0.0:9093
    KAFKA_ADVERTISED_LISTENERS: PLAINTEXT://kafka:9092
    KAFKA_CONTROLLER_LISTENER_NAMES: CONTROLLER
    KAFKA_LOG_DIRS: /var/lib/kafka/data
    CLUSTER_ID: MkU3OEVBNTcwNTJENDM2Qk  # generate once: kafka-storage random-uuid
  ```

- [ ] **6.2 — Kafka producer in freight-carrier-api**
  After the `BidSubmitted` Outbox write, also produce a `RateEvent` to the `rate-stream` topic
  using `Confluent.Kafka` directly (no MassTransit abstraction — by design, to show you know
  the raw API):
  ```csharp
  var message = new Message<string, string> {
      Key = laneId.ToString(),         // partition by lane for ordering within a lane
      Value = JsonSerializer.Serialize(rateEvent)
  };
  await producer.ProduceAsync("rate-stream", message);
  ```
  > **Note:** Kafka producer is a singleton — `ProducerBuilder<K,V>.Build()` is expensive. Register
  > it as `IProducer<string, string>` singleton in DI.

- [ ] **6.3 — analytics_db schema**
  ```sql
  CREATE TABLE rate_facts (
      id            uuid          NOT NULL,
      carrier_id    uuid          NOT NULL,
      lane_id       uuid          NOT NULL,
      bid_amount    numeric(12,2) NOT NULL,
      currency      char(3)       NOT NULL,
      submitted_at  timestamptz   NOT NULL,
      rfp_id        uuid          NOT NULL
  ) PARTITION BY RANGE (submitted_at);

  -- Create monthly partitions for the current period
  CREATE TABLE rate_facts_2026_04 PARTITION OF rate_facts
      FOR VALUES FROM ('2026-04-01') TO ('2026-05-01');
  ```
  > **Note:** Add a `UNIQUE (carrier_id, lane_id, rfp_id)` constraint on each partition for
  > idempotent inserts — `INSERT ... ON CONFLICT DO NOTHING`. This makes the consumer safe to
  > replay from the beginning of the Kafka log without creating duplicate facts.

- [ ] **6.4 — freight-analytics-worker: Kafka consumer**
  Use `Confluent.Kafka` `IConsumer<string, string>` directly. Run in a `BackgroundService`.
  Key consumer config:
  ```csharp
  new ConsumerConfig {
      GroupId = "analytics-worker",
      AutoOffsetReset = AutoOffsetReset.Earliest,   // replay from start if no committed offset
      EnableAutoCommit = false,                      // manual commit after successful DB write
  }
  ```
  Consume → deserialize → upsert to `rate_facts` (`ON CONFLICT DO NOTHING`) → commit offset.
  > **Note:** Manual offset commit after the DB write is what gives you at-least-once delivery
  > with idempotent processing. If the worker crashes after the DB write but before the commit,
  > the message is reprocessed — the `ON CONFLICT DO NOTHING` makes it a no-op.

- [ ] **6.5 — Materialized views**
  ```sql
  CREATE MATERIALIZED VIEW mv_lane_median_rate AS
  SELECT lane_id,
         PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY bid_amount) AS median_rate,
         PERCENTILE_CONT(0.25) WITHIN GROUP (ORDER BY bid_amount) AS p25_rate,
         PERCENTILE_CONT(0.75) WITHIN GROUP (ORDER BY bid_amount) AS p75_rate,
         COUNT(*) AS sample_count
  FROM   rate_facts
  WHERE  submitted_at >= NOW() - INTERVAL '30 days'
  GROUP  BY lane_id;

  -- Refresh every hour via a BackgroundService timer (not pg_cron in dev)
  ```
  > **Note:** `REFRESH MATERIALIZED VIEW CONCURRENTLY` requires a unique index on the view.
  > Add `CREATE UNIQUE INDEX ON mv_lane_median_rate (lane_id)`. Without it, `CONCURRENTLY`
  > fails and a plain `REFRESH` takes an exclusive lock — which blocks reads during the refresh.

- [ ] **6.6 — Benchmarking read endpoint (add to rfp-api)**
  `GET /rfps/{id}/benchmark?laneId={laneId}` — query the materialized view directly via Dapper.
  Returns median, P25, P75, sample count. The rfp-api reads from `analytics_db` (separate
  connection string) — this is fine for a read; you're not mixing write transactions.

- [ ] **6.7 — Verify Stage 2**
  ```bash
  docker compose -f docker-compose.stage1.yml -f docker-compose.stage2.yml up --build
  # Submit several bids across different carriers for the same lane
  # Wait 5s for consumer to process
  # REFRESH MATERIALIZED VIEW mv_lane_median_rate;  (or wait for the hourly timer)
  GET http://localhost:8080/rfps/{id}/benchmark?laneId={laneId}
  # → median, P25, P75, sample count
  ```

---

## Cross-Cutting Checklist (apply to every service)

These are not separate tasks — they're standards. Check them before marking any milestone complete.

- [ ] **Structured logging** — every log entry has `TraceId`, `SpanId`, `ServiceName`.
      No `Console.WriteLine`. No `_logger.LogInformation("User {0} did thing", userId)` — use
      named placeholders: `_logger.LogInformation("User {UserId} submitted bid {BidId}", ...)`.
- [ ] **OpenTelemetry traces** — trace context propagated via HTTP headers (`traceparent`),
      RabbitMQ message headers, and gRPC metadata. A single request should produce one trace
      visible across all service logs.
- [ ] **Problem Details** — all 4xx and 5xx responses use RFC 7807 `application/problem+json`.
      No plain-string error responses.
- [ ] **Health checks** — `/health` (liveness: is the process alive?) and `/ready` (readiness:
      are all dependencies reachable?). Each dependency tagged separately.
- [ ] **No secrets in config files** — use environment variables in Docker Compose. Connection
      strings go in `environment:` in `docker-compose.stage1.yml`, not in `appsettings.json`.
- [ ] **Unit tests for domain rules** — every aggregate invariant has a positive and negative test.
      No infrastructure in domain tests.

---

## Interview Story Map

When the interviewer says "walk me through your project," use this sequence:

1. **Problem** (30 sec) — freight procurement: shippers + carriers + RFPs + bids. $800B industry,
   spreadsheets and phone calls today.
2. **Architecture** (2 min) — 4 services, each owns its data, communicate via RabbitMQ commands
   and events. One synchronous gRPC call inside the Saga. Gateway handles JWT + rate limiting.
3. **Hardest part** (2 min) — pick one: Transactional Outbox guaranteeing message delivery, OR
   Saga compensation when step 3 of 4 fails, OR optimistic concurrency on concurrent bids.
4. **Trade-off you consciously made** (1 min) — PostgreSQL jsonb instead of Cosmos DB (explain
   why), OR polling Outbox instead of LISTEN/NOTIFY (explain why), OR REST over gRPC for external
   surfaces (explain when you'd flip it).
5. **What you'd add next** (30 sec) — Kafka analytics tier, real auth provider, Kubernetes
   deployment, ML rate-recommendation sidecar.

---

*FreightFlow BUILDPLAN · v1.0 · April 2026 · Stage 1: ~14 days focused work · Stage 2: +3–4 days*
