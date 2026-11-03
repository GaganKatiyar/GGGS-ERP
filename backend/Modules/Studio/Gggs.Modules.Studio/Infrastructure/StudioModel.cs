using Gggs.Data;
using Gggs.Modules.Studio.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Studio.Infrastructure;

/// <summary>The Studio module's tables, in the <c>stu</c> schema.</summary>
internal sealed class StudioModel : IModuleModel
{
    public const string Schema = "stu";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Automation>(entity =>
        {
            entity.ToTable("Automation", Schema);
            entity.Property(row => row.Name).HasMaxLength(120);
            entity.Property(row => row.TriggerCode).HasMaxLength(80);
            entity.Property(row => row.Operation).HasMaxLength(60);
            entity.Property(row => row.ConditionsJson).HasMaxLength(4000).IsRequired();
            entity.Property(row => row.ParametersJson).HasMaxLength(8000).IsRequired();
            entity.HasIndex(row => new { row.TenantId, row.TriggerCode }).HasDatabaseName("IX_Automation_TenantId_Trigger");
            entity.Ignore(row => row.Conditions);
            entity.Ignore(row => row.Parameters);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<ChannelRoute>(entity =>
        {
            entity.ToTable("ChannelRoute", Schema);
            entity.Property(row => row.Channel).HasMaxLength(20);
            entity.HasIndex(row => new { row.TenantId, row.Channel }).HasDatabaseName("UX_ChannelRoute_TenantId_Channel").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<ProvisioningSetting>(entity =>
        {
            entity.ToTable("ProvisioningSetting", Schema);
            entity.HasIndex(row => new { row.TenantId, row.ConnectionId }).HasDatabaseName("UX_ProvisioningSetting_TenantId_Connection").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<ProvisionedAccount>(entity =>
        {
            entity.ToTable("ProvisionedAccount", Schema);
            entity.Property(row => row.ExternalId).HasMaxLength(200);
            entity.Property(row => row.Login).HasMaxLength(256);
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.LastMessage).HasMaxLength(500);

            // One account per person per directory, however often a hire is delivered.
            entity.HasIndex(row => new { row.TenantId, row.EmployeeId, row.ConnectionId }).HasDatabaseName("UX_ProvisionedAccount_TenantId_Employee_Connection").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasIndex(row => new { row.TenantId, row.Status, row.NextAttemptAt }).HasDatabaseName("IX_ProvisionedAccount_TenantId_Status_Next");
            entity.Ignore(row => row.NeedsAttention);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<AutomationDelivery>(entity =>
        {
            entity.ToTable("AutomationDelivery", Schema);
            entity.Property(row => row.TriggerCode).HasMaxLength(80);
            entity.Property(row => row.PayloadJson).IsRequired();
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.LastMessage).HasMaxLength(500);

            // An automation acts once on each event, however often the event is delivered.
            entity.HasIndex(row => new { row.TenantId, row.AutomationId, row.EventId }).HasDatabaseName("UX_AutomationDelivery_TenantId_Automation_Event").IsUnique();
            entity.HasIndex(row => new { row.TenantId, row.Status, row.NextAttemptAt }).HasDatabaseName("IX_AutomationDelivery_TenantId_Status_Next");
            entity.HasIndex(row => new { row.TenantId, row.CreatedDate }).HasDatabaseName("IX_AutomationDelivery_TenantId_Created");
            entity.Ignore(row => row.Payload);
            entity.Ignore(row => row.DomainEvents);
        });
    }
}

internal static class StudioData
{
    extension(AppDbContext db)
    {
        public DbSet<Automation> Automations => db.Set<Automation>();

        public DbSet<AutomationDelivery> AutomationDeliveries => db.Set<AutomationDelivery>();

        public DbSet<ChannelRoute> ChannelRoutes => db.Set<ChannelRoute>();

        public DbSet<ProvisioningSetting> ProvisioningSettings => db.Set<ProvisioningSetting>();

        public DbSet<ProvisionedAccount> ProvisionedAccounts => db.Set<ProvisionedAccount>();
    }
}
