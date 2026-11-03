using Gggs.Data;
using Gggs.Modules.Notification.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Notification.Infrastructure;

/// <summary>The Notification module's tables, in the <c>ntf</c> schema.</summary>
internal sealed class NotificationModel : IModuleModel
{
    public const string Schema = "ntf";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<NotificationTemplate>(entity =>
        {
            entity.ToTable("NotificationTemplate", Schema);
            entity.Property(template => template.Code).HasMaxLength(64).IsRequired();
            entity.Property(template => template.Name).HasMaxLength(200).IsRequired();
            entity.Property(template => template.Subject).HasMaxLength(300).IsRequired();
            entity.Property(template => template.Body).HasMaxLength(4000).IsRequired();
            entity.HasIndex(template => new { template.TenantId, template.Code })
                .HasDatabaseName("UX_NotificationTemplate_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(template => template.DomainEvents);
        });

        modelBuilder.Entity<InAppNotification>(entity =>
        {
            entity.ToTable("InAppNotification", Schema);
            entity.Property(notification => notification.EventCode).HasMaxLength(64).IsRequired();
            entity.Property(notification => notification.Title).HasMaxLength(300).IsRequired();
            entity.Property(notification => notification.Body).HasMaxLength(4000).IsRequired();
            entity.Property(notification => notification.Link).HasMaxLength(300);

            // The inbox and its unread badge, for an employee or for a bare user.
            entity.HasIndex(notification => new { notification.TenantId, notification.RecipientEmployeeId, notification.ReadDate, notification.CreatedDate })
                .HasDatabaseName("IX_InAppNotification_TenantId_Employee");
            entity.HasIndex(notification => new { notification.TenantId, notification.RecipientUserId, notification.ReadDate, notification.CreatedDate })
                .HasDatabaseName("IX_InAppNotification_TenantId_User");
            entity.Ignore(notification => notification.DomainEvents);
        });

        modelBuilder.Entity<NotificationPreference>(entity =>
        {
            entity.ToTable("NotificationPreference", Schema);
            entity.Property(preference => preference.EventCode).HasMaxLength(64).IsRequired();
            entity.HasIndex(preference => new { preference.TenantId, preference.UserId, preference.EventCode })
                .HasDatabaseName("UX_NotificationPreference_TenantId_User_Event")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.HasIndex(preference => new { preference.TenantId, preference.EmployeeId, preference.EventCode })
                .HasDatabaseName("IX_NotificationPreference_TenantId_Employee_Event");
            entity.Ignore(preference => preference.DomainEvents);
        });

        modelBuilder.Entity<SmsMessage>(entity =>
        {
            entity.ToTable("SmsMessage", Schema);
            entity.Property(sms => sms.ToNumber).HasMaxLength(32).IsRequired();
            entity.Property(sms => sms.Body).HasMaxLength(1000).IsRequired();
            entity.Property(sms => sms.EventCode).HasMaxLength(64).IsRequired();
            entity.Property(sms => sms.Status).HasConversion<byte>();
            entity.Property(sms => sms.LastError).HasMaxLength(1000);
            entity.HasIndex(sms => new { sms.TenantId, sms.CreatedDate }).HasDatabaseName("IX_SmsMessage_TenantId_Created");
            entity.Ignore(sms => sms.DomainEvents);
        });

        modelBuilder.Entity<EmailMessage>(entity =>
        {
            entity.ToTable("EmailMessage", Schema);
            entity.Property(email => email.ToAddress).HasMaxLength(256).IsRequired();
            entity.Property(email => email.Subject).HasMaxLength(300).IsRequired();
            entity.Property(email => email.Body).HasMaxLength(8000).IsRequired();
            entity.Property(email => email.EventCode).HasMaxLength(64).IsRequired();
            entity.Property(email => email.Status).HasConversion<byte>();
            entity.Property(email => email.LastError).HasMaxLength(1000);
            entity.HasIndex(email => new { email.TenantId, email.Status, email.CreatedDate }).HasDatabaseName("IX_EmailMessage_TenantId_Status");
            entity.Ignore(email => email.DomainEvents);
        });
    }
}

internal static class NotificationData
{
    extension(AppDbContext db)
    {
        public DbSet<NotificationTemplate> NotificationTemplates => db.Set<NotificationTemplate>();

        public DbSet<InAppNotification> InAppNotifications => db.Set<InAppNotification>();

        public DbSet<EmailMessage> EmailMessages => db.Set<EmailMessage>();

        public DbSet<SmsMessage> SmsMessages => db.Set<SmsMessage>();

        public DbSet<NotificationPreference> NotificationPreferences => db.Set<NotificationPreference>();
    }
}
