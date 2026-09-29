using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence;
using Gggs.Workflow.Domain.Snapshots;
using Gggs.Workflow.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Workflow.Infrastructure;

/// <summary>Workflow's database: definitions, running instances, tasks, delegations, and the
/// employee snapshot fed from Core HR.</summary>
public sealed class WorkflowDbContext(DbContextOptions<WorkflowDbContext> options, ITenantContext tenantContext)
    : GggsDbContext(options, tenantContext)
{
    protected override string DefaultSchema => "wf";

    public DbSet<WorkflowDefinition> Definitions => Set<WorkflowDefinition>();

    public DbSet<WorkflowDefinitionVersion> DefinitionVersions => Set<WorkflowDefinitionVersion>();

    public DbSet<WorkflowInstance> Instances => Set<WorkflowInstance>();

    public DbSet<WorkflowTask> Tasks => Set<WorkflowTask>();

    public DbSet<WorkflowActionLog> ActionLog => Set<WorkflowActionLog>();

    public DbSet<Delegation> Delegations => Set<Delegation>();

    public DbSet<EmployeeSnapshot> EmployeeSnapshots => Set<EmployeeSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<WorkflowDefinition>(entity =>
        {
            entity.ToTable("WorkflowDefinition");
            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.EntityType).HasMaxLength(50).IsRequired();

            entity.HasIndex(x => new { x.TenantId, x.Code })
                .HasDatabaseName("UX_WorkflowDefinition_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<WorkflowDefinitionVersion>(entity =>
        {
            entity.ToTable("WorkflowDefinitionVersion");
            entity.Property(x => x.Status).HasConversion<byte>();
            entity.Property(x => x.DefinitionJson).IsRequired();

            entity.HasIndex(x => new { x.TenantId, x.DefinitionId, x.Version })
                .HasDatabaseName("UX_WorkflowDefinitionVersion_TenantId_DefinitionId_Version")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            // WorkflowEngine.StartAsync relies on at most one Published row per definition.
            entity.HasIndex(x => new { x.TenantId, x.DefinitionId, x.Status })
                .HasDatabaseName("IX_WorkflowDefinitionVersion_TenantId_DefinitionId_Status_Published")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0 AND [Status] = 2");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<WorkflowInstance>(entity =>
        {
            entity.ToTable("WorkflowInstance");
            entity.Property(x => x.EntityType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.SourceService).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Status).HasConversion<byte>();
            entity.Property(x => x.CurrentStepKey).HasMaxLength(100);
            entity.Property(x => x.ContextJson).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(100);

            // WorkflowEngine.StartAsync checks this before starting a second instance for the
            // same entity - the filtered unique index is the real guarantee, the check is the
            // friendly error.
            entity.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId })
                .HasDatabaseName("UX_WorkflowInstance_TenantId_EntityType_EntityId_Running")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0 AND [Status] = 1");

            entity.HasIndex(x => new { x.TenantId, x.InitiatorUserId, x.Status })
                .HasDatabaseName("IX_WorkflowInstance_TenantId_InitiatorUserId_Status");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<WorkflowTask>(entity =>
        {
            entity.ToTable("WorkflowTask");
            entity.Property(x => x.StepKey).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Status).HasConversion<byte>();
            entity.Property(x => x.Comment).HasMaxLength(1000);

            // The approvals inbox: "my pending tasks, oldest due first".
            entity.HasIndex(x => new { x.TenantId, x.AssigneeUserId, x.Status, x.DueDate })
                .HasDatabaseName("IX_WorkflowTask_TenantId_AssigneeUserId_Status_DueDate");

            entity.HasIndex(x => new { x.TenantId, x.InstanceId, x.StepKey })
                .HasDatabaseName("IX_WorkflowTask_TenantId_InstanceId_StepKey");

            // The SLA scanner's own query: every pending task past its due date.
            entity.HasIndex(x => new { x.TenantId, x.Status, x.DueDate })
                .HasDatabaseName("IX_WorkflowTask_TenantId_Status_DueDate");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<WorkflowActionLog>(entity =>
        {
            entity.ToTable("WorkflowActionLog");
            entity.Property(x => x.Action).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Comment).HasMaxLength(1000);

            entity.HasIndex(x => new { x.TenantId, x.InstanceId, x.OccurredDate })
                .HasDatabaseName("IX_WorkflowActionLog_TenantId_InstanceId_OccurredDate");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<Delegation>(entity =>
        {
            entity.ToTable("Delegation");
            entity.Property(x => x.Reason).HasMaxLength(500);

            entity.HasIndex(x => new { x.TenantId, x.FromUserId, x.StartDate, x.EndDate })
                .HasDatabaseName("IX_Delegation_TenantId_FromUserId_StartDate_EndDate");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<EmployeeSnapshot>(entity =>
        {
            entity.ToTable("EmployeeSnapshot", "ref");
            entity.Property(x => x.EmployeeCode).HasMaxLength(30).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();

            entity.HasIndex(x => new { x.TenantId, x.EmployeeId })
                .HasDatabaseName("UX_EmployeeSnapshot_TenantId_EmployeeId")
                .IsUnique();

            entity.HasIndex(x => new { x.TenantId, x.ManagerEmployeeId })
                .HasDatabaseName("IX_EmployeeSnapshot_TenantId_ManagerEmployeeId");

            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.AddGggsMessagingModel();

        base.OnModelCreating(modelBuilder);
    }
}
