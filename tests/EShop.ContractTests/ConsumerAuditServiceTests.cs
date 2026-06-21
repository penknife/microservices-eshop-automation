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
