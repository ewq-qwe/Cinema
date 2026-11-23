using Catalog.Dal.IntegrationEventLog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Dal.Configurations;

/// <summary>Лаб. 08: таблиця transactional outbox — dbo.IntegrationEventLog.</summary>
public sealed class IntegrationEventLogEntryConfiguration : IEntityTypeConfiguration<IntegrationEventLogEntry>
{
    public void Configure(EntityTypeBuilder<IntegrationEventLogEntry> builder)
    {
        builder.ToTable("IntegrationEventLog", t =>
            t.HasCheckConstraint("CK_IntegrationEventLog_State", "[State] IN (0, 1)"));
        builder.HasKey(e => e.EventId);
        builder.Property(e => e.EventId).ValueGeneratedNever();
        builder.Property(e => e.EventType).IsRequired().HasMaxLength(IntegrationEventLogEntry.TypeMaxLength).IsUnicode(false);
        builder.Property(e => e.Exchange).IsRequired().HasMaxLength(IntegrationEventLogEntry.TypeMaxLength).IsUnicode(false);
        builder.Property(e => e.RoutingKey).IsRequired().HasMaxLength(IntegrationEventLogEntry.TypeMaxLength).IsUnicode(false);
        builder.Property(e => e.Content).IsRequired();                                  // nvarchar(max) — JSON події
        builder.Property(e => e.CorrelationId).HasMaxLength(100).IsUnicode(false);
        builder.Property(e => e.TraceParent).HasMaxLength(64).IsUnicode(false);         // 00-<32>-<16>-<2> = 55 символів
        builder.Property(e => e.OccurredAt).HasColumnType("datetime2(7)");
        builder.Property(e => e.PublishedAt).HasColumnType("datetime2(7)");
        builder.Property(e => e.LastError).HasMaxLength(IntegrationEventLogEntry.ErrorMaxLength);
        builder.Property(e => e.State).HasConversion<int>();

        // Фоновий процес вибирає неопубліковані в порядку виникнення: фільтрований індекс лише по «хвосту» таблиці
        builder.HasIndex(e => new { e.State, e.OccurredAt })
               .HasDatabaseName("IX_IntegrationEventLog_Pending")
               .HasFilter("[State] = 0");
    }
}
