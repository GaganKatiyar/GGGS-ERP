using Microsoft.EntityFrameworkCore;

namespace Gggs.BuildingBlocks.Messaging;

/// <summary>Maps the outbox and inbox into a service's own database.</summary>
public static class MessagingModelExtensions
{
    public const string Schema = "messaging";

    /// <summary>
    /// Adds the outbox and inbox tables. Called from each service's <c>OnModelCreating</c>, because
    /// the outbox has to live in the same database as the business data - that shared transaction
    /// is the entire guarantee.
    /// </summary>
    public static ModelBuilder AddGggsMessagingModel(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("OutboxMessage", Schema);
            // Non-clustered on Id: message ids are time-ordered but not in the byte order SQL
            // Server sorts on, so clustering on them would fragment the table. The clustered index
            // follows the relay's own ordering instead.
            entity.HasKey(message => message.Id).IsClustered(false);

            entity.Property(message => message.MessageType).HasMaxLength(200).IsRequired();
            entity.Property(message => message.Payload).IsRequired();
            entity.Property(message => message.CorrelationId).HasMaxLength(128);
            entity.Property(message => message.LastError).HasMaxLength(2000);
            entity.Property(message => message.Status).HasConversion<byte>();

            // The relay's only query: pending work that is due, oldest first. Filtered so the
            // index stays small as published rows accumulate between cleanup sweeps.
            entity.HasIndex(message => new { message.Status, message.NextAttemptDate, message.OccurredDate })
                .HasDatabaseName("IX_OutboxMessage_Pending")
                .HasFilter("[Status] = 0");

            // Cleanup and "was this ever sent?" support questions.
            entity.HasIndex(message => new { message.TenantId, message.PublishedDate })
                .HasDatabaseName("IX_OutboxMessage_TenantId_PublishedDate");

            entity.HasIndex(message => new { message.OccurredDate, message.Id })
                .HasDatabaseName("CIX_OutboxMessage_OccurredDate")
                .IsClustered();
        });

        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("InboxMessage", Schema);

            // Composite key is the deduplication rule: one row per message per consumer, so two
            // consumers each handle a message once and neither handles it twice.
            entity.HasKey(message => new { message.MessageId, message.ConsumerName });

            entity.Property(message => message.ConsumerName).HasMaxLength(200).IsRequired();
            entity.HasIndex(message => message.ProcessedDate).HasDatabaseName("IX_InboxMessage_ProcessedDate");
        });

        return modelBuilder;
    }
}
