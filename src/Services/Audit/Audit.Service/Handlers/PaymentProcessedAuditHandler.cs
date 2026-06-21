// src/Services/Audit/Audit.Service/Handlers/PaymentProcessedAuditHandler.cs
using Audit.Service.Domain;
using Audit.Service.Infrastructure;
using EShop.Contracts;
using EShop.Messaging.Kafka;
using EShop.Messaging.Kafka.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Audit.Service.Handlers;

public class PaymentProcessedAuditHandler : IIntegrationEventHandler<PaymentProcessedIntegrationEvent>
{
    private readonly AuditDbContext _db;
    private readonly ILogger<PaymentProcessedAuditHandler> _logger;

    public PaymentProcessedAuditHandler(AuditDbContext db, ILogger<PaymentProcessedAuditHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task HandleAsync(PaymentProcessedIntegrationEvent @event, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var alreadyProcessed = await _db.ProcessedMessages
            .AnyAsync(m => m.EventId == @event.EventId, ct);

        if (alreadyProcessed)
        {
            _logger.LogInformation("PaymentProcessed event {EventId} already audited; skipping", @event.EventId);
            await tx.RollbackAsync(ct);
            return;
        }

        _db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            EventId = @event.EventId,
            EventType = AuditEventType.PaymentProcessed,
            OrderId = @event.OrderId,
            Amount = @event.Amount,
            PaymentStatus = (int)@event.Status,
            FailureReason = @event.FailureReason,
            OccurredAt = @event.OccurredAt,
            RecordedAt = DateTimeOffset.UtcNow,
        });

        _db.ProcessedMessages.Add(new ProcessedMessage
        {
            EventId = @event.EventId,
            EventType = nameof(PaymentProcessedIntegrationEvent),
            ProcessedAt = DateTimeOffset.UtcNow,
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("Audited PaymentProcessed for order {OrderId} status {Status}",
            @event.OrderId, @event.Status);
    }
}
