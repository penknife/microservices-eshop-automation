namespace EShop.Contracts;

/// <summary>
/// Base contract for all integration events flowing through Kafka.
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}
