using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EShop.Messaging.Kafka.Outbox;

/// <summary>
/// Row persisted in the same DB transaction as the producing aggregate.
/// The outbox dispatcher publishes pending rows to Kafka and marks them dispatched.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; }

    [Required]
    public string Topic { get; set; } = default!;

    [Required]
    public string PayloadType { get; set; } = default!;

    [Required]
    [Column(TypeName = "jsonb")]
    public string Payload { get; set; } = default!;

    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }

    /// <summary>
    /// Optional partition key. If null, the Kafka producer uses the EventId.
    /// </summary>
    public string? PartitionKey { get; set; }
}
