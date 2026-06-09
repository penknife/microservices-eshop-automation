namespace EShop.Contracts;

public sealed record PaymentProcessedIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid OrderId,
    Guid PaymentId,
    decimal Amount,
    PaymentStatus Status,
    string? FailureReason
) : IIntegrationEvent
{
    public const string TopicName = "payments.processed";
}
