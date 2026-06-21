// tests/EShop.IntegrationTests/Fixtures/EShopFixture.cs
using Audit.Service.Infrastructure;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;

namespace EShop.IntegrationTests.Fixtures
{
    public sealed class EShopFixture
    {
        // Kafka
        private readonly KafkaContainer _kafka = new KafkaBuilder()
            .WithImage("confluentinc/cp-kafka:7.6.1")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(9093))
            .Build();

        // Postgres
        private readonly PostgreSqlContainer _pgOrders = new PostgreSqlBuilder()
            .WithImage("postgres:16").WithDatabase("orders")
            .WithUsername("postgres").WithPassword("postgres").Build();

        private readonly PostgreSqlContainer _pgPayments = new PostgreSqlBuilder()
            .WithImage("postgres:16").WithDatabase("payments")
            .WithUsername("postgres").WithPassword("postgres").Build();

        private readonly PostgreSqlContainer _pgNotifications = new PostgreSqlBuilder()
            .WithImage("postgres:16").WithDatabase("notifications")
            .WithUsername("postgres").WithPassword("postgres").Build();

        private readonly PostgreSqlContainer _pgAudit = new PostgreSqlBuilder()
            .WithImage("postgres:16").WithDatabase("audit")
            .WithUsername("postgres").WithPassword("postgres").Build();

        // WebApplicationFactories
        public WebApplicationFactory<Order.Api.ProgramMarker> OrderFactory { get; private set; } = default!;
        public WebApplicationFactory<Payment.Service.ProgramMarker> PaymentFactory { get; private set; } = default!;
        public WebApplicationFactory<Notification.Service.ProgramMarker> NotificationFactory { get; private set; } = default!;
        public WebApplicationFactory<Audit.Service.ProgramMarker> AuditFactory { get; private set; } = default!;

        public HttpClient OrderClient { get; private set; } = default!;

        public async Task InitializeAsync()
        {
            await Task.WhenAll(
                _kafka.StartAsync(),
                _pgOrders.StartAsync(),
                _pgPayments.StartAsync(),
                _pgNotifications.StartAsync(),
                _pgAudit.StartAsync());

            var kafkaBootstrap = _kafka.GetBootstrapAddress();

            OrderFactory = new WebApplicationFactory<Order.Api.ProgramMarker>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("IntegrationTest");
                    builder.ConfigureServices(services =>
                    {
                        ReplaceDbContext<Order.Api.Infrastructure.OrdersDbContext>(services,
                            $"{_pgOrders.GetConnectionString()};Include Error Detail=true", "orders");
                        services.Configure<EShop.Messaging.Kafka.KafkaOptions>(opts =>
                        {
                            opts.BootstrapServers = kafkaBootstrap;
                            opts.ConsumerGroupId = "order-service-test";
                            opts.OutboxPollingIntervalSeconds = 1;
                        });
                    });
                });

            PaymentFactory = new WebApplicationFactory<Payment.Service.ProgramMarker>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("IntegrationTest");
                    builder.ConfigureServices(services =>
                    {
                        ReplaceDbContext<Payment.Service.Infrastructure.PaymentsDbContext>(services,
                            $"{_pgPayments.GetConnectionString()};Include Error Detail=true", "payments");
                        services.Configure<EShop.Messaging.Kafka.KafkaOptions>(opts =>
                        {
                            opts.BootstrapServers = kafkaBootstrap;
                            opts.ConsumerGroupId = "payment-service-test";
                            opts.OutboxPollingIntervalSeconds = 1;
                        });
                    });
                });

            NotificationFactory = new WebApplicationFactory<Notification.Service.ProgramMarker>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("IntegrationTest");
                    builder.ConfigureServices(services =>
                    {
                        ReplaceDbContext<Notification.Service.Infrastructure.NotificationsDbContext>(services,
                            $"{_pgNotifications.GetConnectionString()};Include Error Detail=true", "notifications");
                        services.Configure<EShop.Messaging.Kafka.KafkaOptions>(opts =>
                        {
                            opts.BootstrapServers = kafkaBootstrap;
                            opts.ConsumerGroupId = "notification-service-test";
                            opts.OutboxPollingIntervalSeconds = 1;
                        });
                    });
                });

            AuditFactory = new WebApplicationFactory<Audit.Service.ProgramMarker>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("IntegrationTest");
                    builder.ConfigureServices(services =>
                    {
                        ReplaceDbContext<AuditDbContext>(services,
                            $"{_pgAudit.GetConnectionString()};Include Error Detail=true", "audit");
                        services.Configure<EShop.Messaging.Kafka.KafkaOptions>(opts =>
                        {
                            opts.BootstrapServers = kafkaBootstrap;
                            opts.ConsumerGroupId = "audit-service-test";
                            opts.OutboxPollingIntervalSeconds = 1;
                        });
                    });
                });

            OrderClient = OrderFactory.CreateClient();
            _ = PaymentFactory.CreateClient();
            _ = NotificationFactory.CreateClient();
            _ = AuditFactory.CreateClient();

            await Task.Delay(TimeSpan.FromSeconds(5));
        }

        public async Task DisposeAsync()
        {
            await OrderFactory.DisposeAsync();
            await PaymentFactory.DisposeAsync();
            await NotificationFactory.DisposeAsync();
            await AuditFactory.DisposeAsync();

            await _kafka.DisposeAsync();
            await _pgOrders.DisposeAsync();
            await _pgPayments.DisposeAsync();
            await _pgNotifications.DisposeAsync();
            await _pgAudit.DisposeAsync();
        }

        public async Task<T> UseNotificationsDbAsync<T>(
            Func<Notification.Service.Infrastructure.NotificationsDbContext, Task<T>> action)
        {
            using var scope = NotificationFactory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Notification.Service.Infrastructure.NotificationsDbContext>();
            return await action(db);
        }

        public async Task<T> UsePaymentsDbAsync<T>(
            Func<Payment.Service.Infrastructure.PaymentsDbContext, Task<T>> action)
        {
            using var scope = PaymentFactory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Payment.Service.Infrastructure.PaymentsDbContext>();
            return await action(db);
        }

        public async Task<T> UseAuditDbAsync<T>(Func<AuditDbContext, Task<T>> action)
        {
            using var scope = AuditFactory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            return await action(db);
        }

        private static void ReplaceDbContext<TDbContext>(
            IServiceCollection services, string connectionString, string schema)
            where TDbContext : DbContext
        {
            var descriptor = services.FirstOrDefault(d =>
                d.ServiceType == typeof(DbContextOptions<TDbContext>));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddDbContext<TDbContext>(opts =>
                opts.UseNpgsql(connectionString,
                    npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", schema)));
        }
    }
}
