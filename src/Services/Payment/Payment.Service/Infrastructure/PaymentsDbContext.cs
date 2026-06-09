using EShop.Messaging.Kafka.Inbox;
using EShop.Messaging.Kafka.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Payment.Service.Infrastructure;

public class PaymentsDbContext : DbContext
{
    public PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : base(options)
    {
    }

    public DbSet<Domain.Payment> Payments => Set<Domain.Payment>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("payments");

        modelBuilder.Entity<Domain.Payment>(b =>
        {
            b.ToTable("payments");
            b.HasKey(x => x.Id);
            b.Property(x => x.Amount).HasColumnType("numeric(18,2)");
            b.Property(x => x.Status).HasConversion<int>();
            b.Property(x => x.FailureReason).HasMaxLength(2000);
        });

        modelBuilder.ConfigureOutbox();
        modelBuilder.ConfigureInbox();

        base.OnModelCreating(modelBuilder);
    }
}
