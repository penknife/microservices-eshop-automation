# Audit Service Contract Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add PactNet contract tests covering Audit.Service as a message consumer (OrderCreated + PaymentProcessed) and as an HTTP provider (GET /audit/orders/{id} + GET /audit/summary).

**Architecture:** Three test files + one infrastructure file. Consumer tests generate pact JSON files; the provider test spins Audit.Service on a real Kestrel port backed by Testcontainers Postgres and verifies the pact. No Kafka broker is needed anywhere.

**Tech Stack:** PactNet 5, NUnit 3, Testcontainers (PostgreSQL), ASP.NET Core WebApplicationFactory, .NET 10

## Global Constraints

- All tests go in `tests/EShop.ContractTests/`
- Pact pact files land in `bin/…/pacts/` (set via `Path.GetDirectoryName(Assembly.Location) + "/pacts"`)
- Enums (`PaymentStatus`) serialise as integers (1 = Succeeded, 2 = Failed) — do not use string enum converter
- Follow existing patterns from `ConsumerOrderServiceTests.cs`, `ConsumerNotificationServiceTests.cs`, `OrderApiFactory.cs`
- Run commands with `export PATH="$HOME/.dotnet:$PATH"` prepended if dotnet is not on PATH

---

## File Map

| Action   | Path                                                                          | Purpose                                           |
|----------|-------------------------------------------------------------------------------|---------------------------------------------------|
| Modify   | `tests/EShop.ContractTests/EShop.ContractTests.csproj`                       | Add Audit.Service project reference               |
| Create   | `tests/EShop.ContractTests/ConsumerAuditServiceTests.cs`                     | Message pacts: AuditService←OrderService and AuditService←PaymentService |
| Create   | `tests/EShop.ContractTests/ConsumerAuditApiClientTests.cs`                   | HTTP pact: AuditApiClient→AuditService endpoints  |
| Create   | `tests/EShop.ContractTests/Infrastructure/AuditApiFactory.cs`                | Kestrel host + provider-state seeding for Audit   |
| Create   | `tests/EShop.ContractTests/ProviderAuditServiceTests.cs`                     | HTTP provider verification                        |

---

## Task 1: csproj update + Consumer message pacts

**Files:**
- Modify: `tests/EShop.ContractTests/EShop.ContractTests.csproj`
- Create: `tests/EShop.ContractTests/ConsumerAuditServiceTests.cs`

**Interfaces:**
- Produces: pact files `AuditService-OrderService.json` and `AuditService-PaymentService.json` in the build output `pacts/` directory

- [ ] **Step 1: Add Audit.Service project reference to csproj**

Open `tests/EShop.ContractTests/EShop.ContractTests.csproj`. In the `<ItemGroup>` that contains `ProjectReference` entries, add:

```xml
<ProjectReference Include="..\..\src\Services\Audit\Audit.Service\Audit.Service.csproj" />
```

The full ItemGroup should look like:
```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\BuildingBlocks\EShop.Contracts\EShop.Contracts.csproj" />
  <ProjectReference Include="..\..\src\BuildingBlocks\EShop.Messaging.Kafka\EShop.Messaging.Kafka.csproj" />
  <ProjectReference Include="..\..\src\Services\Order\Order.Api\Order.Api.csproj" />
  <ProjectReference Include="..\..\src\Services\Payment\Payment.Service\Payment.Service.csproj" />
  <ProjectReference Include="..\..\src\Services\Audit\Audit.Service\Audit.Service.csproj" />
</ItemGroup>
```

- [ ] **Step 2: Verify build compiles**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build tests/EShop.ContractTests/EShop.ContractTests.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 3: Create ConsumerAuditServiceTests.cs**

Create `tests/EShop.ContractTests/ConsumerAuditServiceTests.cs`:

```csharp
using PactNet;
using PactNet.Matchers;
using System.Text.Json;

namespace EShop.ContractTests;

[TestFixture]
public sealed class ConsumerAuditServiceTests
{
    private IMessagePactBuilderV4 _orderPact = null!;
    private IMessagePactBuilderV4 _paymentPact = null!;

    [SetUp]
    public void Setup()
    {
        var config = new PactConfig
        {
            PactDir = Path.Combine(
                Path.GetDirectoryName(typeof(ConsumerAuditServiceTests).Assembly.Location)!, "pacts"),
            LogLevel = PactLogLevel.Debug
        };

        _orderPact   = Pact.V4("AuditService", "OrderService",   config).WithMessageInteractions();
        _paymentPact = Pact.V4("AuditService", "PaymentService", config).WithMessageInteractions();
    }

    [Test]
    public async Task OrderCreatedEvent_ShouldBeConsumedByAuditServiceAsync()
    {
        await _orderPact
            .ExpectsToReceive("an order created event for auditing")
            .Given("an order has been created")
            .WithJsonContent(new
            {
                eventId     = Match.Type(Guid.NewGuid()),
                occurredAt  = Match.Type(DateTimeOffset.UtcNow),
                orderId     = Match.Type(Guid.NewGuid()),
                customerId  = Match.Type("customer-123"),
                totalAmount = Match.Decimal(19.98m),
                items       = Match.MinType(new
                {
                    sku      = Match.Type("SKU-001"),
                    quantity = Match.Integer(2),
                    price    = Match.Decimal(9.99m)
                }, 1)
            })
            // PactNet 5 deserialises with default (case-sensitive) options; use JsonElement
            // to access properties by their actual camelCase wire names.
            .VerifyAsync<JsonElement>(message =>
            {
                Assert.That(message.GetProperty("eventId").GetGuid(),          Is.Not.EqualTo(Guid.Empty));
                Assert.That(message.GetProperty("orderId").GetGuid(),          Is.Not.EqualTo(Guid.Empty));
                Assert.That(message.GetProperty("customerId").GetString(),     Is.Not.Empty);
                Assert.That(message.GetProperty("totalAmount").GetDecimal(),   Is.GreaterThan(0));

                var items = message.GetProperty("items");
                Assert.That(items.GetArrayLength(), Is.GreaterThan(0));

                var first = items[0];
                Assert.That(first.GetProperty("sku").GetString(),       Is.Not.Empty);
                Assert.That(first.GetProperty("quantity").GetInt32(),   Is.GreaterThan(0));
                Assert.That(first.GetProperty("price").GetDecimal(),    Is.GreaterThan(0));
                return Task.CompletedTask;
            });
    }

    [Test]
    public async Task PaymentProcessedEvent_WithSucceededStatus_ShouldBeConsumedByAuditServiceAsync()
    {
        await _paymentPact
            .ExpectsToReceive("a payment processed event with succeeded status for auditing")
            .Given("a payment has succeeded")
            .WithJsonContent(new
            {
                eventId       = Match.Type(Guid.NewGuid()),
                occurredAt    = Match.Type(DateTimeOffset.UtcNow),
                orderId       = Match.Type(Guid.NewGuid()),
                paymentId     = Match.Type(Guid.NewGuid()),
                amount        = Match.Decimal(19.98m),
                status        = Match.Integer(1),   // PaymentStatus.Succeeded = 1
                failureReason = (string?)null
            })
            .VerifyAsync<JsonElement>(message =>
            {
                Assert.That(message.GetProperty("eventId").GetGuid(),   Is.Not.EqualTo(Guid.Empty));
                Assert.That(message.GetProperty("orderId").GetGuid(),   Is.Not.EqualTo(Guid.Empty));
                Assert.That(message.GetProperty("amount").GetDecimal(), Is.GreaterThan(0));
                Assert.That(message.GetProperty("status").GetInt32(),   Is.EqualTo(1));
                Assert.That(message.GetProperty("failureReason").ValueKind, Is.EqualTo(JsonValueKind.Null));
                return Task.CompletedTask;
            });
    }

    [Test]
    public async Task PaymentProcessedEvent_WithFailedStatus_ShouldBeConsumedByAuditServiceAsync()
    {
        await _paymentPact
            .ExpectsToReceive("a payment processed event with failed status for auditing")
            .Given("a payment has failed")
            .WithJsonContent(new
            {
                eventId       = Match.Type(Guid.NewGuid()),
                occurredAt    = Match.Type(DateTimeOffset.UtcNow),
                orderId       = Match.Type(Guid.NewGuid()),
                paymentId     = Match.Type(Guid.NewGuid()),
                amount        = Match.Decimal(19.98m),
                status        = Match.Integer(2),   // PaymentStatus.Failed = 2
                failureReason = Match.Type("Amount exceeds configured threshold")
            })
            .VerifyAsync<JsonElement>(message =>
            {
                Assert.That(message.GetProperty("eventId").GetGuid(),        Is.Not.EqualTo(Guid.Empty));
                Assert.That(message.GetProperty("orderId").GetGuid(),        Is.Not.EqualTo(Guid.Empty));
                Assert.That(message.GetProperty("amount").GetDecimal(),      Is.GreaterThan(0));
                Assert.That(message.GetProperty("status").GetInt32(),        Is.EqualTo(2));
                Assert.That(message.GetProperty("failureReason").GetString(), Is.Not.Null.And.Not.Empty);
                return Task.CompletedTask;
            });
    }
}
```

- [ ] **Step 4: Run the consumer message tests**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet test tests/EShop.ContractTests/EShop.ContractTests.csproj \
  --filter "FullyQualifiedName~ConsumerAuditServiceTests" \
  --logger "console;verbosity=normal"
```

Expected: 3 tests pass. Two pact files are written to `tests/EShop.ContractTests/bin/Debug/net10.0/pacts/`:
- `AuditService-OrderService.json`
- `AuditService-PaymentService.json`

Verify they exist:
```bash
ls tests/EShop.ContractTests/bin/Debug/net10.0/pacts/AuditService-*.json
```

- [ ] **Step 5: Commit**

```bash
git add tests/EShop.ContractTests/EShop.ContractTests.csproj \
        tests/EShop.ContractTests/ConsumerAuditServiceTests.cs
git commit -m "test(contracts): add AuditService message consumer pacts for OrderCreated and PaymentProcessed"
```

---

## Task 2: Consumer HTTP pact for Audit API endpoints

**Files:**
- Create: `tests/EShop.ContractTests/ConsumerAuditApiClientTests.cs`

**Interfaces:**
- Produces: pact file `AuditApiClient-AuditService.json` in `pacts/` directory
- Provider states referenced (must match strings used in Task 3's middleware exactly):
  - `"an order with audit entries exists"` — for the `GET /audit/orders/{orderId}` test
  - `"audit entries exist for payment summary"` — for the `GET /audit/summary` test

- [ ] **Step 1: Create ConsumerAuditApiClientTests.cs**

Create `tests/EShop.ContractTests/ConsumerAuditApiClientTests.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using PactNet;
using PactNet.Matchers;

namespace EShop.ContractTests;

// DTOs that represent what an AuditApi client expects to receive.
// paymentStatus is int? because PaymentStatus enum serialises as integer (1 or 2).
public sealed record AuditEntryClientResponse(
    [property: JsonPropertyName("id")]            Guid Id,
    [property: JsonPropertyName("eventId")]       Guid EventId,
    [property: JsonPropertyName("eventType")]     string EventType,
    [property: JsonPropertyName("orderId")]       Guid OrderId,
    [property: JsonPropertyName("amount")]        decimal Amount,
    [property: JsonPropertyName("paymentStatus")] int? PaymentStatus,
    [property: JsonPropertyName("failureReason")] string? FailureReason,
    [property: JsonPropertyName("occurredAt")]    DateTimeOffset OccurredAt,
    [property: JsonPropertyName("recordedAt")]    DateTimeOffset RecordedAt);

public sealed record PaymentSummaryClientResponse(
    [property: JsonPropertyName("date")]        string Date,
    [property: JsonPropertyName("succeeded")]   int Succeeded,
    [property: JsonPropertyName("failed")]      int Failed,
    [property: JsonPropertyName("totalAmount")] decimal TotalAmount);

[TestFixture]
public sealed class ConsumerAuditApiClientTests
{
    private IPactBuilderV4 _pact = null!;
    // Port 9001 is taken by ConsumerOrderServiceTests; use 9002.
    private readonly int _port = 9002;
    private static readonly Guid SeedOrderId = new("22222222-2222-2222-2222-222222222222");

    [SetUp]
    public void Setup()
    {
        var config = new PactConfig
        {
            PactDir = Path.Combine(
                Path.GetDirectoryName(typeof(ConsumerAuditApiClientTests).Assembly.Location)!, "pacts"),
            LogLevel = PactLogLevel.Debug
        };

        _pact = Pact.V4("AuditApiClient", "AuditService", config).WithHttpInteractions(_port);
    }

    [Test]
    public async Task GetOrderAuditTrail_ShouldReturnAuditEntriesAsync()
    {
        _pact
            .UponReceiving("get audit trail for an order")
            .Given("an order with audit entries exists")
            .WithRequest(HttpMethod.Get, $"/audit/orders/{SeedOrderId}")
            .WillRespond()
            .WithStatus(System.Net.HttpStatusCode.OK)
            .WithHeader("Content-Type", "application/json; charset=utf-8")
            .WithJsonBody(Match.MinType(new
            {
                id            = Match.Type(Guid.NewGuid()),
                eventId       = Match.Type(Guid.NewGuid()),
                eventType     = Match.Type("OrderCreated"),
                orderId       = Match.Type(SeedOrderId),
                amount        = Match.Decimal(19.98m),
                paymentStatus = (int?)null,
                failureReason = (string?)null,
                occurredAt    = Match.Type(DateTimeOffset.UtcNow),
                recordedAt    = Match.Type(DateTimeOffset.UtcNow),
            }, 1));

        await _pact.VerifyAsync(async ctx =>
        {
            using var client = new HttpClient { BaseAddress = ctx.MockServerUri };
            var response = await client.GetAsync($"/audit/orders/{SeedOrderId}");

            Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK));
            var entries = await response.Content
                .ReadFromJsonAsync<List<AuditEntryClientResponse>>();
            Assert.That(entries,           Is.Not.Null);
            Assert.That(entries!,          Is.Not.Empty);
            Assert.That(entries[0].EventType, Is.Not.Empty);
            Assert.That(entries[0].Amount, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task GetPaymentSummary_ShouldReturnDailySummariesAsync()
    {
        _pact
            .UponReceiving("get payment summary")
            .Given("audit entries exist for payment summary")
            .WithRequest(HttpMethod.Get, "/audit/summary")
            .WillRespond()
            .WithStatus(System.Net.HttpStatusCode.OK)
            .WithHeader("Content-Type", "application/json; charset=utf-8")
            .WithJsonBody(Match.MinType(new
            {
                date        = Match.Type("2026-06-21"),
                succeeded   = Match.Integer(0),
                failed      = Match.Integer(0),
                totalAmount = Match.Decimal(0m),
            }, 1));

        await _pact.VerifyAsync(async ctx =>
        {
            using var client = new HttpClient { BaseAddress = ctx.MockServerUri };
            var response = await client.GetAsync("/audit/summary");

            Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK));
            var summaries = await response.Content
                .ReadFromJsonAsync<List<PaymentSummaryClientResponse>>();
            Assert.That(summaries,  Is.Not.Null);
            Assert.That(summaries!, Is.Not.Empty);
            Assert.That(summaries[0].Date, Is.Not.Empty);
        });
    }
}
```

- [ ] **Step 2: Run the consumer HTTP tests**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet test tests/EShop.ContractTests/EShop.ContractTests.csproj \
  --filter "FullyQualifiedName~ConsumerAuditApiClientTests" \
  --logger "console;verbosity=normal"
```

Expected: 2 tests pass. Pact file written:
```bash
ls tests/EShop.ContractTests/bin/Debug/net10.0/pacts/AuditApiClient-AuditService.json
```

- [ ] **Step 3: Commit**

```bash
git add tests/EShop.ContractTests/ConsumerAuditApiClientTests.cs
git commit -m "test(contracts): add AuditApiClient HTTP consumer pact for audit endpoints"
```

---

## Task 3: Provider infrastructure (AuditApiFactory) + Provider HTTP test

**Files:**
- Create: `tests/EShop.ContractTests/Infrastructure/AuditApiFactory.cs`
- Create: `tests/EShop.ContractTests/ProviderAuditServiceTests.cs`

**Interfaces:**
- Consumes: pact file `AuditApiClient-AuditService.json` (generated in Task 2)
- Consumes: `Audit.Service.ProgramMarker` (marker class for `WebApplicationFactory<T>`)
- Consumes: `Audit.Service.Infrastructure.AuditDbContext`
- Consumes: `Audit.Service.Domain.AuditEntry`, `Audit.Service.Domain.AuditEventType`
- Consumes: `EShop.Contracts.PaymentStatus`
- Consumes: `ProviderStateRequest` (already declared in `Infrastructure/ProviderStateMiddleware.cs` as `internal sealed record ProviderStateRequest(string? State, string? Action)`)

- [ ] **Step 1: Create AuditApiFactory.cs**

Create `tests/EShop.ContractTests/Infrastructure/AuditApiFactory.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Audit.Service.Domain;
using Audit.Service.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace EShop.ContractTests.Infrastructure;

/// <summary>
/// Wraps <see cref="AuditApiFactory"/> with its Testcontainers Postgres.
/// Create once per test class, dispose when done.
/// </summary>
public sealed class AuditApiFixture : IAsyncDisposable
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithDatabase("audit")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private AuditApiFactory _factory = null!;

    public Uri ServerAddress => _factory.ServerAddress;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _factory = new AuditApiFactory(
            connectionString: $"{_pg.GetConnectionString()};Include Error Detail=true");
        // Accessing ServerAddress triggers lazy host construction (migrations run here).
        _ = _factory.ServerAddress;
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _pg.DisposeAsync();
    }
}

/// <summary>
/// Hosts Audit.Service on a real Kestrel TCP port so the Pact verifier can reach it
/// over HTTP. Kafka consumer background services are given a non-existent bootstrap
/// address — they fail silently and do not crash the host.
/// </summary>
internal sealed class AuditApiFactory : WebApplicationFactory<Audit.Service.ProgramMarker>
{
    private readonly string _connectionString;
    private readonly int _port;
    private IHost? _kestrelHost;

    internal AuditApiFactory(string connectionString)
    {
        _connectionString = connectionString;
        _port = GetFreePort();
    }

    public Uri ServerAddress
    {
        get
        {
            _ = Services; // triggers lazy WebApplicationFactory host construction
            return new Uri($"http://localhost:{_port}/");
        }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTest");

        builder.ConfigureServices(services =>
        {
            // ── Swap real DB for Testcontainers Postgres ──────────────────────
            var descriptor = services.FirstOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AuditDbContext>));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddDbContext<AuditDbContext>(opts =>
                opts.UseNpgsql(
                    _connectionString,
                    npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "audit")));

            // ── Point Kafka consumers at a non-existent broker ────────────────
            // There is no Kafka broker in the contract test environment.
            // Consumers will log connection errors and retry, but will not crash
            // the host. They stop cleanly when the host is disposed.
            services.PostConfigure<EShop.Messaging.Kafka.KafkaOptions>(opts =>
            {
                opts.BootstrapServers = "localhost:1"; // nothing listens here
                opts.ConsumerGroupId  = "audit-contract-test";
            });

            // ── Inject provider-state middleware ──────────────────────────────
            services.AddTransient<IStartupFilter, AuditProviderStateStartupFilter>();
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Host 1: TestServer (in-process) — needed by WebApplicationFactory internals
        var testHost = builder.Build();

        // Host 2: Real Kestrel — Pact verifier sends genuine HTTP traffic
        builder.ConfigureWebHost(wb =>
        {
            wb.UseKestrel(opts => opts.Listen(IPAddress.Loopback, _port));
        });

        _kestrelHost = builder.Build();
        _kestrelHost.Start();

        return testHost;
    }

    public override async ValueTask DisposeAsync()
    {
        if (_kestrelHost is not null)
        {
            await _kestrelHost.StopAsync();
            _kestrelHost.Dispose();
        }

        await base.DisposeAsync();
    }
}

// ── Provider-state middleware ────────────────────────────────────────────────

internal sealed class AuditProviderStateStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            app.UseMiddleware<AuditProviderStateMiddleware>();
            next(app);
        };
}

internal sealed class AuditProviderStateMiddleware(RequestDelegate next)
{
    // Fixed order ID seeded for "an order with audit entries exists" state.
    // Must match SeedOrderId in ConsumerAuditApiClientTests.
    private static readonly Guid SeedOrderId = new("22222222-2222-2222-2222-222222222222");

    // Separate order ID used only for the payment summary state.
    private static readonly Guid SummaryOrderId = new("33333333-3333-3333-3333-333333333333");

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Method == HttpMethods.Post &&
            context.Request.Path == "/provider-states")
        {
            await HandleAsync(context);
            return;
        }

        await next(context);
    }

    private static async Task HandleAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        // ProviderStateRequest is declared in ProviderStateMiddleware.cs (same namespace)
        var req = JsonSerializer.Deserialize<ProviderStateRequest>(body, JsonOpts);

        if (req?.State is not null)
        {
            using var scope = context.RequestServices.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

            switch (req.State)
            {
                case "an order with audit entries exists":
                    await SeedOrderAuditEntriesAsync(db);
                    break;

                case "audit entries exist for payment summary":
                    await SeedPaymentSummaryEntriesAsync(db);
                    break;
            }
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
    }

    private static async Task SeedOrderAuditEntriesAsync(AuditDbContext db)
    {
        if (await db.AuditEntries.AnyAsync(e => e.OrderId == SeedOrderId))
            return;

        db.AuditEntries.Add(new AuditEntry
        {
            Id            = Guid.NewGuid(),
            EventId       = Guid.NewGuid(),
            EventType     = AuditEventType.OrderCreated,
            OrderId       = SeedOrderId,
            Amount        = 19.98m,
            PaymentStatus = null,
            FailureReason = null,
            OccurredAt    = DateTimeOffset.UtcNow.AddMinutes(-2),
            RecordedAt    = DateTimeOffset.UtcNow.AddMinutes(-2),
        });

        await db.SaveChangesAsync();
    }

    private static async Task SeedPaymentSummaryEntriesAsync(AuditDbContext db)
    {
        if (await db.AuditEntries.AnyAsync(e => e.OrderId == SummaryOrderId))
            return;

        db.AuditEntries.Add(new AuditEntry
        {
            Id            = Guid.NewGuid(),
            EventId       = Guid.NewGuid(),
            EventType     = AuditEventType.PaymentProcessed,
            OrderId       = SummaryOrderId,
            Amount        = 19.98m,
            PaymentStatus = EShop.Contracts.PaymentStatus.Succeeded,
            FailureReason = null,
            OccurredAt    = DateTimeOffset.UtcNow,
            RecordedAt    = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 2: Create ProviderAuditServiceTests.cs**

Create `tests/EShop.ContractTests/ProviderAuditServiceTests.cs`:

```csharp
using EShop.ContractTests.Infrastructure;
using PactNet;
using PactNet.Verifier;

namespace EShop.ContractTests;

[TestFixture]
public sealed class ProviderAuditServiceTests
{
    private AuditApiFixture _auditApi = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        _auditApi = new AuditApiFixture();
        await _auditApi.InitializeAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync() => await _auditApi.DisposeAsync();

    [Test]
    public void VerifyProvider()
    {
        var pactFile = new FileInfo(
            Path.Combine(
                Path.GetDirectoryName(typeof(ProviderAuditServiceTests).Assembly.Location)!,
                "pacts",
                "AuditApiClient-AuditService.json"));

        new PactVerifier("AuditService", new PactVerifierConfig { LogLevel = PactLogLevel.Debug })
            .WithHttpEndpoint(_auditApi.ServerAddress)
            .WithFileSource(pactFile)
            .WithProviderStateUrl(new Uri(_auditApi.ServerAddress, "provider-states"))
            .Verify();
    }
}
```

- [ ] **Step 3: Build to verify compilation**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build tests/EShop.ContractTests/EShop.ContractTests.csproj
```

Expected: `Build succeeded.` with no errors.

- [ ] **Step 4: Run all consumer tests first to ensure pact files exist**

The provider verifier reads pact files from the build output directory. Because tests rebuild the output directory, run ALL consumer tests before the provider test:

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet test tests/EShop.ContractTests/EShop.ContractTests.csproj \
  --filter "FullyQualifiedName~ConsumerAuditApiClientTests" \
  --logger "console;verbosity=normal"
```

Confirm pact file exists:
```bash
ls tests/EShop.ContractTests/bin/Debug/net10.0/pacts/AuditApiClient-AuditService.json
```

- [ ] **Step 5: Run the provider test**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet test tests/EShop.ContractTests/EShop.ContractTests.csproj \
  --filter "FullyQualifiedName~ProviderAuditServiceTests" \
  --logger "console;verbosity=normal"
```

Expected: 1 test passes. If the test fails with a pact mismatch, read the PactNet debug output. Common issues:
- Provider state string mismatch: state name in middleware must be exactly `"an order with audit entries exists"` and `"audit entries exist for payment summary"`
- Response shape mismatch: check that `AuditEndpoints.cs` serialises field names in camelCase

- [ ] **Step 6: Run full contract test suite**

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet test tests/EShop.ContractTests/EShop.ContractTests.csproj \
  --logger "console;verbosity=normal"
```

Expected: all tests pass (consumer tests generate pacts, then provider tests verify them). The total test count should include the 3 new consumer message tests, 2 new consumer HTTP tests, and 1 new provider test.

- [ ] **Step 7: Commit**

```bash
git add tests/EShop.ContractTests/Infrastructure/AuditApiFactory.cs \
        tests/EShop.ContractTests/ProviderAuditServiceTests.cs
git commit -m "test(contracts): add AuditService HTTP provider verification with Kestrel factory and provider-state seeding"
```
