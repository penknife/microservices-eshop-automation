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
