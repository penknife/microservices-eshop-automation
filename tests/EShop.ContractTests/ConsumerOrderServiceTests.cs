using System.Net.Http.Json;
using System.Text.Json.Serialization;
using PactNet;
using PactNet.Matchers;

namespace EShop.ContractTests;

public sealed record OrderItemResponse(
    [property: JsonPropertyName("sku")] string Sku,
    [property: JsonPropertyName("quantity")] int Quantity,
    [property: JsonPropertyName("price")] decimal Price);

public sealed record OrderResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("customerId")] string CustomerId,
    [property: JsonPropertyName("totalAmount")] decimal TotalAmount,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("items")] IReadOnlyList<OrderItemResponse> Items);

public sealed class ConsumerOrderServiceTests
{
    private IPactBuilderV4 _pact = null!;
    private readonly int _port = 9001;

    [SetUp]
    public void Setup()
    {
         var config = new PactConfig
        {
            PactDir = Path.Combine(Directory.GetCurrentDirectory(), "pacts"),
            LogLevel = PactLogLevel.Debug
        };

        _pact = Pact.V4("OrdersClient", "OrdersApi", config)
                    .WithHttpInteractions(_port);
    }

     [Test]
     public async Task CreateOrder_ShouldSucceedAsync()
     {
        _pact
            .UponReceiving("create new order")
            .Given("order creation is allowed")
            .WithRequest(HttpMethod.Post, "/orders")
            .WithHeader("Content-Type", "application/json")
            .WithJsonBody(new
            {
                customerId = "customer-123",
                items = new[]
                {
                    new { sku = "SKU-001", quantity = 2, price = 9.99m }
                }
            })
            .WillRespond()
            .WithStatus(System.Net.HttpStatusCode.Created)
            .WithHeader("Content-Type", "application/json; charset=utf-8")
            .WithJsonBody(new
            {
                id          = Match.Type(Guid.NewGuid()),
                customerId  = Match.Type("customer-123"),
                totalAmount = Match.Decimal(19.98m),
                createdAt   = Match.Type(DateTimeOffset.UtcNow),
                items       = Match.MinType(new
                {
                    sku      = Match.Type("SKU-001"),
                    quantity = Match.Integer(2),
                    price    = Match.Decimal(9.99m)
                }, 1)
            });

        await _pact.VerifyAsync(async ctx =>
        {
            using var client = new HttpClient { BaseAddress = ctx.MockServerUri };
            var response = await client.PostAsJsonAsync("/orders", new
            {
                customerId = "customer-123",
                items = new[] { new { sku = "SKU-001", quantity = 2, price = 9.99m } }
            });

            var order = await response.Content.ReadFromJsonAsync<OrderResponse>();

            Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.Created));
            Assert.That(order, Is.Not.Null);
            Assert.That(order!.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(order.CustomerId, Is.EqualTo("customer-123"));
            Assert.That(order.TotalAmount, Is.GreaterThan(0));
            Assert.That(order.Items, Is.Not.Empty);
            Assert.That(order.Items[0].Sku, Is.EqualTo("SKU-001"));
            Assert.That(order.Items[0].Quantity, Is.EqualTo(2));
            Assert.That(order.Items[0].Price, Is.EqualTo(9.99m));
        });
    }  
}


    