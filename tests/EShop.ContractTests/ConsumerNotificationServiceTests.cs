using PactNet;
using PactNet.Matchers;
using System.Text.Json;

namespace EShop.ContractTests;

[TestFixture]
public sealed class ConsumerNotificationServiceTests
{
    private IMessagePactBuilderV4 _pact = null!;

    [SetUp]
    public void Setup()
    {
        var config = new PactConfig
        {
            PactDir = Path.Combine(Path.GetDirectoryName(typeof(ConsumerNotificationServiceTests).Assembly.Location)!, "pacts"),
            LogLevel = PactLogLevel.Debug
        };

        _pact = Pact.V4("NotificationService", "PaymentService", config).WithMessageInteractions();
    }

    [Test]
    public async Task PaymentProcessedEvent_WithSucceededStatus_ShouldBeConsumedByNotificationServiceAsync()
    {
        await _pact
            .ExpectsToReceive("a payment processed event for a succeeded payment")
            .Given("a payment has succeeded")
            .WithJsonContent(new
            {
                eventId       = Match.Type(Guid.NewGuid()),
                occurredAt    = Match.Type(DateTimeOffset.UtcNow),
                orderId       = Match.Type(Guid.NewGuid()),
                paymentId     = Match.Type(Guid.NewGuid()),
                amount        = Match.Decimal(19.98m),
                status        = Match.Integer(1),   // PaymentStatus.Succeeded serialises as 1
                failureReason = (string?)null
            })
            // PactNet 5 deserialises with default (case-sensitive) options; use JsonElement
            // to access properties by their actual camelCase wire names without a type mismatch.
            .VerifyAsync<JsonElement>(message =>
            {
                Assert.That(message.GetProperty("eventId").GetGuid(),   Is.Not.EqualTo(Guid.Empty));
                Assert.That(message.GetProperty("orderId").GetGuid(),   Is.Not.EqualTo(Guid.Empty));
                Assert.That(message.GetProperty("paymentId").GetGuid(), Is.Not.EqualTo(Guid.Empty));
                Assert.That(message.GetProperty("amount").GetDecimal(), Is.GreaterThan(0));
                Assert.That(message.GetProperty("status").GetInt32(),   Is.EqualTo(1)); // Succeeded
                Assert.That(message.GetProperty("failureReason").ValueKind, Is.EqualTo(JsonValueKind.Null));
                return Task.CompletedTask;
            });
    }

    [Test]
    public async Task PaymentProcessedEvent_WithFailedStatus_ShouldBeConsumedByNotificationServiceAsync()
    {
        await _pact
            .ExpectsToReceive("a payment processed event for a failed payment")
            .Given("a payment has failed")
            .WithJsonContent(new
            {
                eventId       = Match.Type(Guid.NewGuid()),
                occurredAt    = Match.Type(DateTimeOffset.UtcNow),
                orderId       = Match.Type(Guid.NewGuid()),
                paymentId     = Match.Type(Guid.NewGuid()),
                amount        = Match.Decimal(19.98m),
                status        = Match.Integer(2),   // PaymentStatus.Failed serialises as 2
                failureReason = Match.Type("Amount exceeds configured threshold")
            })
            .VerifyAsync<JsonElement>(message =>
            {
                Assert.That(message.GetProperty("eventId").GetGuid(),   Is.Not.EqualTo(Guid.Empty));
                Assert.That(message.GetProperty("orderId").GetGuid(),   Is.Not.EqualTo(Guid.Empty));
                Assert.That(message.GetProperty("paymentId").GetGuid(), Is.Not.EqualTo(Guid.Empty));
                Assert.That(message.GetProperty("amount").GetDecimal(), Is.GreaterThan(0));
                Assert.That(message.GetProperty("status").GetInt32(),   Is.EqualTo(2)); // Failed
                Assert.That(message.GetProperty("failureReason").GetString(), Is.Not.Null.And.Not.Empty);
                return Task.CompletedTask;
            });
    }
}
