// src/Services/Audit/Audit.Service/Handlers/OrderCreatedAuditHandler.cs
using Audit.Service.Domain;
using Audit.Service.Infrastructure;
using EShop.Contracts;
using EShop.Messaging.Kafka;
using EShop.Messaging.Kafka.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Audit.Service.Handlers;

public class OrderCreatedAuditHandler : IIntegrationEventHandler<OrderCreatedIntegrationEvent>
{
    private readonly AuditDbContext _db;
    private readonly ILogger<OrderCreatedAuditHandler> _logger;

    public OrderCreatedAuditHandler(AuditDbContext db, ILogger<OrderCreatedAuditHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task HandleAsync(OrderCreatedIntegrationEvent @event, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var alreadyProcessed = await _db.ProcessedMessages
            .AnyAsync(m => m.EventId == @event.EventId, ct);

        if (alreadyProcessed)
        {
            _logger.LogInformation("OrderCreated event {EventId} already audited; skipping", @event.EventId);
            await tx.RollbackAsync(ct);
            return;
        }

        _db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            EventId = @event.EventId,
            EventType = AuditEventType.OrderCreated,
            OrderId = @event.OrderId,
            Amount = @event.TotalAmount,
            PaymentStatus = null,
            FailureReason = null,
            OccurredAt = @event.OccurredAt,
            RecordedAt = DateTimeOffset.UtcNow,
        });

        _db.ProcessedMessages.Add(new ProcessedMessage
        {
            EventId = @event.EventId,
            EventType = nameof(OrderCreatedIntegrationEvent),
            ProcessedAt = DateTimeOffset.UtcNow,
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("Audited OrderCreated for order {OrderId}", @event.OrderId);
    }
}
