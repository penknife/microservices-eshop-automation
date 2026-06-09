using EShop.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EShop.Messaging.Kafka;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="KafkaOptions"/> from configuration (section: "Kafka").
    /// Call <see cref="AddKafkaConsumer{TEvent,THandler}"/> for each topic the service subscribes to,
    /// and <see cref="AddOutboxDispatcher{TDbContext}"/> for each DbContext that owns an outbox.
    /// </summary>
    public static IServiceCollection AddKafkaMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KafkaOptions>()
            .Bind(configuration.GetSection(KafkaOptions.SectionName));

        return services;
    }

    public static IServiceCollection AddKafkaConsumer<TEvent, THandler>(
        this IServiceCollection services,
        string topic)
        where TEvent : class, IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        services.TryAddScoped<THandler>();

        services.AddSingleton<IHostedService>(sp => new KafkaConsumerBackgroundService<TEvent, THandler>(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOptions<KafkaOptions>>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<KafkaConsumerBackgroundService<TEvent, THandler>>>(),
            topic));

        return services;
    }

    public static IServiceCollection AddOutboxDispatcher<TDbContext>(this IServiceCollection services)
        where TDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        services.AddHostedService<OutboxDispatcherBackgroundService<TDbContext>>();
        return services;
    }
}
