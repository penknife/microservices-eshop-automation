using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Order.Api.Domain;
using Order.Api.Infrastructure;
using System.Text.Json;

namespace EShop.ContractTests.Infrastructure;

/// <summary>
/// Injects <see cref="ProviderStateMiddleware"/> into the pipeline via
/// <see cref="IStartupFilter"/> so the Pact verifier can POST to
/// <c>/provider-states</c> before each interaction.
/// </summary>
internal sealed class ProviderStateStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            app.UseMiddleware<ProviderStateMiddleware>();
            next(app);
        };
}

internal sealed class ProviderStateMiddleware(RequestDelegate next)
{
    private static readonly Guid SeedOrderId = new("11111111-1111-1111-1111-111111111111");

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Method == HttpMethods.Post &&
            context.Request.Path == "/provider-states")
        {
            await HandleAsync(context);
            return;
        }

        await next(context);
    }

    private static async Task HandleAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        var req = JsonSerializer.Deserialize<ProviderStateRequest>(body, JsonOpts);

        if (req?.State is not null)
        {
            using var scope = context.RequestServices.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

            switch (req.State)
            {
                case "order creation is allowed":
                    // No seeding needed — empty DB is sufficient.
                    break;

                case "an order with id 11111111-1111-1111-1111-111111111111 exists":
                    await EnsureOrderExistsAsync(db);
                    break;
            }
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
    }

    private static async Task EnsureOrderExistsAsync(OrdersDbContext db)
    {
        if (await db.Orders.AnyAsync(o => o.Id == SeedOrderId))
            return;

        db.Orders.Add(new Order.Api.Domain.Order
        {
            Id = SeedOrderId,
            CustomerId = "customer-123",
            TotalAmount = 19.98m,
            CreatedAt = DateTimeOffset.UtcNow,
            Items =
            [
                new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = SeedOrderId,
                    Sku = "SKU-001",
                    Quantity = 2,
                    Price = 9.99m,
                }
            ]
        });

        await db.SaveChangesAsync();
    }
}

internal sealed record ProviderStateRequest(string? State, string? Action);
