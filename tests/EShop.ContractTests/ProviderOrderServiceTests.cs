using EShop.ContractTests.Infrastructure;
using PactNet;
using PactNet.Verifier;

namespace EShop.ContractTests;

[TestFixture]
public sealed class ProviderOrderServiceTests
{
    private OrderApiFixture _orderApi = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        _orderApi = new OrderApiFixture();
        await _orderApi.InitializeAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync() => await _orderApi.DisposeAsync();

    [Test]
    public void VerifyProvider()
    {
        var pactFile = new FileInfo(
            Path.Combine(Path.GetDirectoryName(typeof(ProviderOrderServiceTests).Assembly.Location)!, "pacts", "OrdersClient-OrdersApi.json"));

        new PactVerifier("OrdersApi", new PactVerifierConfig { LogLevel = PactLogLevel.Debug })
            .WithHttpEndpoint(_orderApi.ServerAddress)
            .WithFileSource(pactFile)
            .WithProviderStateUrl(new Uri(_orderApi.ServerAddress, "provider-states"))
            .Verify();
    }
}
