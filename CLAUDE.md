# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Run all tests (requires Docker running)
export PATH="$HOME/.dotnet:$PATH"   # if .NET 10 SDK is in ~/.dotnet
dotnet test eShop.slnx

# Run a single test project
dotnet test tests/EShop.IntegrationTests/EShop.IntegrationTests.csproj
dotnet test tests/EShop.ContractTests/EShop.ContractTests.csproj

# Run a specific test by name
dotnet test eShop.slnx --filter "FullyQualifiedName~Post_Order_Flows_To_Notification"

# Run all Docker services
docker compose up -d --build

# Tear down (including volumes)
docker compose down -v

# Add an EF Core migration (example for Order.Api)
dotnet ef migrations add <Name> --project src/Services/Order/Order.Api --startup-project src/Services/Order/Order.Api
```

## Architecture

Event-driven pipeline on .NET 10, Kafka (KRaft/bitnami), and PostgreSQL. Each service owns an isolated Postgres database. Services communicate exclusively via Kafka. There are no synchronous service-to-service calls.

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

### Key cross-cutting patterns

**Transactional Outbox** (`EShop.Messaging.Kafka`): Instead of publishing to Kafka directly, handlers write an `OutboxMessage` row in the same DB transaction as the domain change. `OutboxDispatcherBackgroundService<TDbContext>` polls and publishes these rows, then marks them sent.

**Inbox / idempotency** (`ProcessedMessage`): Before processing an event, handlers check `ProcessedMessages` table by `EventId`. If found, they skip and roll back. The check + domain write + outbox enqueue happen in one transaction.

**DI wiring** (`MessagingServiceCollectionExtensions`):
- `AddKafkaMessaging(config)` — binds `KafkaOptions` from the `"Kafka"` config section
- `AddKafkaConsumer<TEvent, THandler>(topic)` — registers a `KafkaConsumerBackgroundService` hosted service
- `AddOutboxDispatcher<TDbContext>()` — registers the outbox poller

## Project Structure

```
src/
  BuildingBlocks/
    EShop.Contracts/           # Integration event records + PaymentStatus enum
    EShop.Messaging.Kafka/     # KafkaConsumerBackgroundService, OutboxDispatcherBackgroundService,
                               # OutboxMessage, ProcessedMessage, DI extensions
  Services/
    Order/Order.Api/           # Minimal API (POST /orders, GET /orders/{id}), OrdersDbContext
    Payment/Payment.Service/   # OrderCreatedHandler, PaymentsDbContext
    Notification/Notification.Service/  # PaymentProcessedHandler, NotificationsDbContext
tests/
  EShop.IntegrationTests/      # Testcontainers-based end-to-end pipeline tests
  EShop.ContractTests/         # PactNet 5 consumer + provider contract tests
```

## Test Architecture

### Integration tests (`EShop.IntegrationTests`)

`EShopFixture` (shared, set up once per NUnit assembly run via `AssemblySetup`) spins up:
- 1 Kafka container (`confluentinc/cp-kafka:7.6.1`)
- 3 Postgres containers (orders / payments / notifications)
- 3 `WebApplicationFactory` instances wired to those containers

Tests poll the DB with a 30-second deadline to observe async pipeline results. Docker must be running; no other external infra needed.

### Contract tests (`EShop.ContractTests`)

Uses **PactNet 5** (message pacts for Kafka events, HTTP pacts for REST):

- **Consumer tests** (`Consumer*.cs`) — define the contract and write pact JSON to `bin/.../pacts/`
- **Provider tests** (`Provider*.cs`) — verify the real implementation satisfies the recorded pact

`ProviderPaymentServiceTests` contains a P/Invoke workaround for a PactNet 5.0.1 bug where `WithMessages()` registers `scheme="message"` instead of `"http"` in pact-ffi. The `FixMessageProviderScheme` method re-registers with `scheme="http"` via reflection + native interop.

`OrderApiFactory` hosts Order.Api on a real Kestrel TCP port (needed because Pact verifier sends genuine HTTP traffic; `TestServer`'s in-memory pipe is insufficient). It builds two hosts: a `TestServer` host for internal DI, and a Kestrel host on a free port.

## Configuration

All runtime config is via environment variables (Docker Compose sets them). For tests, `WebApplicationFactory.ConfigureServices` replaces `DbContextOptions` and `KafkaOptions` with container-derived values.

| Variable | Service | Notes |
|---|---|---|
| `ConnectionStrings__Default` | all | Postgres connection string |
| `Kafka__BootstrapServers` | all | e.g. `kafka:9092` |
| `Kafka__ConsumerGroupId` | all | service-specific group |
| `Kafka__OutboxPollingIntervalSeconds` | order, payment | default `2` |
| `Payment__FailureThreshold` | payment | orders above this amount fail; default `999999.99` |
| `Database__AutoMigrate` | all | runs EF migrations on startup when `true` |

## Package Management

Uses **Central Package Management** (`Directory.Packages.props`). Add version numbers only in `Directory.Packages.props`, not in individual `.csproj` files. `Directory.Build.props` sets `TargetFramework=net10.0`, `Nullable=enable`, `ImplicitUsings=enable` for the whole solution.
