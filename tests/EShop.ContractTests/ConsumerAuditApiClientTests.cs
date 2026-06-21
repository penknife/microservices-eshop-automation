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
            Assert.That(entries,              Is.Not.Null);
            Assert.That(entries!,             Is.Not.Empty);
            Assert.That(entries[0].EventType, Is.Not.Empty);
            Assert.That(entries[0].Amount,    Is.GreaterThan(0));
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
            Assert.That(summaries,       Is.Not.Null);
            Assert.That(summaries!,      Is.Not.Empty);
            Assert.That(summaries[0].Date, Is.Not.Empty);
        });
    }
}
