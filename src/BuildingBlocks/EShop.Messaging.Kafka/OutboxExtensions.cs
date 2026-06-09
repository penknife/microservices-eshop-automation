using System.Text.Json;
using EShop.Contracts;
using EShop.Messaging.Kafka.Outbox;
using Microsoft.EntityFrameworkCore;

namespace EShop.Messaging.Kafka;

/// <summary>
/// Helpers to append integration events to the outbox in the same EF transaction as the aggregate.
/// </summary>
public static class OutboxExtensions
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task EnqueueAsync<TEvent>(
        this DbContext dbContext,
        string topic,
        TEvent @event,
        string? partitionKey = null,
        CancellationToken ct = default)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(@event);

        var row = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Topic = topic,
            PayloadType = typeof(TEvent).AssemblyQualifiedName ?? typeof(TEvent).FullName!,
            Payload = JsonSerializer.Serialize(@event, s_jsonOptions),
            OccurredAt = @event.OccurredAt,
            PartitionKey = partitionKey,
        };

        await dbContext.Set<OutboxMessage>().AddAsync(row, ct).ConfigureAwait(false);
    }
}
