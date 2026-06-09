using EShop.Contracts;

namespace EShop.Messaging.Kafka;

/// <summary>
/// Service-side handler for an integration event. Implementations are responsible for their own
/// transactional consistency (DB write + ProcessedMessage insert + outbox enqueue, all in one tx).
/// </summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct);
}
