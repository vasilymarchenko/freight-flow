# FreightFlow — Manual Testing Guide

> Run requests top to bottom. Each step captures IDs that the next step needs.
> All requests go through the Gateway (`localhost:8080`).
> For direct-service bypass during development, swap `8080` for `5000` (RfpApi) or `5001` (CarrierApi).

---

## Prerequisites

```bash
# Start all containers
docker compose -f docker-compose.stage1.yml up --build

# Verify all services are up and healthy
docker compose ps
curl -s http://localhost:8080/health  # gateway
curl -s http://localhost:5000/health  # rfp-api
curl -s http://localhost:5001/health  # carrier-api
curl -s http://localhost:5002/health  # workflow-worker
```

Expected: all return `200 OK` with `{"status":"Healthy"}`.

---

## Variables

Keep these values as you go through the steps:

```
TOKEN        = (from Step 1)
CARRIER_ID   = (from Step 2)
RFP_ID       = (from Step 3)
LANE_ID      = (from Step 4)
BID_ID       = (from Step 6)
```

---

## Happy Path

### Step 1 — Obtain a JWT

**Business action:** Authenticate as a shipper to get a bearer token.

```http
POST http://localhost:8080/token
Content-Type: application/json

{
  "sub": "shipper-1",
  "role": "shipper"
}
```

| Expected | Detail |
|---|---|
| `200 OK` | `{ "token": "<jwt>" }` |
| Token is HS256, 1-hour TTL | Issuer: `freight-gateway`, Audience: `freight-flow` |

Save the token value as `TOKEN`. All subsequent requests include:
```
Authorization: Bearer {{TOKEN}}
```

---

### Step 2 — Onboard a Carrier

**Business action:** A trucking company registers on the platform.

```http
POST http://localhost:8080/carriers
Authorization: Bearer {{TOKEN}}
Content-Type: application/json

{
  "dotNumber":       "1234567",
  "name":            "Acme Trucking LLC",
  "insuranceExpiry": "2027-12-31",
  "equipmentTypes":  ["DryVan"],
  "certifications":  [],
  "notes":           null
}
```

| Expected | Detail |
|---|---|
| `201 Created` | `{ "id": "<uuid>" }` — save as `CARRIER_ID` |
| `Location` header | `/carriers/{CARRIER_ID}` |
| Background event | `CarrierOnboarded` published to RabbitMQ via Outbox (visible in RabbitMQ mgmt UI at `:15672`) |

**Verify carrier was saved:**

```http
GET http://localhost:8080/carriers/{{CARRIER_ID}}
Authorization: Bearer {{TOKEN}}
```

Expected `200 OK`:
```json
{
  "id":              "{{CARRIER_ID}}",
  "dotNumber":       "1234567",
  "name":            "Acme Trucking LLC",
  "authorityStatus": "Active",
  "insuranceExpiry": "2027-12-31",
  "profile": {
    "equipmentTypes": ["DryVan"],
    "certifications": [],
    "notes":          null
  },
  "createdAt": "..."
}
```

---

### Step 3 — Create an RFP

**Business action:** Shipper opens a request for proposal.

```http
POST http://localhost:8080/rfps
Authorization: Bearer {{TOKEN}}
Content-Type: application/json

{
  "shipperId":    "00000000-0000-0000-0000-000000000001",
  "openAt":       "2026-05-01T00:00:00Z",
  "closeAt":      "2026-06-30T00:00:00Z",
  "maxBidRounds": 3
}
```

| Expected | Detail |
|---|---|
| `201 Created` | `{ "id": "<uuid>" }` — save as `RFP_ID` |
| `Location` header | `/rfps/{RFP_ID}` |
| Background event | `RfpCreated` published via Outbox |
| RFP status | `Draft` |

---

### Step 4 — Add a Lane to the RFP

**Business action:** Shipper defines a shipping route with volume and freight class.

```http
POST http://localhost:8080/rfps/{{RFP_ID}}/lanes
Authorization: Bearer {{TOKEN}}
Content-Type: application/json

{
  "originZip":    "90210",
  "destZip":      "10001",
  "freightClass": "Class70",
  "volume":       100
}
```

| Expected | Detail |
|---|---|
| `201 Created` | `{ "id": "<uuid>" }` — save as `LANE_ID` |
| `Location` header | `/rfps/{RFP_ID}/lanes/{LANE_ID}` |
| RFP status | still `Draft` |

---

### Step 5 — Open the RFP

**Business action:** Shipper opens the RFP for bidding. Status transitions `Draft → Open`.

```http
POST http://localhost:8080/rfps/{{RFP_ID}}/open
Authorization: Bearer {{TOKEN}}
```

| Expected | Detail |
|---|---|
| `200 OK` | empty body |
| Background event | `RfpOpened` published via Outbox |
| RFP status | `Open` |

**Verify:**

```http
GET http://localhost:8080/rfps/{{RFP_ID}}
Authorization: Bearer {{TOKEN}}
```

Confirm `"status": "Open"` in the response.

---

### Step 6 — Carrier Submits a Bid

**Business action:** A carrier quotes a price for the lane.

> The `Idempotency-Key` header makes this safe to retry — the same key returns the same `201` without re-processing.

```http
POST http://localhost:8080/rfps/{{RFP_ID}}/bids
Authorization: Bearer {{TOKEN}}
Idempotency-Key: bid-key-001
Content-Type: application/json

{
  "carrierId": "{{CARRIER_ID}}",
  "lanePrices": [
    {
      "laneId":   "{{LANE_ID}}",
      "amount":   1250.00,
      "currency": "USD"
    }
  ]
}
```

| Expected | Detail |
|---|---|
| `201 Created` | `{ "id": "<uuid>" }` — save as `BID_ID` |
| `Location` header | `/rfps/{RFP_ID}/bids/{BID_ID}` |
| Background event | `BidSubmitted` published via Outbox |

**Read active bids (Dapper hot path):**

```http
GET http://localhost:8080/rfps/{{RFP_ID}}/bids
Authorization: Bearer {{TOKEN}}
```

Expected `200 OK` — one row per `(bid, lane)`, lowest amount first:
```json
[
  {
    "bidId":     "{{BID_ID}}",
    "carrierId": "{{CARRIER_ID}}",
    "round":     1,
    "laneId":    "{{LANE_ID}}",
    "amount":    1250.00,
    "currency":  "USD"
  }
]
```

---

### Step 7 — Close the RFP

**Business action:** Bidding period ends; RFP transitions `Open → Closed` before awarding.

```http
POST http://localhost:8080/rfps/{{RFP_ID}}/close
Authorization: Bearer {{TOKEN}}
```

| Expected | Detail |
|---|---|
| `200 OK` | empty body |
| Background event | `RfpClosed` published via Outbox |
| RFP status | `Closed` |

Verify:

```http
GET http://localhost:8080/rfps/{{RFP_ID}}
Authorization: Bearer {{TOKEN}}
```

Confirm `"status": "Closed"`.

---

### Step 8 — Award the Winning Bid

**Business action:** Shipper selects the winning carrier. Triggers the AwardWorkflow Saga asynchronously.

```http
POST http://localhost:8080/rfps/{{RFP_ID}}/awards
Authorization: Bearer {{TOKEN}}
Content-Type: application/json

{
  "bidId": "{{BID_ID}}"
}
```

| Expected | Detail |
|---|---|
| `202 Accepted` | empty body — saga starts asynchronously |
| Background event | `AwardIssued` published via Outbox → WorkflowWorker consumes it |
| RFP status | immediately transitions to `Awarded` |

**Watch the saga progress in logs:**

```bash
docker logs freight-workflow-worker --follow
```

Look for these log lines in order:
```
Saga <id>: ReserveCapacity succeeded for carrier ...
Saga <id>: Contract <contractId> issued.
Saga <id>: ShipperNotified published.
Saga <id>: RfpMarkAsAwarded published for RFP <id>. Workflow complete.
```

---

### Step 9 — Confirm Final State

**Business action:** Verify the end-to-end result.

```http
GET http://localhost:8080/rfps/{{RFP_ID}}
Authorization: Bearer {{TOKEN}}
```

Expected `200 OK`:
```json
{
  "id":     "{{RFP_ID}}",
  "status": "Awarded",
  "lanes":  [...],
  "bids":   [...],
  "award": {
    "bidId":     "{{BID_ID}}",
    "carrierId": "{{CARRIER_ID}}",
    "awardedAt": "..."
  }
}
```

Check RabbitMQ management UI (`http://localhost:15672`, guest/guest) to confirm all queues are empty (all messages processed).

---

## Typical Failure Scenarios

### F-1 — Missing or Invalid JWT

**Action:** Call any protected endpoint without a token.

```http
POST http://localhost:8080/rfps
Content-Type: application/json

{ "shipperId": "...", ... }
```

| Expected | Detail |
|---|---|
| `401 Unauthorized` | Gateway rejects the request before it reaches the service |

---

### F-2 — Validation Error (malformed request body)

**Action:** Create an RFP with `closeAt` in the past.

```http
POST http://localhost:8080/rfps
Authorization: Bearer {{TOKEN}}
Content-Type: application/json

{
  "shipperId":    "00000000-0000-0000-0000-000000000001",
  "openAt":       "2020-01-01T00:00:00Z",
  "closeAt":      "2020-06-01T00:00:00Z",
  "maxBidRounds": 3
}
```

| Expected | Detail |
|---|---|
| `400 Bad Request` | RFC 7807 `ValidationProblem` — `"CloseAt must be in the future."` |

---

### F-3 — Domain Rule Violation: Lane Added to Non-Draft RFP

**Action:** Attempt to add a lane after the RFP has been opened (Step 5).

```http
POST http://localhost:8080/rfps/{{RFP_ID}}/lanes
Authorization: Bearer {{TOKEN}}
Content-Type: application/json

{
  "originZip": "10001",
  "destZip":   "77001",
  "freightClass": "Class85",
  "volume": 50
}
```

| Expected | Detail |
|---|---|
| `422 Unprocessable Entity` | `"Cannot add a lane to an RFP that is not in Draft status."` |

---

### F-4 — Domain Rule Violation: Bid on a Non-Open RFP

**Action:** Submit a bid when the RFP is in `Draft` or `Closed` state.

```http
POST http://localhost:8080/rfps/{{RFP_ID}}/bids
Authorization: Bearer {{TOKEN}}
Content-Type: application/json

{
  "carrierId": "{{CARRIER_ID}}",
  "lanePrices": [{ "laneId": "{{LANE_ID}}", "amount": 999.00, "currency": "USD" }]
}
```

| Expected | Detail |
|---|---|
| `422 Unprocessable Entity` | `"RFP is not open for bidding."` |

---

### F-5 — Domain Rule Violation: Award a Non-Closed RFP

**Action:** POST to `/awards` while the RFP is still `Open`.

```http
POST http://localhost:8080/rfps/{{RFP_ID}}/awards
Authorization: Bearer {{TOKEN}}
Content-Type: application/json

{ "bidId": "{{BID_ID}}" }
```

| Expected | Detail |
|---|---|
| `422 Unprocessable Entity` | `"Can only award an RFP that is in Closed status."` |

---

### F-6 — Domain Rule Violation: Bid Round Exceeded

**Action:** Submit a fourth bid when `maxBidRounds = 3`.

Repeat the bid submission from Step 6 three additional times (each with a unique `Idempotency-Key`).
On the fourth attempt:

| Expected | Detail |
|---|---|
| `422 Unprocessable Entity` | `"Cannot exceed maximum bid rounds of 3."` |

---

### F-7 — Idempotency: Duplicate Bid Submission

**Action:** Re-submit the same bid using the same `Idempotency-Key`.

```http
POST http://localhost:8080/rfps/{{RFP_ID}}/bids
Authorization: Bearer {{TOKEN}}
Idempotency-Key: bid-key-001       ← same key as Step 6
Content-Type: application/json

{
  "carrierId": "{{CARRIER_ID}}",
  "lanePrices": [
    { "laneId": "{{LANE_ID}}", "amount": 1250.00, "currency": "USD" }
  ]
}
```

| Expected | Detail |
|---|---|
| `201 Created` | Same body and `BID_ID` as the original request |
| No new bid created | Middleware replayed the cached response; handler never executed |
| No new DB row | Confirm with `GET /rfps/{RFP_ID}/bids` — still one row |

---

### F-8 — Optimistic Concurrency Conflict

**Action:** Simulate two simultaneous bids hitting the same RFP. This is best reproduced with a script:

```bash
# Two concurrent bids — one will win, one will get 409
curl -s -X POST http://localhost:8080/rfps/{{RFP_ID}}/bids \
  -H "Authorization: Bearer {{TOKEN}}" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: concurrent-bid-A" \
  -d '{"carrierId":"{{CARRIER_ID}}","lanePrices":[{"laneId":"{{LANE_ID}}","amount":1100,"currency":"USD"}]}' &

curl -s -X POST http://localhost:8080/rfps/{{RFP_ID}}/bids \
  -H "Authorization: Bearer {{TOKEN}}" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: concurrent-bid-B" \
  -d '{"carrierId":"{{CARRIER_ID}}","lanePrices":[{"laneId":"{{LANE_ID}}","amount":1150,"currency":"USD"}]}' &

wait
```

| Expected | One request |
|---|---|
| `201 Created` | Bid accepted |
| **Other request:** `409 Conflict` | `Retry-After: 1` header present; retry after 1 second |

---

### F-9 — Carrier Not Found

**Action:** Submit a bid with a non-existent carrier ID.

```http
POST http://localhost:8080/rfps/{{RFP_ID}}/bids
Authorization: Bearer {{TOKEN}}
Content-Type: application/json

{
  "carrierId": "00000000-0000-0000-0000-000000000999",
  "lanePrices": [{ "laneId": "{{LANE_ID}}", "amount": 800.00, "currency": "USD" }]
}
```

| Expected | Detail |
|---|---|
| `201 Created` | Bid is recorded — the RFP API does not validate carrier existence at bid time; validation happens in the saga when capacity is reserved |

> To see a saga-level failure: if the carrier does not have a capacity record for the lane when the saga runs `ReserveCapacity`, the gRPC call returns `success: false` → the saga compensates by releasing capacity and the workflow transitions to `CompensationPending`. Observe this in the workflow-worker logs.

---

### F-10 — Rate Limit Exceeded

**Action:** Send more than 1000 requests per minute for the same authenticated `sub`.

```bash
for i in $(seq 1 1005); do
  curl -s -o /dev/null -w "%{http_code}\n" \
    -H "Authorization: Bearer {{TOKEN}}" \
    http://localhost:8080/rfps/{{RFP_ID}}
done
```

| Expected | Detail |
|---|---|
| First 1000 | `200 OK` |
| Requests 1001+ | `429 Too Many Requests` |

---

### F-11 — Carrier Onboard with Expired Insurance

**Action:** Register a carrier with an insurance expiry date in the past.

```http
POST http://localhost:8080/carriers
Authorization: Bearer {{TOKEN}}
Content-Type: application/json

{
  "dotNumber":       "9999999",
  "name":            "Expired Carrier Inc",
  "insuranceExpiry": "2020-01-01",
  "equipmentTypes":  ["DryVan"],
  "certifications":  []
}
```

| Expected | Detail |
|---|---|
| `400 Bad Request` | RFC 7807 — `"Insurance expiry must be in the future."` |

---

### F-12 — Resource Not Found

**Action:** Request an RFP or Carrier that does not exist.

```http
GET http://localhost:8080/rfps/00000000-0000-0000-0000-000000000000
Authorization: Bearer {{TOKEN}}
```

| Expected | Detail |
|---|---|
| `404 Not Found` | RFC 7807 Problem Details — `"Not Found"` |

---

## Health Checks Reference

| Endpoint | Auth | What it checks |
|---|---|---|
| `GET :8080/health` | none | Gateway process alive |
| `GET :5000/health` | none | RfpApi process alive |
| `GET :5000/ready` | none | RfpApi + PostgreSQL + Redis + RabbitMQ reachable |
| `GET :5001/health` | none | CarrierApi process alive |
| `GET :5001/ready` | none | CarrierApi + PostgreSQL + Redis + RabbitMQ reachable |
| `GET :5002/health` | none | WorkflowWorker process alive |

---

## RabbitMQ Event Reference

| Trigger | Event | Consumer |
|---|---|---|
| `POST /rfps` | `RfpCreated` | none in Stage 1 |
| `POST /rfps/{id}/open` | `RfpOpened` | none in Stage 1 |
| `POST /rfps/{id}/bids` | `BidSubmitted` | none in Stage 1 |
| `POST /rfps/{id}/awards` | `AwardIssued` | WorkflowWorker — starts `AwardWorkflow` saga |
| `POST /carriers` | `CarrierOnboarded` | none in Stage 1 |
| Saga step 4 | `RfpMarkAsAwarded` | RfpApi `RfpMarkAsAwardedConsumer` — attaches ContractId, publishes `RfpAwardAcknowledged` |
| RfpApi consumer | `RfpAwardAcknowledged` | WorkflowWorker — advances saga to `Completed` |

Outbox poll interval: 5 seconds. Poison messages dead-lettered after 3 retries.  
Management UI: `http://localhost:15672` (guest / guest).
