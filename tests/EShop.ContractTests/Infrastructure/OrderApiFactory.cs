using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Order.Api.Infrastructure;
using Testcontainers.PostgreSql;

namespace EShop.ContractTests.Infrastructure;

/// <summary>
/// Wraps <see cref="OrderApiFactory"/> together with its Testcontainers
/// (Postgres + Kafka). Create once, dispose when done.
/// </summary>
public sealed class OrderApiFixture : IAsyncDisposable
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithDatabase("orders")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private OrderApiFactory _factory = null!;

    public Uri ServerAddress => _factory.ServerAddress;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();

        _factory = new OrderApiFactory(
            connectionString: $"{_pg.GetConnectionString()};Include Error Detail=true");

        // Trigger host start + EF migrations (AutoMigrate=true by default).
        _ = _factory.ServerAddress;
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _pg.DisposeAsync();
    }
}

/// <summary>
/// Hosts Order.Api on a real Kestrel port so the Pact verifier can reach it
/// over HTTP. Provider-state seeding is injected as a startup filter.
/// </summary>
internal sealed class OrderApiFactory : WebApplicationFactory<Order.Api.ProgramMarker>
{
    private readonly string _connectionString;
    private int _port;
    private IHost? _kestrelHost;

    internal OrderApiFactory(string connectionString)
    {
        _connectionString = connectionString;
        _port = GetFreePort();
    }

    public Uri ServerAddress
    {
        get
        {
            // Accessing Services triggers host creation.
            _ = Services;
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
            var descriptor = services.FirstOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<OrdersDbContext>));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddDbContext<OrdersDbContext>(opts =>
                opts.UseNpgsql(
                    _connectionString,
                    npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "orders")));

            // Remove the outbox dispatcher — it connects to Kafka which is not
            // needed for contract tests (HTTP-only verification).
            var outboxDescriptor = services.FirstOrDefault(
                d => d.ImplementationType == typeof(EShop.Messaging.Kafka.OutboxDispatcherBackgroundService<OrdersDbContext>));
            if (outboxDescriptor is not null)
                services.Remove(outboxDescriptor);

            services.AddTransient<IStartupFilter, ProviderStateStartupFilter>();
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Build the TestServer-based host that WebApplicationFactory needs
        // internally (for Services, CreateClient, etc.).
        var testHost = builder.Build();

        // Build a second host with real Kestrel so the Pact verifier can reach
        // the API over HTTP.
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
