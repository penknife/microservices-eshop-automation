namespace Notification.Service.Domain;

public class Notification
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string Channel { get; set; } = "Console";
    public string Message { get; set; } = default!;
    public DateTimeOffset SentAt { get; set; }
}
