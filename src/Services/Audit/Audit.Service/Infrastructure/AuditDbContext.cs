// src/Services/Audit/Audit.Service/Infrastructure/AuditDbContext.cs
using Audit.Service.Domain;
using EShop.Messaging.Kafka.Inbox;
using Microsoft.EntityFrameworkCore;

namespace Audit.Service.Infrastructure;

public class AuditDbContext : DbContext
{
    public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options)
    {
    }

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("audit");

        modelBuilder.Entity<AuditEntry>(b =>
        {
            b.ToTable("audit_entries");
            b.HasKey(x => x.Id);
            b.Property(x => x.EventType).HasConversion<int>();
            b.Property(x => x.Amount).HasColumnType("numeric(18,2)");
            b.Property(x => x.FailureReason).HasMaxLength(2000);
            b.HasIndex(x => x.OrderId).HasDatabaseName("ix_audit_order_id");
            b.HasIndex(x => x.OccurredAt).HasDatabaseName("ix_audit_occurred_at");
        });

        modelBuilder.ConfigureInbox();

        base.OnModelCreating(modelBuilder);
    }
}
