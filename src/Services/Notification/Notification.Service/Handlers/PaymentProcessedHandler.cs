using EShop.Contracts;
using EShop.Messaging.Kafka;
using EShop.Messaging.Kafka.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notification.Service.Infrastructure;

namespace Notification.Service.Handlers;

/// <summary>
/// Consumes PaymentProcessedIntegrationEvent, persists a Notification row and writes a
/// structured Serilog log line. Terminal consumer — no outbox enqueue.
/// </summary>
public class PaymentProcessedHandler : IIntegrationEventHandler<PaymentProcessedIntegrationEvent>
{
    private readonly NotificationsDbContext _db;
    private readonly ILogger<PaymentProcessedHandler> _logger;

    public PaymentProcessedHandler(NotificationsDbContext db, ILogger<PaymentProcessedHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task HandleAsync(PaymentProcessedIntegrationEvent @event, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Idempotency check
        var alreadyProcessed = await _db.ProcessedMessages
            .AnyAsync(m => m.EventId == @event.EventId, ct);

        if (alreadyProcessed)
        {
            _logger.LogInformation(
                "PaymentProcessed event {EventId} already processed; skipping", @event.EventId);
            await tx.RollbackAsync(ct);
            return;
        }

        var message = @event.Status == PaymentStatus.Succeeded
            ? $"Payment succeeded for order {@event.OrderId}. Amount: {@event.Amount:F2}."
            : $"Payment failed for order {@event.OrderId}. Reason: {@event.FailureReason ?? "unknown"}.";

        var notification = new Domain.Notification
        {
            Id = Guid.NewGuid(),
            OrderId = @event.OrderId,
            Channel = "Console",
            Message = message,
            SentAt = DateTimeOffset.UtcNow,
        };

        _db.Notifications.Add(notification);

        _db.ProcessedMessages.Add(new ProcessedMessage
        {
            EventId = @event.EventId,
            EventType = nameof(PaymentProcessedIntegrationEvent),
            ProcessedAt = DateTimeOffset.UtcNow,
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "Notification sent for order {OrderId} status {Status}: {Message}",
            @event.OrderId, @event.Status, message);
    }
}
