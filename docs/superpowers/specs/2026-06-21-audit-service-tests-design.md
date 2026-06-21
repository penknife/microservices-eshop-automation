# Audit Service Tests Design

**Date:** 2026-06-21  
**Scope:** Contract tests for Audit.Service (consumer message pacts + HTTP provider pact)

## Context

Audit.Service is a new event-driven service that:
- Consumes `OrderCreatedIntegrationEvent` from `orders.created` topic
- Consumes `PaymentProcessedIntegrationEvent` from `payments.processed` topic
- Exposes `GET /audit/orders/{orderId}` → list of `AuditEntryResponse`
- Exposes `GET /audit/summary` → list of `PaymentSummaryResponse` (grouped by date)

Integration tests already exist in `OrderPipelineTests.cs` (`Audit_Receives_Both_Events_For_Order`, `Audit_Summary_Counts_Payment_Status`). This spec covers only the missing contract tests.

## What to Build

### 1. `ConsumerAuditServiceTests.cs` — message consumer pacts

Two NUnit tests using `IMessagePactBuilderV4`:

- `OrderCreatedEvent_ShouldBeConsumedByAuditService` — consumer `AuditService`, provider `OrderService`. Verifies fields: `eventId`, `occurredAt`, `orderId`, `customerId`, `totalAmount`, `items`. Generates `AuditService-OrderService.json`.
- `PaymentProcessedEvent_WithSucceededStatus_ShouldBeConsumedByAuditService` — consumer `AuditService`, provider `PaymentService`. Verifies fields: `eventId`, `occurredAt`, `orderId`, `amount`, `status` (integer 1), `failureReason` null. Generates `AuditService-PaymentService.json`.
- `PaymentProcessedEvent_WithFailedStatus_ShouldBeConsumedByAuditService` — same pact builder, `status` integer 2, non-null `failureReason`.

### 2. `ConsumerAuditApiClientTests.cs` — HTTP consumer pact

Uses `IPactBuilderV4` on port 9002. Consumer `AuditApiClient`, provider `AuditService`.

- `GetOrderAuditTrail_ShouldReturnEntries` — `GET /audit/orders/{orderId}`, expects 200 with JSON array of objects matching `AuditEntryResponse` shape (id, eventId, eventType string, orderId, amount, paymentStatus, failureReason, occurredAt, recordedAt).
- `GetPaymentSummary_ShouldReturnDailySummaries` — `GET /audit/summary`, expects 200 with JSON array matching `PaymentSummaryResponse` shape (date, succeeded, failed, totalAmount).

Generates `AuditApiClient-AuditService.json`.

### 3. `Infrastructure/AuditApiFactory.cs` — Kestrel host for provider verification

Mirrors `OrderApiFactory` pattern:
- `AuditApiFixture` wraps `AuditApiFactory` + `PostgreSqlContainer` (postgres:16, database `audit`)
- `AuditApiFactory` extends `WebApplicationFactory<Audit.Service.ProgramMarker>`, replaces `DbContextOptions<AuditDbContext>` with test container, removes Kafka consumers (no broker in contract tests), injects `AuditProviderStateStartupFilter`
- `AuditProviderStateMiddleware` handles `POST /provider-states` for states:
  - `"an order with audit entries exists"` — seeds one `AuditEntry` of each type for a fixed order ID
  - `"audit entries exist for payment summary"` — seeds a `PaymentProcessed` AuditEntry for today

### 4. `ProviderAuditServiceTests.cs` — HTTP provider verification

One NUnit test `VerifyProvider` that:
- Creates `AuditApiFixture` in `[OneTimeSetUp]`
- Runs `PactVerifier("AuditService")` with `WithHttpEndpoint`, `WithFileSource(AuditApiClient-AuditService.json)`, `WithProviderStateUrl`
- Disposes fixture in `[OneTimeTearDown]`

### 5. `.csproj` update

Add `<ProjectReference Include="..\..\src\Services\Audit\Audit.Service\Audit.Service.csproj" />` to `EShop.ContractTests.csproj`.

## Out of Scope

Message provider tests (proving Order.Api and Payment.Service produce messages that satisfy Audit's consumer pacts) are skipped — the same event shapes are already verified by existing `ConsumerPaymentServiceTests`/`ConsumerNotificationServiceTests` + `ProviderPaymentServiceTests`.
