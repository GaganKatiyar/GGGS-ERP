using Gggs.Data;
using Gggs.Modules.Workflow.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Workflow.Infrastructure;

/// <summary>The Workflow module's tables, in the <c>wf</c> schema.</summary>
internal sealed class WorkflowModel : IModuleModel
{
    public const string Schema = "wf";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<WorkflowDefinition>(entity =>
        {
            entity.ToTable("WorkflowDefinition", Schema);
            entity.Property(definition => definition.Code).HasMaxLength(32).IsRequired();
            entity.Property(definition => definition.Name).HasMaxLength(200).IsRequired();
            entity.Property(definition => definition.Description).HasMaxLength(1000);
            entity.Property(definition => definition.SubjectType).HasMaxLength(64).IsRequired();
            entity.Property(definition => definition.StagesJson).IsRequired();

            entity.HasIndex(definition => new { definition.TenantId, definition.Code })
                .HasDatabaseName("UX_WorkflowDefinition_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            // One active definition per subject: which approval applies is never ambiguous.
            entity.HasIndex(definition => new { definition.TenantId, definition.SubjectType })
                .HasDatabaseName("UX_WorkflowDefinition_TenantId_SubjectType_Active")
                .IsUnique()
                .HasFilter("[IsActive] = 1 AND [IsDeleted] = 0");

            entity.Ignore(definition => definition.Stages);
            entity.Ignore(definition => definition.DomainEvents);
        });

        modelBuilder.Entity<WorkflowInstance>(entity =>
        {
            entity.ToTable("WorkflowInstance", Schema);
            entity.Property(instance => instance.SubjectType).HasMaxLength(64).IsRequired();
            entity.Property(instance => instance.Title).HasMaxLength(300).IsRequired();
            entity.Property(instance => instance.Link).HasMaxLength(300);
            entity.Property(instance => instance.FieldsJson).IsRequired();
            entity.Property(instance => instance.StagesJson).IsRequired();
            entity.Property(instance => instance.Status).HasConversion<byte>();

            // One open request per subject: a record is never being approved twice at once.
            entity.HasIndex(instance => new { instance.TenantId, instance.SubjectType, instance.SubjectId })
                .HasDatabaseName("UX_WorkflowInstance_TenantId_Subject_Open")
                .IsUnique()
                .HasFilter("[Status] IN (1, 4) AND [IsDeleted] = 0");

            entity.HasIndex(instance => new { instance.TenantId, instance.RequesterEmployeeId, instance.SubmittedDate })
                .HasDatabaseName("IX_WorkflowInstance_TenantId_Requester");

            entity.HasIndex(instance => new { instance.TenantId, instance.Status, instance.SubmittedDate })
                .HasDatabaseName("IX_WorkflowInstance_TenantId_Status");

            entity.Ignore(instance => instance.Stages);
            entity.Ignore(instance => instance.Fields);
            entity.Ignore(instance => instance.IsOpen);
            entity.Ignore(instance => instance.DomainEvents);
        });

        modelBuilder.Entity<WorkflowTask>(entity =>
        {
            entity.ToTable("WorkflowTask", Schema);
            entity.Property(task => task.StageName).HasMaxLength(100).IsRequired();
            entity.Property(task => task.Status).HasConversion<byte>();
            entity.Property(task => task.Comment).HasMaxLength(1000);

            entity.HasOne<WorkflowInstance>().WithMany().HasForeignKey(task => task.InstanceId).OnDelete(DeleteBehavior.Restrict);

            // The inbox: my pending tasks, oldest first.
            entity.HasIndex(task => new { task.TenantId, task.AssigneeEmployeeId, task.Status, task.DueDate })
                .HasDatabaseName("IX_WorkflowTask_TenantId_Assignee_Status");

            // The SLA sweep: pending tasks past their due date.
            entity.HasIndex(task => new { task.TenantId, task.Status, task.DueDate })
                .HasDatabaseName("IX_WorkflowTask_TenantId_Status_DueDate");

            entity.HasIndex(task => new { task.TenantId, task.InstanceId }).HasDatabaseName("IX_WorkflowTask_TenantId_InstanceId");

            entity.Ignore(task => task.IsPending);
            entity.Ignore(task => task.DomainEvents);
        });

        modelBuilder.Entity<WorkflowAction>(entity =>
        {
            entity.ToTable("WorkflowAction", Schema);
            entity.Property(action => action.Kind).HasConversion<byte>();
            entity.Property(action => action.Comment).HasMaxLength(1000);
            entity.HasOne<WorkflowInstance>().WithMany().HasForeignKey(action => action.InstanceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(action => new { action.TenantId, action.InstanceId, action.OccurredDate }).HasDatabaseName("IX_WorkflowAction_TenantId_InstanceId");
            entity.Ignore(action => action.DomainEvents);
        });

        modelBuilder.Entity<Delegation>(entity =>
        {
            entity.ToTable("Delegation", Schema);
            entity.Property(delegation => delegation.SubjectType).HasMaxLength(64);
            entity.Property(delegation => delegation.Reason).HasMaxLength(300);
            entity.HasIndex(delegation => new { delegation.TenantId, delegation.DelegatorEmployeeId, delegation.StartsOn })
                .HasDatabaseName("IX_Delegation_TenantId_Delegator");
            entity.Ignore(delegation => delegation.DomainEvents);
        });
    }
}

internal static class WorkflowData
{
    extension(AppDbContext db)
    {
        public DbSet<WorkflowDefinition> WorkflowDefinitions => db.Set<WorkflowDefinition>();

        public DbSet<WorkflowInstance> WorkflowInstances => db.Set<WorkflowInstance>();

        public DbSet<WorkflowTask> WorkflowTasks => db.Set<WorkflowTask>();

        public DbSet<WorkflowAction> WorkflowActions => db.Set<WorkflowAction>();

        public DbSet<Delegation> Delegations => db.Set<Delegation>();
    }
}
