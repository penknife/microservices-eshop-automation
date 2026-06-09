using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EShop.Messaging.Kafka.Outbox;

public static class OutboxModelBuilder
{
    public static void ConfigureOutbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages");
            b.HasKey(x => x.Id);
            b.Property(x => x.Topic).HasMaxLength(200).IsRequired();
            b.Property(x => x.PayloadType).HasMaxLength(500).IsRequired();
            b.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            b.Property(x => x.PartitionKey).HasMaxLength(200);
            b.HasIndex(x => new { x.DispatchedAt, x.OccurredAt })
                .HasDatabaseName("ix_outbox_pending");
        });
    }
}
