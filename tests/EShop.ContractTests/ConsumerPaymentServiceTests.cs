using PactNet;
using PactNet.Matchers;
using System.Text.Json;

namespace EShop.ContractTests;

[TestFixture]
public sealed class ConsumerPaymentServiceTests
{
    private IMessagePactBuilderV4 _pact = null!;

    [SetUp]
    public void Setup()
    {
        var config = new PactConfig
        {
            PactDir = Path.Combine(Path.GetDirectoryName(typeof(ConsumerPaymentServiceTests).Assembly.Location)!, "pacts"),
            LogLevel = PactLogLevel.Debug
        };

        _pact = Pact.V4("PaymentService", "OrderService", config).WithMessageInteractions();
    }

    [Test]
    public async Task OrderCreatedEvent_ShouldBeConsumedByPaymentServiceAsync()
    {
        await _pact
            .ExpectsToReceive("an order created event")
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
            // PactNet 5 deserialises the example with default (case-sensitive) options, so we
            // use JsonElement here and access properties by their actual camelCase wire names.
            .VerifyAsync<JsonElement>(message =>
            {
                Assert.That(message.GetProperty("eventId").GetGuid(),  Is.Not.EqualTo(Guid.Empty));
                Assert.That(message.GetProperty("orderId").GetGuid(),  Is.Not.EqualTo(Guid.Empty));
                Assert.That(message.GetProperty("customerId").GetString(), Is.Not.Empty);
                Assert.That(message.GetProperty("totalAmount").GetDecimal(), Is.GreaterThan(0));

                var items = message.GetProperty("items");
                Assert.That(items.GetArrayLength(), Is.GreaterThan(0));

                var first = items[0];
                Assert.That(first.GetProperty("sku").GetString(),           Is.Not.Empty);
                Assert.That(first.GetProperty("quantity").GetInt32(),       Is.GreaterThan(0));
                Assert.That(first.GetProperty("price").GetDecimal(),        Is.GreaterThan(0));
                return Task.CompletedTask;
            });
    }
}
