namespace Order.Api.Domain;

public class Order
{
    public Guid Id { get; set; }
    public string CustomerId { get; set; } = default!;
    public decimal TotalAmount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<OrderItem> Items { get; set; } = new();
}

public class OrderItem
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string Sku { get; set; } = default!;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}
