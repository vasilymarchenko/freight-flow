# FreightFlow — Domain Reference

> Use this file when writing or reviewing any code that touches domain concepts,
> aggregate boundaries, or the ubiquitous language. Use these exact terms in class
> names, method names, variable names, and comments — do not invent synonyms.

---

## Ubiquitous Language

| Term | Definition | Notes |
|---|---|---|
| **Shipper** | A company that needs to move goods. Creates RFPs and awards contracts. | Always the RFP-creator side |
| **Carrier** | A trucking company. Onboards to the platform, discovers RFPs, submits bids. | Always the bid-submitting side |
| **RFP** | Request for Proposal. A structured solicitation covering one or more Lanes, with an open/close date and a max bid-round count. | Aggregate root in RfpApi |
| **Lane** | A shipping route: origin ZIP → destination ZIP, freight class, expected volume. An RFP contains 1–500 lanes. | Value object / child entity of RFP |
| **Bid** | A carrier's price offer for one or more lanes in an open RFP. Immutable once submitted. A new bid round creates a new Bid. | Entity inside RFP aggregate |
| **Award** | The shipper's decision to accept a specific Bid. Triggers the award workflow Saga. | Entity inside RFP aggregate; created by `RFP.IssueAward()` |
| **Contract** | The binding record produced by a completed award workflow. Links Shipper, Carrier, Lane, and agreed rate. | Aggregate root in WorkflowWorker; read-only after creation |
| **Rate Event** | An immutable fact emitted for every bid submitted. Stored directly in Stage 1; streamed via Kafka in Stage 2. | Used by analytics worker |
| **Capacity Record** | A carrier's statement of available volume on a specific lane, with reservation state. | Entity inside Carrier aggregate |
| **Carrier Profile** | Variable-attribute document (insurance, certifications, equipment types). Stored as `jsonb` in dev; Cosmos DB / MongoDB in production. | Value object in Carrier aggregate |
| **DOT Number** | USDOT registration number — primary identifier for a carrier with federal authorities. | Strongly-typed value object `DotNumber` |
| **Freight Class** | NMFC classification (Class50 through Class500) determining shipping rates. | Enum in SharedKernel |
| **FMCSA SAFER** | Federal Motor Carrier Safety Administration's Safety and Fitness Electronic Records — carrier compliance check. | Stubbed in local dev |

---

## Aggregate Boundaries

### RFP Aggregate (`FreightFlow.RfpApi`)

```
RFP (aggregate root)
  ├─ Id: RfpId
  ├─ ShipperId: Guid
  ├─ Status: RfpStatus { Draft, Open, Closed, Awarded }
  ├─ OpenAt: DateTimeOffset
  ├─ CloseAt: DateTimeOffset
  ├─ MaxBidRounds: int
  ├─ Lanes: IReadOnlyList<Lane>          ← value objects
  ├─ Bids: IReadOnlyList<Bid>            ← entities
  └─ Award: Award?                       ← entity, null until awarded
```

**Invariants — throw `DomainException`, never `ArgumentException`:**
- Cannot add a Lane to an RFP not in `Draft`
- Cannot submit a Bid to an RFP not in `Open`
- Cannot submit more Bid rounds than `MaxBidRounds`
- Cannot award an RFP that has no bids
- Cannot award an RFP already in `Awarded`
- Cannot close an RFP not in `Open`

**Domain events (collected on aggregate, dispatched by interceptor after `SaveChanges`):**
- `RfpCreated` — on construction
- `RfpOpened` — on `Open()` transition
- `RfpClosed` — on `Close()` transition
- `BidSubmitted` — on each `SubmitBid()` call
- `AwardIssued` — on `IssueAward()` — this triggers the Saga

---

### Carrier Aggregate (`FreightFlow.CarrierApi`)

```
Carrier (aggregate root)
  ├─ Id: CarrierId
  ├─ DotNumber: DotNumber
  ├─ Name: string
  ├─ AuthorityStatus: AuthorityStatus { Active, Inactive, Revoked }
  ├─ InsuranceExpiry: DateOnly
  ├─ Profile: CarrierProfile             ← jsonb; variable-attribute document
  └─ CapacityRecords: IReadOnlyList<CapacityRecord>
        ├─ LaneId: Guid
        ├─ AvailableVolume: int
        └─ ReservedVolume: int           ← xmin concurrency token on this entity
```

**Invariants:**
- Cannot reserve capacity if `AuthorityStatus != Active`
- Cannot reserve capacity that would make `AvailableVolume - ReservedVolume < 0`
- Cannot release a reservation that does not exist

**Domain events:**
- `CarrierOnboarded` — written to Outbox on registration

---

### Contract Aggregate (`FreightFlow.WorkflowWorker`)

```
Contract (aggregate root — read-only after creation)
  ├─ Id: ContractId
  ├─ RfpId: RfpId
  ├─ CarrierId: CarrierId
  ├─ LaneId: LaneId
  ├─ AgreedRate: Money
  ├─ IssuedAt: DateTimeOffset
  └─ Status: ContractStatus { Draft, Active, Void }
```

Created by Saga step 2. Never mutated after `Status → Active`. Saga compensation sets `Status → Void`.

---

## Bounded Contexts & Service Ownership

| Bounded Context | Service | Owns |
|---|---|---|
| Procurement | `FreightFlow.RfpApi` | RFP lifecycle, Lane, Bid, Award |
| Carrier Management | `FreightFlow.CarrierApi` | Carrier onboarding, capacity, gRPC `ReserveCapacity` |
| Award Workflow | `FreightFlow.WorkflowWorker` | Saga state, Contract |
| Gateway / Security | `FreightFlow.Gateway` | JWT validation, rate limiting, routing |
| Analytics (Stage 2) | `freight-analytics-worker` | Rate facts, materialized benchmarking indices |

**Cross-context communication:**
- RfpApi → WorkflowWorker: `AwardIssued` event via RabbitMQ (Outbox)
- WorkflowWorker → CarrierApi: `ReserveCapacity` gRPC call (synchronous, Saga step 1)
- CarrierApi → WorkflowWorker: `BidSubmitted` command via RabbitMQ
- CarrierApi → Kafka (Stage 2): `RateEvent` to `rate-stream` topic, keyed by `lane_id`

---

## Value Objects (SharedKernel)

```csharp
public readonly record struct Money(decimal Amount, string Currency);       // Amount ≥ 0; Currency = ISO 4217 3-char
public readonly record struct ZipCode(string Value);                        // 5-digit US ZIP
public readonly record struct DotNumber(string Value);                      // non-empty numeric string

public enum FreightClass
{
    Class50, Class55, Class60, Class65, Class70, Class77_5, Class85,
    Class92_5, Class100, Class110, Class125, Class150, Class175,
    Class200, Class250, Class300, Class400, Class500
}
```

Each validates in its constructor and throws `DomainException` on invalid input.

---

## Domain Exception Hierarchy

```
DomainException (base)               → HTTP 422 Unprocessable Entity
  ├─ RfpNotOpenException
  ├─ MaxBidRoundsExceededException
  ├─ CarrierNotActiveException
  └─ InsufficientCapacityException

NotFoundException                    → HTTP 404 Not Found
  ├─ RfpNotFoundException
  └─ CarrierNotFoundException
```

All in SharedKernel. `DomainExceptionMiddleware` in each API maps these to RFC 7807 responses.

---

## Stage 2 — Kafka Additions

Only relevant after Stage 1 is complete and verified.

- **Producer**: added to `CarrierApi` — publishes `RateEvent` to `rate-stream` topic, keyed by `lane_id` for per-lane ordering
- **Consumer**: `freight-analytics-worker` — `Confluent.Kafka` directly (no MassTransit), manual offset commit after upsert
- **Topic**: `rate-stream`, partitioned by `lane_id`
- **Analytics DB**: `analytics_db`, PostgreSQL, `rate_facts` partitioned by `submitted_at` month
- **Materialized views**: `mv_lane_median_rate`, refreshed hourly with `REFRESH MATERIALIZED VIEW CONCURRENTLY` (requires a unique index on `lane_id`)
