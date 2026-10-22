using Gggs.Data;
using Gggs.Modules.Separation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Separation.Infrastructure;

/// <summary>The Separation module's tables, in the <c>sep</c> schema.</summary>
internal sealed class SeparationModel : IModuleModel
{
    public const string Schema = "sep";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<SeparationCase>(entity =>
        {
            entity.ToTable("Case", Schema);
            entity.Property(row => row.Type).HasConversion<byte>();
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.Reason).HasMaxLength(1000);
            entity.Property(row => row.ReasonCode).HasMaxLength(40);
            entity.Property(row => row.SecondaryReasonCode).HasMaxLength(40);
            entity.Property(row => row.RetentionNote).HasMaxLength(1000);
            entity.Property(row => row.HandoverNote).HasMaxLength(2000);
            entity.Property(row => row.NomineeName).HasMaxLength(200);
            entity.Property(row => row.NomineeRelation).HasMaxLength(60);
            entity.Property(row => row.NomineeContact).HasMaxLength(200);
            entity.Ignore(row => row.AwaitingConfirmation);
            entity.HasIndex(row => new { row.TenantId, row.EmployeeId }).HasDatabaseName("IX_Case_TenantId_Employee");
            entity.HasIndex(row => new { row.TenantId, row.Status, row.LastWorkingDay }).HasDatabaseName("IX_Case_TenantId_Status_LastDay");
            entity.Ignore(row => row.ExitReasonCode);
            entity.Ignore(row => row.ShortfallDays);
            entity.Ignore(row => row.InProgress);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<ClearanceItem>(entity =>
        {
            entity.ToTable("ClearanceItem", Schema);
            entity.Property(row => row.Area).HasConversion<byte>();
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.Title).HasMaxLength(200);
            entity.Property(row => row.RecoveryAmount).HasPrecision(18, 2);
            entity.Property(row => row.Note).HasMaxLength(500);
            entity.Property(row => row.SourceType).HasMaxLength(40);
            entity.HasOne<SeparationCase>().WithMany().HasForeignKey(row => row.CaseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.CaseId }).HasDatabaseName("IX_ClearanceItem_TenantId_Case");
            entity.HasIndex(row => new { row.TenantId, row.OwnerEmployeeId, row.Status }).HasDatabaseName("IX_ClearanceItem_TenantId_Owner_Status");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<ExitInterview>(entity =>
        {
            entity.ToTable("ExitInterview", Schema);
            entity.Property(row => row.PrimaryReason).HasMaxLength(100);
            entity.Property(row => row.WhatWentWell).HasMaxLength(2000);
            entity.Property(row => row.WhatToImprove).HasMaxLength(2000);
            entity.HasOne<SeparationCase>().WithMany().HasForeignKey(row => row.CaseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.CaseId }).HasDatabaseName("UX_ExitInterview_TenantId_Case").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<Settlement>(entity =>
        {
            entity.ToTable("Settlement", Schema);
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.Earnings).HasPrecision(18, 2);
            entity.Property(row => row.Deductions).HasPrecision(18, 2);
            entity.Property(row => row.Net).HasPrecision(18, 2);
            entity.Property(row => row.PaymentReference).HasMaxLength(100);
            entity.HasOne<SeparationCase>().WithMany().HasForeignKey(row => row.CaseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.CaseId }).HasDatabaseName("UX_Settlement_TenantId_Case").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<SettlementLine>(entity =>
        {
            entity.ToTable("SettlementLine", Schema);
            entity.Property(row => row.Code).HasMaxLength(30);
            entity.Property(row => row.Label).HasMaxLength(200);
            entity.Property(row => row.Amount).HasPrecision(18, 2);
            entity.Property(row => row.Basis).HasMaxLength(300);
            entity.HasOne<Settlement>().WithMany().HasForeignKey(row => row.SettlementId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.SettlementId }).HasDatabaseName("IX_SettlementLine_TenantId_Settlement");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<SeparationPolicy>(entity =>
        {
            entity.ToTable("Policy", Schema);
            entity.Property(row => row.BlockedLeaveTypeCodes).HasMaxLength(400);
            entity.Property(row => row.EncashableLeaveTypeCodes).HasMaxLength(400);
            entity.HasIndex(row => row.TenantId).HasDatabaseName("UX_Policy_TenantId").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.Ignore(row => row.BlockedLeaveTypes);
            entity.Ignore(row => row.EncashableLeaveTypes);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<NoticeRule>(entity =>
        {
            entity.ToTable("NoticeRule", Schema);
            entity.Property(row => row.EmploymentTypeCode).HasMaxLength(40);
            entity.HasIndex(row => row.TenantId).HasDatabaseName("IX_NoticeRule_TenantId");
            entity.Ignore(row => row.Specificity);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<CaseEvent>(entity =>
        {
            entity.ToTable("CaseEvent", Schema);
            entity.Property(row => row.Kind).HasMaxLength(60);
            entity.Property(row => row.Title).HasMaxLength(200);
            entity.Property(row => row.Note).HasMaxLength(1000);
            entity.HasOne<SeparationCase>().WithMany().HasForeignKey(row => row.CaseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.CaseId, row.OccurredAt }).HasDatabaseName("IX_CaseEvent_TenantId_Case");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<OffboardingTemplate>(entity =>
        {
            entity.ToTable("OffboardingTemplate", Schema);
            entity.Property(row => row.Name).HasMaxLength(200);
            entity.Property(row => row.Description).HasMaxLength(1000);
            entity.Property(row => row.EmploymentTypeCode).HasMaxLength(64);
            entity.Property(row => row.StepsJson).HasColumnName("Steps");
            entity.HasIndex(row => row.TenantId).HasDatabaseName("IX_OffboardingTemplate_TenantId");
            entity.Ignore(row => row.Steps);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<OffboardingTask>(entity =>
        {
            entity.ToTable("OffboardingTask", Schema);
            entity.Property(row => row.Title).HasMaxLength(200);
            entity.Property(row => row.Description).HasMaxLength(1000);
            entity.Property(row => row.Owner).HasConversion<byte>();
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.Notes).HasMaxLength(1000);
            entity.HasOne<SeparationCase>().WithMany().HasForeignKey(row => row.CaseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.CaseId }).HasDatabaseName("IX_OffboardingTask_TenantId_Case");
            entity.HasIndex(row => new { row.TenantId, row.AssigneeEmployeeId, row.Status }).HasDatabaseName("IX_OffboardingTask_TenantId_Assignee_Status");
            entity.Ignore(row => row.IsOpen);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<HandoverItem>(entity =>
        {
            entity.ToTable("HandoverItem", Schema);
            entity.Property(row => row.Kind).HasConversion<byte>();
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.Title).HasMaxLength(300);
            entity.Property(row => row.Detail).HasMaxLength(2000);
            entity.Property(row => row.Outcome).HasMaxLength(500);
            entity.HasOne<SeparationCase>().WithMany().HasForeignKey(row => row.CaseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.CaseId }).HasDatabaseName("IX_HandoverItem_TenantId_Case");
            entity.Ignore(row => row.IsOpen);
            entity.Ignore(row => row.NeedsRecipient);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<ShowCause>(entity =>
        {
            entity.ToTable("ShowCause", Schema);
            entity.Property(row => row.Allegation).HasMaxLength(2000);
            entity.Property(row => row.Reply).HasMaxLength(4000);
            entity.Property(row => row.EnquiryFindings).HasMaxLength(4000);
            entity.Property(row => row.DecisionNote).HasMaxLength(2000);
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.Outcome).HasConversion<byte>();
            entity.HasIndex(row => new { row.TenantId, row.EmployeeId }).HasDatabaseName("IX_ShowCause_TenantId_Employee");
            entity.HasIndex(row => new { row.TenantId, row.Status }).HasDatabaseName("IX_ShowCause_TenantId_Status");
            entity.Ignore(row => row.IsOpen);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<AbsenceAlert>(entity =>
        {
            entity.ToTable("AbsenceAlert", Schema);
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.Note).HasMaxLength(1000);
            entity.HasIndex(row => new { row.TenantId, row.EmployeeId }).HasDatabaseName("IX_AbsenceAlert_TenantId_Employee");
            entity.HasIndex(row => new { row.TenantId, row.Status }).HasDatabaseName("IX_AbsenceAlert_TenantId_Status");
            entity.Ignore(row => row.IsOpen);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<ExitSurvey>(entity =>
        {
            entity.ToTable("ExitSurvey", Schema);
            entity.Property(row => row.Title).HasMaxLength(200);
            entity.Property(row => row.Introduction).HasMaxLength(1000);
            entity.Property(row => row.QuestionsJson).HasColumnName("Questions");
            entity.HasIndex(row => row.TenantId).HasDatabaseName("UX_ExitSurvey_TenantId").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.Ignore(row => row.Questions);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<ExitSurveyResponse>(entity =>
        {
            entity.ToTable("ExitSurveyResponse", Schema);
            entity.Property(row => row.AnswersJson).HasColumnName("Answers");
            entity.HasOne<SeparationCase>().WithMany().HasForeignKey(row => row.CaseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.CaseId }).HasDatabaseName("UX_ExitSurveyResponse_TenantId_Case").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.Ignore(row => row.Answers);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<AlumniProfile>(entity =>
        {
            entity.ToTable("Alumni", Schema);
            entity.Property(row => row.Name).HasMaxLength(200);
            entity.Property(row => row.Email).HasMaxLength(256);
            entity.Property(row => row.Mobile).HasMaxLength(20);
            entity.Property(row => row.LastDesignation).HasMaxLength(150);
            entity.Property(row => row.SeparationType).HasConversion<byte>();
            entity.Property(row => row.Notes).HasMaxLength(1000);
            entity.HasIndex(row => new { row.TenantId, row.EmployeeId }).HasDatabaseName("UX_Alumni_TenantId_Employee").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.Ignore(row => row.DomainEvents);
        });
    }
}

internal static class SeparationData
{
    extension(AppDbContext db)
    {
        public DbSet<SeparationCase> SeparationCases => db.Set<SeparationCase>();

        public DbSet<ClearanceItem> ClearanceItems => db.Set<ClearanceItem>();

        public DbSet<ExitInterview> ExitInterviews => db.Set<ExitInterview>();

        public DbSet<Settlement> Settlements => db.Set<Settlement>();

        public DbSet<SettlementLine> SettlementLines => db.Set<SettlementLine>();

        public DbSet<AlumniProfile> Alumni => db.Set<AlumniProfile>();

        public DbSet<SeparationPolicy> SeparationPolicies => db.Set<SeparationPolicy>();

        public DbSet<NoticeRule> NoticeRules => db.Set<NoticeRule>();

        public DbSet<CaseEvent> CaseEvents => db.Set<CaseEvent>();

        public DbSet<OffboardingTemplate> OffboardingTemplates => db.Set<OffboardingTemplate>();

        public DbSet<OffboardingTask> OffboardingTasks => db.Set<OffboardingTask>();

        public DbSet<HandoverItem> HandoverItems => db.Set<HandoverItem>();

        public DbSet<ShowCause> ShowCauses => db.Set<ShowCause>();

        public DbSet<AbsenceAlert> AbsenceAlerts => db.Set<AbsenceAlert>();

        public DbSet<ExitSurvey> ExitSurveys => db.Set<ExitSurvey>();

        public DbSet<ExitSurveyResponse> ExitSurveyResponses => db.Set<ExitSurveyResponse>();
    }
}
