namespace EShop.Contracts;

public sealed record OrderItemDto(string Sku, int Quantity, decimal Price);

public sealed record OrderCreatedIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid OrderId,
    string CustomerId,
    decimal TotalAmount,
    IReadOnlyList<OrderItemDto> Items
) : IIntegrationEvent
{
    public const string TopicName = "orders.created";
}
