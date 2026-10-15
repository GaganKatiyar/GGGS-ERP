using Gggs.Data;
using Gggs.Modules.Project.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectEntity = Gggs.Modules.Project.Domain.Project;

namespace Gggs.Modules.Project.Infrastructure;

/// <summary>The Project module's tables, in the <c>prj</c> schema.</summary>
internal sealed class ProjectModel : IModuleModel
{
    public const string Schema = "prj";

    private static readonly string[] HealthDimensions = ["Schedule", "Budget", "Resources", "Scope", "Quality", "Risk", "Issues", "ClientSatisfaction", "Overall"];

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<ProjectEntity>(entity =>
        {
            entity.ToTable("Project", Schema);
            entity.Property(project => project.Code).HasMaxLength(16).IsRequired();
            entity.Property(project => project.Name).HasMaxLength(200).IsRequired();
            entity.Property(project => project.Category).HasMaxLength(100);
            entity.Property(project => project.Currency).HasMaxLength(8).IsRequired();
            entity.Property(project => project.Description).HasMaxLength(4000);
            entity.Property(project => project.WorkModeCode).HasMaxLength(64);
            entity.Property(project => project.Budget).HasPrecision(18, 2);
            entity.Property(project => project.RevenueTarget).HasPrecision(18, 2);
            entity.Property(project => project.CostBudget).HasPrecision(18, 2);
            entity.Property(project => project.Type).HasConversion<byte>();
            entity.Property(project => project.Stage).HasConversion<byte>();
            entity.Property(project => project.Status).HasConversion<byte>();
            entity.Property(project => project.Priority).HasConversion<byte>();
            entity.Property(project => project.HealthStatus).HasConversion<byte>();
            entity.Property(project => project.BillingType).HasConversion<byte>();
            entity.Property(project => project.Confidentiality).HasConversion<byte>();
            entity.Property(project => project.Methodology).HasConversion<byte>();
            entity.HasIndex(project => new { project.TenantId, project.Code })
                .HasDatabaseName("UX_Project_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.HasIndex(project => new { project.TenantId, project.Status }).HasDatabaseName("IX_Project_TenantId_Status");
            entity.HasIndex(project => new { project.TenantId, project.ClientId }).HasDatabaseName("IX_Project_TenantId_Client");
            entity.HasIndex(project => new { project.TenantId, project.ProjectManagerEmployeeId }).HasDatabaseName("IX_Project_TenantId_Manager");
            entity.Ignore(project => project.TargetEndDate);
            entity.Ignore(project => project.IsOpen);
            entity.Ignore(project => project.DomainEvents);
        });

        modelBuilder.Entity<ProjectStatusHistory>(entity =>
        {
            entity.ToTable("ProjectStatusHistory", Schema);
            entity.Property(history => history.Kind).HasMaxLength(16).IsRequired();
            entity.Property(history => history.FromValue).HasMaxLength(32).IsRequired();
            entity.Property(history => history.ToValue).HasMaxLength(32).IsRequired();
            entity.Property(history => history.Comment).HasMaxLength(1000);
            OwnedByProject(entity, history => history.ProjectId, "ProjectStatusHistory");
            entity.Ignore(history => history.DomainEvents);
        });

        modelBuilder.Entity<ProjectRole>(entity =>
        {
            entity.ToTable("ProjectRole", Schema);
            entity.Property(role => role.Code).HasMaxLength(32).IsRequired();
            entity.Property(role => role.Name).HasMaxLength(100).IsRequired();
            entity.Property(role => role.Description).HasMaxLength(500);
            entity.Property(role => role.SkillsJson).IsRequired();
            entity.HasIndex(role => new { role.TenantId, role.Code })
                .HasDatabaseName("UX_ProjectRole_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(role => role.Skills);
            entity.Ignore(role => role.DomainEvents);
        });

        modelBuilder.Entity<ProjectTeamMember>(entity =>
        {
            entity.ToTable("ProjectTeamMember", Schema);
            entity.Property(member => member.BillingRate).HasPrecision(18, 2);
            entity.Property(member => member.CostRate).HasPrecision(18, 2);
            entity.Property(member => member.Status).HasConversion<byte>();
            entity.Property(member => member.Notes).HasMaxLength(500);
            entity.HasOne<ProjectRole>().WithMany().HasForeignKey(member => member.ProjectRoleId).OnDelete(DeleteBehavior.Restrict);
            OwnedByProject(entity, member => member.ProjectId, "ProjectTeamMember");
            entity.HasIndex(member => new { member.TenantId, member.EmployeeId, member.Status }).HasDatabaseName("IX_ProjectTeamMember_TenantId_Employee");

            // One active seat per person per role on a project; the same person may hold two roles.
            entity.HasIndex(member => new { member.TenantId, member.ProjectId, member.EmployeeId, member.ProjectRoleId })
                .HasDatabaseName("UX_ProjectTeamMember_TenantId_Project_Employee_Role")
                .IsUnique()
                .HasFilter("[Status] = 1 AND [IsDeleted] = 0");
            entity.Ignore(member => member.DomainEvents);
        });

        modelBuilder.Entity<ProjectPhase>(entity =>
        {
            entity.ToTable("ProjectPhase", Schema);
            entity.Property(phase => phase.Name).HasMaxLength(100).IsRequired();
            entity.Property(phase => phase.Budget).HasPrecision(18, 2);
            entity.Property(phase => phase.Status).HasConversion<byte>();
            OwnedByProject(entity, phase => phase.ProjectId, "ProjectPhase");
            entity.Ignore(phase => phase.DomainEvents);
        });

        modelBuilder.Entity<BoardColumn>(entity =>
        {
            entity.ToTable("BoardColumn", Schema);
            entity.Property(column => column.Name).HasMaxLength(50).IsRequired();
            entity.Property(column => column.Category).HasConversion<byte>();
            OwnedByProject(entity, column => column.ProjectId, "BoardColumn");
            entity.Ignore(column => column.DomainEvents);
        });

        modelBuilder.Entity<ProjectTask>(entity =>
        {
            entity.ToTable("ProjectTask", Schema);
            entity.Property(task => task.Code).HasMaxLength(32).IsRequired();
            entity.Property(task => task.Title).HasMaxLength(300).IsRequired();
            entity.Property(task => task.Description).HasMaxLength(8000);
            entity.Property(task => task.EstimatedHours).HasPrecision(9, 2);
            entity.Property(task => task.ActualHours).HasPrecision(9, 2);
            entity.Property(task => task.Type).HasConversion<byte>();
            entity.Property(task => task.Priority).HasConversion<byte>();
            entity.HasOne<BoardColumn>().WithMany().HasForeignKey(task => task.ColumnId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProjectTask>().WithMany().HasForeignKey(task => task.ParentTaskId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProjectPhase>().WithMany().HasForeignKey(task => task.PhaseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProjectMilestone>().WithMany().HasForeignKey(task => task.MilestoneId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Sprint>().WithMany().HasForeignKey(task => task.SprintId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Requirement>().WithMany().HasForeignKey(task => task.RequirementId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProjectEntity>().WithMany().HasForeignKey(task => task.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(task => new { task.TenantId, task.ProjectId, task.Number })
                .HasDatabaseName("UX_ProjectTask_TenantId_Project_Number")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.HasIndex(task => new { task.TenantId, task.ProjectId, task.ColumnId, task.Rank }).HasDatabaseName("IX_ProjectTask_TenantId_Board");
            entity.HasIndex(task => new { task.TenantId, task.AssigneeEmployeeId }).HasDatabaseName("IX_ProjectTask_TenantId_Assignee");
            entity.HasIndex(task => new { task.TenantId, task.SprintId }).HasDatabaseName("IX_ProjectTask_TenantId_Sprint");
            entity.Ignore(task => task.IsDone);
            entity.Ignore(task => task.DomainEvents);
        });

        modelBuilder.Entity<TaskDependency>(entity =>
        {
            entity.ToTable("TaskDependency", Schema);
            entity.Property(dependency => dependency.Type).HasConversion<byte>();
            entity.HasOne<ProjectTask>().WithMany().HasForeignKey(dependency => dependency.PredecessorTaskId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProjectTask>().WithMany().HasForeignKey(dependency => dependency.SuccessorTaskId).OnDelete(DeleteBehavior.Restrict);
            OwnedByProject(entity, dependency => dependency.ProjectId, "TaskDependency");
            entity.HasIndex(dependency => new { dependency.TenantId, dependency.PredecessorTaskId, dependency.SuccessorTaskId })
                .HasDatabaseName("UX_TaskDependency_TenantId_Pair")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(dependency => dependency.DomainEvents);
        });

        modelBuilder.Entity<ProjectMilestone>(entity =>
        {
            entity.ToTable("ProjectMilestone", Schema);
            entity.Property(milestone => milestone.Name).HasMaxLength(200).IsRequired();
            entity.Property(milestone => milestone.Description).HasMaxLength(2000);
            entity.Property(milestone => milestone.Status).HasConversion<byte>();
            entity.HasOne<ProjectPhase>().WithMany().HasForeignKey(milestone => milestone.PhaseId).OnDelete(DeleteBehavior.Restrict);
            OwnedByProject(entity, milestone => milestone.ProjectId, "ProjectMilestone");
            entity.HasIndex(milestone => new { milestone.TenantId, milestone.Status, milestone.PlannedDate }).HasDatabaseName("IX_ProjectMilestone_TenantId_Status_Date");
            entity.Ignore(milestone => milestone.IsOpen);
            entity.Ignore(milestone => milestone.DomainEvents);
        });

        modelBuilder.Entity<Sprint>(entity =>
        {
            entity.ToTable("Sprint", Schema);
            entity.Property(sprint => sprint.Name).HasMaxLength(100).IsRequired();
            entity.Property(sprint => sprint.Goal).HasMaxLength(1000);
            entity.Property(sprint => sprint.ReviewNotes).HasMaxLength(4000);
            entity.Property(sprint => sprint.RetrospectiveNotes).HasMaxLength(4000);
            entity.Property(sprint => sprint.Status).HasConversion<byte>();
            OwnedByProject(entity, sprint => sprint.ProjectId, "Sprint");

            // At most one sprint running per project at a time.
            entity.HasIndex(sprint => new { sprint.TenantId, sprint.ProjectId }, "UX_Sprint_TenantId_Project_Active")
                .IsUnique()
                .HasFilter("[Status] = 2 AND [IsDeleted] = 0");
            entity.Ignore(sprint => sprint.IsOpen);
            entity.Ignore(sprint => sprint.DomainEvents);
        });

        modelBuilder.Entity<Requirement>(entity =>
        {
            entity.ToTable("Requirement", Schema);
            entity.Property(requirement => requirement.Code).HasMaxLength(16).IsRequired();
            entity.Property(requirement => requirement.Title).HasMaxLength(300).IsRequired();
            entity.Property(requirement => requirement.Description).HasMaxLength(8000);
            entity.Property(requirement => requirement.Source).HasMaxLength(200);
            entity.Property(requirement => requirement.AcceptanceCriteria).HasMaxLength(4000);
            entity.Property(requirement => requirement.Type).HasConversion<byte>();
            entity.Property(requirement => requirement.Priority).HasConversion<byte>();
            entity.Property(requirement => requirement.Status).HasConversion<byte>();
            OwnedByProject(entity, requirement => requirement.ProjectId, "Requirement");
            entity.Ignore(requirement => requirement.DomainEvents);
        });

        modelBuilder.Entity<ChangeRequest>(entity =>
        {
            entity.ToTable("ChangeRequest", Schema);
            entity.Property(request => request.Code).HasMaxLength(16).IsRequired();
            entity.Property(request => request.Title).HasMaxLength(300).IsRequired();
            entity.Property(request => request.Description).HasMaxLength(8000).IsRequired();
            entity.Property(request => request.Reason).HasMaxLength(2000);
            entity.Property(request => request.Impact).HasMaxLength(4000);
            entity.Property(request => request.RequestedByName).HasMaxLength(200);
            entity.Property(request => request.ClientApprovalReference).HasMaxLength(200);
            entity.Property(request => request.EstimatedHours).HasPrecision(9, 2);
            entity.Property(request => request.EstimatedCost).HasPrecision(18, 2);
            entity.Property(request => request.Priority).HasConversion<byte>();
            entity.Property(request => request.Status).HasConversion<byte>();
            OwnedByProject(entity, request => request.ProjectId, "ChangeRequest");
            entity.Ignore(request => request.AwaitsDecision);
            entity.Ignore(request => request.IsAccepted);
            entity.Ignore(request => request.DomainEvents);
        });

        modelBuilder.Entity<RaidItem>(entity =>
        {
            entity.ToTable("RaidItem", Schema);
            entity.Property(item => item.Code).HasMaxLength(16).IsRequired();
            entity.Property(item => item.Title).HasMaxLength(300).IsRequired();
            entity.Property(item => item.Description).HasMaxLength(4000);
            entity.Property(item => item.Category).HasMaxLength(100);
            entity.Property(item => item.Mitigation).HasMaxLength(4000);
            entity.Property(item => item.ContingencyPlan).HasMaxLength(4000);
            entity.Property(item => item.RootCause).HasMaxLength(4000);
            entity.Property(item => item.Resolution).HasMaxLength(4000);
            entity.Property(item => item.Kind).HasConversion<byte>();
            entity.Property(item => item.Severity).HasConversion<byte>();
            entity.Property(item => item.Priority).HasConversion<byte>();
            entity.Property(item => item.Status).HasConversion<byte>();
            OwnedByProject(entity, item => item.ProjectId, "RaidItem");
            entity.HasIndex(item => new { item.TenantId, item.Kind, item.Status }).HasDatabaseName("IX_RaidItem_TenantId_Kind_Status");
            entity.Ignore(item => item.IsOpen);
            entity.Ignore(item => item.DomainEvents);
        });

        modelBuilder.Entity<ProjectMeeting>(entity =>
        {
            entity.ToTable("ProjectMeeting", Schema);
            entity.Property(meeting => meeting.Title).HasMaxLength(200).IsRequired();
            entity.Property(meeting => meeting.Location).HasMaxLength(300);
            entity.Property(meeting => meeting.ParticipantIdsJson).IsRequired();
            entity.Property(meeting => meeting.ExternalParticipants).HasMaxLength(1000);
            entity.Property(meeting => meeting.Agenda).HasMaxLength(8000);
            entity.Property(meeting => meeting.Minutes);
            entity.Property(meeting => meeting.Type).HasConversion<byte>();
            entity.Property(meeting => meeting.Status).HasConversion<byte>();
            OwnedByProject(entity, meeting => meeting.ProjectId, "ProjectMeeting");
            entity.Ignore(meeting => meeting.ParticipantEmployeeIds);
            entity.Ignore(meeting => meeting.DomainEvents);
        });

        modelBuilder.Entity<MeetingAction>(entity =>
        {
            entity.ToTable("MeetingAction", Schema);
            entity.Property(action => action.Description).HasMaxLength(1000).IsRequired();
            entity.Property(action => action.Status).HasConversion<byte>();
            entity.HasOne<ProjectMeeting>().WithMany().HasForeignKey(action => action.MeetingId).OnDelete(DeleteBehavior.Restrict);
            OwnedByProject(entity, action => action.ProjectId, "MeetingAction");
            entity.Ignore(action => action.DomainEvents);
        });

        modelBuilder.Entity<ProjectHealthSnapshot>(entity =>
        {
            entity.ToTable("ProjectHealthSnapshot", Schema);
            entity.Property(snapshot => snapshot.Summary).HasMaxLength(4000).IsRequired();
            entity.Property(snapshot => snapshot.Score).HasPrecision(5, 2);
            entity.Property(snapshot => snapshot.ProgressPercent).HasPrecision(5, 1);
            foreach (string rating in HealthDimensions)
            {
                entity.Property<HealthRating>(rating).HasConversion<byte>();
            }

            entity.HasOne<ProjectEntity>().WithMany().HasForeignKey(snapshot => snapshot.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(snapshot => new { snapshot.TenantId, snapshot.ProjectId, snapshot.AssessedOn })
                .HasDatabaseName("UX_ProjectHealthSnapshot_TenantId_Project_Date")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(snapshot => snapshot.DomainEvents);
        });

        modelBuilder.Entity<ProjectHealthSettings>(entity =>
        {
            entity.ToTable("ProjectHealthSettings", Schema);
            entity.Property(settings => settings.OverallAmberScore).HasPrecision(4, 2);
            entity.Property(settings => settings.OverallRedScore).HasPrecision(4, 2);
            entity.HasIndex(settings => settings.TenantId)
                .HasDatabaseName("UX_ProjectHealthSettings_TenantId")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.Ignore(settings => settings.DomainEvents);
        });
    }

    /// <summary>A child table of a project: a foreign key to it and an index to find its rows.</summary>
    private static void OwnedByProject<T>(EntityTypeBuilder<T> entity, System.Linq.Expressions.Expression<Func<T, object?>> projectId, string table)
        where T : class
    {
        entity.HasOne<ProjectEntity>().WithMany().HasForeignKey(projectId).OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(projectId).HasDatabaseName($"IX_{table}_ProjectId");
    }
}

internal static class ProjectData
{
    extension(AppDbContext db)
    {
        public DbSet<ProjectEntity> Projects => db.Set<ProjectEntity>();

        public DbSet<ProjectStatusHistory> ProjectStatusHistory => db.Set<ProjectStatusHistory>();

        public DbSet<ProjectRole> ProjectRoles => db.Set<ProjectRole>();

        public DbSet<ProjectTeamMember> ProjectTeamMembers => db.Set<ProjectTeamMember>();

        public DbSet<ProjectPhase> ProjectPhases => db.Set<ProjectPhase>();

        public DbSet<BoardColumn> BoardColumns => db.Set<BoardColumn>();

        public DbSet<ProjectTask> ProjectTasks => db.Set<ProjectTask>();

        public DbSet<TaskDependency> TaskDependencies => db.Set<TaskDependency>();

        public DbSet<ProjectMilestone> ProjectMilestones => db.Set<ProjectMilestone>();

        public DbSet<Sprint> Sprints => db.Set<Sprint>();

        public DbSet<Requirement> Requirements => db.Set<Requirement>();

        public DbSet<ChangeRequest> ChangeRequests => db.Set<ChangeRequest>();

        public DbSet<RaidItem> RaidItems => db.Set<RaidItem>();

        public DbSet<ProjectMeeting> ProjectMeetings => db.Set<ProjectMeeting>();

        public DbSet<MeetingAction> MeetingActions => db.Set<MeetingAction>();

        public DbSet<ProjectHealthSnapshot> ProjectHealthSnapshots => db.Set<ProjectHealthSnapshot>();

        public DbSet<ProjectHealthSettings> ProjectHealthSettings => db.Set<ProjectHealthSettings>();
    }
}
