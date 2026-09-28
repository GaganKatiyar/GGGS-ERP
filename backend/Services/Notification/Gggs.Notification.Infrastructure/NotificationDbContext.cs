using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence;
using Gggs.Notification.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Notification.Infrastructure;

/// <summary>The notification database: templates, the in-app inbox and the outbound email queue.</summary>
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options, ITenantContext tenantContext)
    : GggsDbContext(options, tenantContext)
{
    protected override string DefaultSchema => "ntf";

    public DbSet<NotificationTemplate> Templates => Set<NotificationTemplate>();

    public DbSet<InAppNotification> InAppNotifications => Set<InAppNotification>();

    public DbSet<OutboundMessage> OutboundMessages => Set<OutboundMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<NotificationTemplate>(entity =>
        {
            entity.ToTable("NotificationTemplate");
            entity.Property(x => x.EventCode).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Channel).HasConversion<byte>();
            entity.Property(x => x.Locale).HasMaxLength(10).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(200);
            entity.Property(x => x.BodyTemplate).IsRequired();

            // One active template per (event, channel): NotificationTemplateService checks this
            // before creating a second one, and the filtered index is the real guarantee.
            entity.HasIndex(x => new { x.TenantId, x.EventCode, x.Channel })
                .HasDatabaseName("UX_NotificationTemplate_TenantId_EventCode_Channel_Active")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0 AND [IsActive] = 1");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<InAppNotification>(entity =>
        {
            entity.ToTable("InAppNotification");
            entity.Property(x => x.EventCode).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Body).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.Link).HasMaxLength(500);

            // The inbox's own read: "my notifications, newest first", and "how many unread".
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.IsRead, x.CreatedDate })
                .HasDatabaseName("IX_InAppNotification_TenantId_UserId_IsRead_CreatedDate");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<OutboundMessage>(entity =>
        {
            entity.ToTable("OutboundMessage");
            entity.Property(x => x.RecipientAddress).HasMaxLength(256).IsRequired();
            entity.Property(x => x.EventCode).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Body).IsRequired();
            entity.Property(x => x.Status).HasConversion<byte>();
            entity.Property(x => x.ProviderMessageId).HasMaxLength(200);
            entity.Property(x => x.LastError).HasMaxLength(2000);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(300).IsRequired();

            entity.HasIndex(x => new { x.TenantId, x.IdempotencyKey })
                .HasDatabaseName("UX_OutboundMessage_TenantId_IdempotencyKey")
                .IsUnique();

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.AddGggsMessagingModel();

        base.OnModelCreating(modelBuilder);
    }
}
