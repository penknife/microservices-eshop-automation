using EShop.Contracts;
using EShop.Messaging.Kafka;
using Microsoft.EntityFrameworkCore;
using Order.Api.Endpoints;
using Order.Api.Infrastructure;
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

    builder.Services.AddOpenApi();

    var connectionString = builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured");

    builder.Services.AddDbContext<OrdersDbContext>(opts =>
        opts.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "orders")));

    builder.Services.AddKafkaMessaging(builder.Configuration);
    builder.Services.AddOutboxDispatcher<OrdersDbContext>();

    var kafkaBootstrap = builder.Configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
    builder.Services.AddHealthChecks()
        .AddNpgSql(connectionString, name: "postgres")
        .AddKafka(producerConfig => producerConfig.BootstrapServers = kafkaBootstrap, name: "kafka");

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseSerilogRequestLogging();

    app.MapOrdersEndpoints();
    app.MapHealthChecks("/health");
    app.MapGet("/", () => Results.Ok(new { service = "order-api", status = "ok" }));

    // Apply pending migrations on startup (dev convenience; toggle via Database:AutoMigrate).
    if (builder.Configuration.GetValue("Database:AutoMigrate", true))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await db.Database.MigrateAsync();
    }

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Order.Api terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;

