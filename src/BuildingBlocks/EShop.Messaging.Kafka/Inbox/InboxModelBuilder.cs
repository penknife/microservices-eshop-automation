using Microsoft.EntityFrameworkCore;

namespace EShop.Messaging.Kafka.Inbox;

public static class InboxModelBuilder
{
    public static void ConfigureInbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcessedMessage>(b =>
        {
            b.ToTable("processed_messages");
            b.HasKey(x => x.EventId);
            b.Property(x => x.EventType).HasMaxLength(500).IsRequired();
        });
    }
}
