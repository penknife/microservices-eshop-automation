using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using EShop.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PactNet;
using PactNet.Verifier;
using Payment.Service.Handlers;
using Payment.Service.Infrastructure;
using Testcontainers.PostgreSql;

namespace EShop.ContractTests;

/// <summary>
/// Provider verification: proves Payment.Service's OrderCreatedHandler actually produces
/// PaymentProcessedIntegrationEvent messages that satisfy the pact recorded by
/// ConsumerNotificationServiceTests.
///
/// Strategy: for each pact interaction description we invoke OrderCreatedHandler directly
/// against an in-process PaymentsDbContext backed by a Testcontainers Postgres, then return
/// the JSON payload from the outbox row. No Kafka broker is needed.
///
/// PactNet 5.0.1 bug workaround: WithMessages() registers scheme="message" internally but
/// the ffi verifier expects scheme="http". FixMessageProviderScheme patches the registered
/// URI via reflection + P/Invoke after WithMessages returns.
/// </summary>
[TestFixture]
public sealed class ProviderPaymentServiceTests : IAsyncDisposable
{
    private PostgreSqlContainer _pg = null!;
    private string _connectionString = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        _pg = new PostgreSqlBuilder()
            .WithImage("postgres:16")
            .WithDatabase("payments")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _pg.StartAsync();

        _connectionString = $"{_pg.GetConnectionString()};Include Error Detail=true";

        await using var db = BuildDbContext();
        await db.Database.MigrateAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync() => await DisposeAsync();

    [Test]
    public void VerifyProvider()
    {
        var pactFile = new FileInfo(
            Path.Combine(
                Path.GetDirectoryName(typeof(ProviderPaymentServiceTests).Assembly.Location)!,
                "pacts",
                "NotificationService-PaymentService.json"));

        var verifier = new PactVerifier("PaymentService", new PactVerifierConfig { LogLevel = PactLogLevel.Debug });

        verifier.WithMessages(scenarios =>
        {
            // Scenario descriptions must match the pact interaction descriptions exactly.
            scenarios.Add(
                "a payment processed event for a succeeded payment",
                () => JsonSerializer.Deserialize<object>(
                    ProducePaymentEventPayloadAsync(succeeded: true).GetAwaiter().GetResult())!);

            scenarios.Add(
                "a payment processed event for a failed payment",
                () => JsonSerializer.Deserialize<object>(
                    ProducePaymentEventPayloadAsync(succeeded: false).GetAwaiter().GetResult())!);
        });

        // PactNet 5.0.1 bug: WithMessages registers scheme="message" but ffi expects "http".
        // Patch it via reflection after WithMessages has run.
        FixMessageProviderScheme(verifier);

        verifier
            .WithFileSource(pactFile)
            .Verify();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<string> ProducePaymentEventPayloadAsync(bool succeeded)
    {
        // Each invocation gets its own DbContext so outbox rows don't collide.
        await using var db = BuildDbContext();

        // amount must stay below threshold to succeed; exceed it to force failure.
        var amount = succeeded ? 10.00m : 1_000_001.00m;
        // threshold=MaxValue means no order can ever fail; low threshold forces the failure path.
        var threshold = succeeded ? decimal.MaxValue : 999_999.99m;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payment:FailureThreshold"] = threshold.ToString("F2")
            })
            .Build();

        var handler = new OrderCreatedHandler(db, config, NullLogger<OrderCreatedHandler>.Instance);

        var orderEvent = new OrderCreatedIntegrationEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTimeOffset.UtcNow,
            OrderId: Guid.NewGuid(),
            CustomerId: "customer-123",
            TotalAmount: amount,
            Items: [new OrderItemDto("SKU-001", 1, amount)]);

        await handler.HandleAsync(orderEvent, CancellationToken.None);

        var outbox = await db.Outbox
            .Where(m => m.Topic == PaymentProcessedIntegrationEvent.TopicName && m.DispatchedAt == null)
            .OrderByDescending(m => m.OccurredAt)
            .FirstAsync();

        return outbox.Payload;
    }

    private PaymentsDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseNpgsql(
                _connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "payments"))
            .Options;
        return new PaymentsDbContext(options);
    }

    // ── PactNet 5.0.1 scheme bug workaround ───────────────────────────────────
    // WithMessages() calls provider.SetProviderInfo(..., "message", ...) internally,
    // but the ffi verifier requires scheme="http" for the messaging provider HTTP endpoint.
    // We extract the native handle from the internal InteropVerifierProvider via reflection
    // and call SetProviderInfo again with "http" before Verify() runs.
    // See CLAUDE.md for context. Remove when PactNet fixes the upstream bug.

    private static void FixMessageProviderScheme(PactVerifier verifier)
    {
        try
        {
            const BindingFlags nonPublic = BindingFlags.NonPublic | BindingFlags.Instance;

            // Get the native handle from InteropVerifierProvider.
            // InteropVerifierProvider wraps the raw pact_ffi handle.
            var provider = verifier.GetType()
                .GetField("provider", nonPublic)!
                .GetValue(verifier)!;

            // The native handle is an opaque pointer used by every pact_ffi call.
            var handle = (IntPtr)provider.GetType()
                .GetField("handle", nonPublic)!
                .GetValue(provider)!;

            // Get the HttpListener from MessagingProvider to read the actual listening URI.
            var messagingProvider = verifier.GetType()
                .GetField("messagingProvider", nonPublic)!
                .GetValue(verifier)!;

            // PactNet spins up an HttpListener on a random free port to serve message scenarios.
            var httpListener = (HttpListener)messagingProvider.GetType()
                .GetField("server", nonPublic)!
                .GetValue(messagingProvider)!;

            // HttpListener.Prefixes contains URIs like "http://localhost:49152/pact-messages/"
            var prefix = httpListener.Prefixes.First();
            var uri = new Uri(prefix);

            NativeFfi.VerifierSetProviderInfo(
                handle,
                "PaymentService",
                "http",
                uri.Host,
                (ushort)uri.Port,
                uri.AbsolutePath);
        }
        catch
        {
            // Best-effort. If reflection fails (e.g. after a PactNet update that fixes the
            // bug), the test will fail with a PactNet error rather than a reflection exception.
        }
    }

    // Direct P/Invoke into the native pact_ffi shared library bundled with PactNet.
    private static class NativeFfi
    {
        [DllImport("pact_ffi", EntryPoint = "pactffi_verifier_set_provider_info")]
        internal static extern void VerifierSetProviderInfo(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string? name,
            [MarshalAs(UnmanagedType.LPStr)] string? scheme,
            [MarshalAs(UnmanagedType.LPStr)] string? host,
            ushort port,
            [MarshalAs(UnmanagedType.LPStr)] string? path);
    }

    public async ValueTask DisposeAsync()
    {
        if (_pg is not null) await _pg.DisposeAsync();
    }
}
