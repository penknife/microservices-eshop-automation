// src/Services/Audit/Audit.Service/Endpoints/AuditEndpoints.cs
using Audit.Service.Domain;
using Audit.Service.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Audit.Service.Endpoints;

public sealed record AuditEntryResponse(
    Guid Id,
    Guid EventId,
    string EventType,
    Guid OrderId,
    decimal Amount,
    int? PaymentStatus,
    string? FailureReason,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt);

public sealed record PaymentSummaryResponse(
    DateOnly Date,
    int Succeeded,
    int Failed,
    decimal TotalAmount);

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/audit").WithTags("Audit");

        group.MapGet("/orders/{orderId:guid}", GetOrderAuditTrail);
        group.MapGet("/summary", GetPaymentSummary);

        return app;
    }

    private static async Task<IResult> GetOrderAuditTrail(Guid orderId, AuditDbContext db, CancellationToken ct)
    {
        var entries = await db.AuditEntries
            .AsNoTracking()
            .Where(e => e.OrderId == orderId)
            .OrderBy(e => e.OccurredAt)
            .Select(e => new AuditEntryResponse(
                e.Id,
                e.EventId,
                e.EventType.ToString(),
                e.OrderId,
                e.Amount,
                e.PaymentStatus,
                e.FailureReason,
                e.OccurredAt,
                e.RecordedAt))
            .ToListAsync(ct);

        return Results.Ok(entries);
    }

    private static async Task<IResult> GetPaymentSummary(AuditDbContext db, CancellationToken ct)
    {
        // Materialize PaymentProcessed entries then group client-side to avoid
        // EF Core translation limitations with DateOnly.FromDateTime + UtcDateTime.Date.
        var rows = await db.AuditEntries
            .AsNoTracking()
            .Where(e => e.EventType == AuditEventType.PaymentProcessed)
            .Select(e => new { e.OccurredAt, e.PaymentStatus, e.Amount })
            .ToListAsync(ct);

        // PaymentStatus values: 1 = Succeeded, 2 = Failed (from EShop.Contracts.PaymentStatus)
        var summary = rows
            .GroupBy(e => DateOnly.FromDateTime(e.OccurredAt.UtcDateTime.Date))
            .Select(g => new PaymentSummaryResponse(
                g.Key,
                g.Count(e => e.PaymentStatus == 1),
                g.Count(e => e.PaymentStatus == 2),
                g.Sum(e => e.Amount)))
            .OrderByDescending(r => r.Date)
            .ToList();

        return Results.Ok(summary);
    }
}
