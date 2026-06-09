using EShop.Contracts;
using EShop.Messaging.Kafka;
using Microsoft.EntityFrameworkCore;
using Payment.Service.Handlers;
using Payment.Service.Infrastructure;
using Serilog;
using Serilog.Formatting.Compact;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new RenderedCompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(new RenderedCompactJsonFormatter()));

    var connectionString = builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured");

    builder.Services.AddDbContext<PaymentsDbContext>(opts =>
        opts.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "payments")));

    builder.Services.AddKafkaMessaging(builder.Configuration);
    builder.Services.AddKafkaConsumer<OrderCreatedIntegrationEvent, OrderCreatedHandler>(
        OrderCreatedIntegrationEvent.TopicName);
    builder.Services.AddOutboxDispatcher<PaymentsDbContext>();

    var kafkaBootstrap = builder.Configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
    builder.Services.AddHealthChecks()
        .AddNpgSql(connectionString, name: "postgres")
        .AddKafka(producerConfig => producerConfig.BootstrapServers = kafkaBootstrap, name: "kafka");

    var app = builder.Build();

    app.UseSerilogRequestLogging();
    app.MapHealthChecks("/health");
    app.MapGet("/", () => Results.Ok(new { service = "payment-service", status = "ok" }));

    if (builder.Configuration.GetValue("Database:AutoMigrate", true))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        await db.Database.MigrateAsync();
    }

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Payment.Service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;

