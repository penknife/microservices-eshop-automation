using EShop.IntegrationTests.Fixtures;

namespace EShop.IntegrationTests
{
    /// <summary>
    /// NUnit equivalent of xUnit's ICollectionFixture.
    /// [SetUpFixture] scoped to the root namespace runs [OneTimeSetUp]/[OneTimeTearDown]
    /// exactly once for the entire assembly, sharing a single EShopFixture instance.
    /// </summary>
    [SetUpFixture]
    public class AssemblySetup
    {
        public static EShopFixture Fixture { get; private set; } = null!;

        [OneTimeSetUp]
        public async Task SetUpAsync()
        {
            Fixture = new EShopFixture();
            await Fixture.InitializeAsync();
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            await Fixture.DisposeAsync();
        }
    }
}
