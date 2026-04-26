# FreightFlow

A freight-procurement platform for learning .NET 10 backend patterns: ASP.NET Core, EF Core, Dapper, MassTransit (RabbitMQ Outbox), gRPC, YARP, and distributed Saga orchestration. Everything runs locally via Docker Compose — no cloud account required.

## Prerequisites

| Tool | Minimum version |
|---|---|
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | 4.x |
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0 |
| [VS Code](https://code.visualstudio.com/) + [REST Client extension](https://marketplace.visualstudio.com/items?itemName=humao.rest-client) | any |

## Running the app (Stage 1)

Start all infrastructure and application containers:

```bash
docker compose -f docker-compose.stage1.yml up --build
```

First run takes a few minutes while Docker builds the images and pulls base layers. Subsequent starts are fast.

### Verify everything is up

```bash
docker compose -f docker-compose.stage1.yml ps
```

All containers should show `(healthy)`. Then:

```bash
curl localhost:5000/health   # freight-rfp-api
curl localhost:5001/health   # freight-carrier-api
curl localhost:5002/health   # freight-workflow-worker
curl localhost:8080/health   # freight-gateway
```

Each returns `{"status":"Healthy"}`.

### Open the API explorer

```
http://localhost:8080/scalar
```

Available after Milestone 5 (YARP + Scalar wired in Gateway).

### RabbitMQ management UI

```
http://localhost:15672
```

Login: `guest` / `guest`

## Port map

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

## Walking through the full flow

Open `docs/walkthrough.http` in VS Code and run requests top-to-bottom with **Ctrl+Alt+R** (REST Client). The file covers: get JWT → onboard carrier → create RFP → add lanes → open → submit bid → award → poll saga state.

## Stopping

```bash
# Stop containers, keep database volumes
docker compose -f docker-compose.stage1.yml down

# Stop and wipe all data volumes (clean slate)
docker compose -f docker-compose.stage1.yml down -v
```

## Running unit tests

```bash
dotnet test tests/FreightFlow.Domain.Tests/
```

Domain tests have zero infrastructure dependencies and run in under a second.

## Building without Docker

```bash
dotnet restore FreightFlow.slnx
dotnet build FreightFlow.slnx
```

## Stage 2 (Kafka + analytics)

Kafka and the analytics worker are defined in `docker-compose.stage2.yml`. Run both overlays together:

```bash
docker compose -f docker-compose.stage1.yml -f docker-compose.stage2.yml up --build
```

Stage 2 is implemented in Milestone 6.

## Build order

`BUILDPLAN.md` is the authoritative source. Milestones must be completed in order — each one is a dependency for the next.

```
Milestone 0 — Repo + infrastructure skeleton   ← current
Milestone 1 — Domain models
Milestone 2 — freight-carrier-api
Milestone 3 — freight-rfp-api
Milestone 4 — freight-workflow-worker (Saga)
Milestone 5 — freight-gateway + smoke test
Milestone 6 — Stage 2: Kafka / analytics
```
