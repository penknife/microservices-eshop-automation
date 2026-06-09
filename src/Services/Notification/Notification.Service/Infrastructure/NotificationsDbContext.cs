using EShop.Messaging.Kafka.Inbox;
using Microsoft.EntityFrameworkCore;

namespace Notification.Service.Infrastructure;

public class NotificationsDbContext : DbContext
{
    public NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : base(options)
    {
    }

    public DbSet<Domain.Notification> Notifications => Set<Domain.Notification>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("notifications");

        modelBuilder.Entity<Domain.Notification>(b =>
        {
            b.ToTable("notifications");
            b.HasKey(x => x.Id);
            b.Property(x => x.Channel).HasMaxLength(100).IsRequired();
            b.Property(x => x.Message).HasMaxLength(2000).IsRequired();
        });

        modelBuilder.ConfigureInbox();

        base.OnModelCreating(modelBuilder);
    }
}
