// src/Services/Audit/Audit.Service/Domain/AuditEntry.cs
using EShop.Contracts;

namespace Audit.Service.Domain;

public enum AuditEventType
{
    OrderCreated = 1,
    PaymentProcessed = 2,
}

public class AuditEntry
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public AuditEventType EventType { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    // Null for OrderCreated entries
    public EShop.Contracts.PaymentStatus? PaymentStatus { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
