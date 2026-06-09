using System.ComponentModel.DataAnnotations;

namespace EShop.Messaging.Kafka.Inbox;

/// <summary>
/// Records that a particular integration event (by its EventId) has already been processed
/// by this service. Used to make consumer handlers idempotent.
/// </summary>
public class ProcessedMessage
{
    [Key]
    public Guid EventId { get; set; }

    [Required]
    public string EventType { get; set; } = default!;

    public DateTimeOffset ProcessedAt { get; set; }
}
