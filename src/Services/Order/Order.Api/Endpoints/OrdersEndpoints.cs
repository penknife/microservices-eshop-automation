using EShop.Contracts;
using EShop.Messaging.Kafka;
using Microsoft.EntityFrameworkCore;
using Order.Api.Domain;
using Order.Api.Infrastructure;

namespace Order.Api.Endpoints;

public sealed record CreateOrderItemRequest(string Sku, int Quantity, decimal Price);
public sealed record CreateOrderRequest(string CustomerId, IReadOnlyList<CreateOrderItemRequest> Items);
public sealed record OrderResponse(Guid Id, string CustomerId, decimal TotalAmount, DateTimeOffset CreatedAt, IReadOnlyList<OrderItemResponse> Items);
public sealed record OrderItemResponse(string Sku, int Quantity, decimal Price);

public static class OrdersEndpoints
{
    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/orders").WithTags("Orders");

        group.MapPost("/", CreateOrder);
        group.MapGet("/{id:guid}", GetOrder);

        return app;
    }

    private static async Task<IResult> CreateOrder(
        CreateOrderRequest request,
        OrdersDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerId))
        {
            return Results.BadRequest(new { error = "customerId is required" });
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { error = "items must contain at least one entry" });
        }

        if (request.Items.Any(i => i.Quantity <= 0 || i.Price < 0 || string.IsNullOrWhiteSpace(i.Sku)))
        {
            return Results.BadRequest(new { error = "every item requires sku, positive quantity, non-negative price" });
        }

        var order = new Domain.Order
        {
            Id = Guid.NewGuid(),
            CustomerId = request.CustomerId,
            CreatedAt = DateTimeOffset.UtcNow,
            Items = request.Items.Select(i => new OrderItem
            {
                Id = Guid.NewGuid(),
                Sku = i.Sku,
                Quantity = i.Quantity,
                Price = i.Price,
            }).ToList(),
        };

        order.TotalAmount = order.Items.Sum(i => i.Price * i.Quantity);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.Orders.Add(order);

        var @event = new OrderCreatedIntegrationEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTimeOffset.UtcNow,
            OrderId: order.Id,
            CustomerId: order.CustomerId,
            TotalAmount: order.TotalAmount,
            Items: order.Items.Select(i => new OrderItemDto(i.Sku, i.Quantity, i.Price)).ToList());

        await db.EnqueueAsync(OrderCreatedIntegrationEvent.TopicName, @event, partitionKey: order.Id.ToString(), ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Results.Created(
            $"/orders/{order.Id}",
            MapToResponse(order));
    }

    private static async Task<IResult> GetOrder(Guid id, OrdersDbContext db, CancellationToken ct)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

        return order is null
            ? Results.NotFound()
            : Results.Ok(MapToResponse(order));
    }

    private static OrderResponse MapToResponse(Domain.Order order) => new(
        order.Id,
        order.CustomerId,
        order.TotalAmount,
        order.CreatedAt,
        order.Items.Select(i => new OrderItemResponse(i.Sku, i.Quantity, i.Price)).ToList());
}
