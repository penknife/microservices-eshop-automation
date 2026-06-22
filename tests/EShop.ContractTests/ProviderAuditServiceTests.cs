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
