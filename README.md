# microservices-eshop-automation

Event-driven microservices stack on **.NET 10**, **Kafka** (KRaft), and **PostgreSQL** — fully orchestrated with Docker Compose.

## Architecture

```
Client
  │  HTTP POST /orders
  ▼
Order.Api (:8081)
  │  Transactional Outbox → Kafka topic: orders.created
  ▼
Payment.Service (:8082)          ← Kafka consumer (group: payment-service)
  │  Idempotent + Outbox → Kafka topic: payments.processed
  ▼
Notification.Service (:8083)     ← Kafka consumer (group: notification-service)
     Idempotent → persists Notification row + Serilog log
```

Each service owns its own **PostgreSQL** database (separate container). Services communicate exclusively through Kafka. The Transactional Outbox pattern guarantees at-least-once delivery without distributed transactions.

## Prerequisites

| Tool | Version |
|---|---|
| Docker + Docker Compose | >= 24 |
| .NET SDK | 10.0 (only needed to run tests or generate migrations locally) |

## Quick start

```bash
docker compose up -d --build
# Wait ~30 s for all health checks to pass
docker compose ps     # all services should show (healthy)
```

### Create an order

```bash
curl -X POST http://localhost:8081/orders \
  -H 'Content-Type: application/json' \
  -d '{
    "customerId": "alice",
    "items": [
      { "sku": "WIDGET-1", "quantity": 2, "price": 24.99 }
    ]
  }'
# Returns: 201 Created with order JSON
```

### Verify the full pipeline

| Step | What to check |
|---|---|
| Kafka topics | Open Kafka UI at http://localhost:8080 → Topics |
| Payment persisted | `docker exec eshop-postgres-payments psql -U postgres -d payments -c "select * from payments.payments;"` |
| Notification persisted | `docker exec eshop-postgres-notifications psql -U postgres -d notifications -c "select * from notifications.notifications;"` |
| Notification log | `docker compose logs notification-service` |

## Services

| Service | Port | DB | Role |
|---|---|---|---|
| Order.Api | 8081 | postgres-orders:5432 | REST entry, publishes OrderCreated |
| Payment.Service | 8082 | postgres-payments:5433 | Consumes OrderCreated, publishes PaymentProcessed |
| Notification.Service | 8083 | postgres-notifications:5434 | Terminal consumer, logs + persists notifications |
| Kafka UI | 8080 | — | Topic inspection |

## Running integration tests

Tests use **Testcontainers** — Docker must be running. No external services needed.

```bash
export PATH="$HOME/.dotnet:$PATH"   # only needed if .NET 10 SDK is in ~/.dotnet
dotnet test eShop.slnx
```

Tests cover:
- **Happy path**: POST /orders flows through to a Notification row within 30 s.
- **Idempotency**: duplicate OrderCreated messages produce exactly one Payment row.
- **Failure path**: any payment outcome produces a Notification.

## Configuration

All config is via environment variables (Docker Compose already sets these):

| Variable | Service | Default |
|---|---|---|
| `ConnectionStrings__Default` | all | see appsettings.json |
| `Kafka__BootstrapServers` | all | `kafka:9092` |
| `Kafka__ConsumerGroupId` | all | service-specific |
| `Kafka__OutboxPollingIntervalSeconds` | order, payment | `2` |
| `Payment__FailureThreshold` | payment | `999999.99` |
| `Database__AutoMigrate` | all | `true` |

Set `Payment__FailureThreshold` to a small value (e.g. `10`) to trigger the failure branch.

## Project structure

```
src/
  BuildingBlocks/
    EShop.Contracts/              # integration event records + PaymentStatus enum
    EShop.Messaging.Kafka/        # KafkaConsumerBackgroundService, OutboxDispatcher,
                                  # OutboxMessage, ProcessedMessage, DI extensions
  Services/
    Order/Order.Api/              # REST API, OrdersDbContext, outbox migration
    Payment/Payment.Service/      # Kafka consumer, PaymentsDbContext
    Notification/Notification.Service/ # Kafka consumer, NotificationsDbContext
tests/
  EShop.IntegrationTests/         # Testcontainers-based integration tests
docker-compose.yml
Directory.Build.props             # TFM net10.0, nullable, CPM
Directory.Packages.props          # central package versions
eShop.slnx
```

## Stopping / cleanup

```bash
docker compose down -v   # also removes volumes
```

