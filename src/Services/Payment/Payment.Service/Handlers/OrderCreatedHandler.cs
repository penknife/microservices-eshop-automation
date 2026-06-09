using EShop.Contracts;
using EShop.Messaging.Kafka;
using EShop.Messaging.Kafka.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Payment.Service.Infrastructure;

namespace Payment.Service.Handlers;

/// <summary>
/// Consumes OrderCreatedIntegrationEvent, simulates payment processing, persists the result
/// and enqueues a PaymentProcessedIntegrationEvent to the outbox — all in one DB transaction.
/// </summary>
public class OrderCreatedHandler : IIntegrationEventHandler<OrderCreatedIntegrationEvent>
{
    private readonly PaymentsDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<OrderCreatedHandler> _logger;

    public OrderCreatedHandler(PaymentsDbContext db, IConfiguration config, ILogger<OrderCreatedHandler> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task HandleAsync(OrderCreatedIntegrationEvent @event, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Idempotency check
        var alreadyProcessed = await _db.ProcessedMessages
            .AnyAsync(m => m.EventId == @event.EventId, ct);

        if (alreadyProcessed)
        {
            _logger.LogInformation(
                "OrderCreated event {EventId} already processed; skipping", @event.EventId);
            await tx.RollbackAsync(ct);
            return;
        }

        // Simulate: fail if total > configured threshold
        var failureThreshold = _config.GetValue("Payment:FailureThreshold", decimal.MaxValue);
        bool succeeded = @event.TotalAmount <= failureThreshold;
        string? failureReason = succeeded ? null : $"Amount {0}{1:F2} exceeds threshold {2:F2}";

        if (!succeeded)
        {
            failureReason = $"Amount {0}{@event.TotalAmount:F2} exceeds configured threshold {0}{failureThreshold:F2}";
        }

        var payment = new Domain.Payment
        {
            Id = Guid.NewGuid(),
            OrderId = @event.OrderId,
            Amount = @event.TotalAmount,
            Status = succeeded ? Domain.PaymentStatus.Succeeded : Domain.PaymentStatus.Failed,
            FailureReason = failureReason,
            CreatedAt = DateTimeOffset.UtcNow,
            ProcessedAt = DateTimeOffset.UtcNow,
        };

        _db.Payments.Add(payment);

        _db.ProcessedMessages.Add(new ProcessedMessage
        {
            EventId = @event.EventId,
            EventType = nameof(OrderCreatedIntegrationEvent),
            ProcessedAt = DateTimeOffset.UtcNow,
        });

        var outboxEvent = new PaymentProcessedIntegrationEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTimeOffset.UtcNow,
            OrderId: @event.OrderId,
            PaymentId: payment.Id,
            Amount: payment.Amount,
            Status: succeeded ? EShop.Contracts.PaymentStatus.Succeeded : EShop.Contracts.PaymentStatus.Failed,
            FailureReason: failureReason);

        await _db.EnqueueAsync(PaymentProcessedIntegrationEvent.TopicName, outboxEvent, partitionKey: @event.OrderId.ToString(), ct);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "Payment {PaymentId} for order {OrderId} => {Status}",
            payment.Id, payment.OrderId, payment.Status);
    }
}
