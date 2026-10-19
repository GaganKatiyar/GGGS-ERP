using Gggs.Data;
using Gggs.Modules.Helpdesk.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Helpdesk.Infrastructure;

/// <summary>The Helpdesk module's tables, in the <c>hd</c> schema.</summary>
internal sealed class HelpdeskModel : IModuleModel
{
    public const string Schema = "hd";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<HelpdeskCategory>(entity =>
        {
            entity.ToTable("Category", Schema);
            entity.Property(row => row.Code).HasMaxLength(30);
            entity.Property(row => row.Name).HasMaxLength(100);
            entity.Property(row => row.Description).HasMaxLength(500);
            entity.HasIndex(row => new { row.TenantId, row.Code }).HasDatabaseName("UX_Category_TenantId_Code").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<Ticket>(entity =>
        {
            entity.ToTable("Ticket", Schema);
            entity.Property(row => row.Number).HasMaxLength(30);
            entity.Property(row => row.Subject).HasMaxLength(200);
            entity.Property(row => row.Description).HasMaxLength(4000);
            entity.Property(row => row.Priority).HasConversion<byte>();
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.Resolution).HasMaxLength(2000);
            entity.Property(row => row.SatisfactionComment).HasMaxLength(500);
            entity.HasOne<HelpdeskCategory>().WithMany().HasForeignKey(row => row.CategoryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.Number }).HasDatabaseName("UX_Ticket_TenantId_Number").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasIndex(row => new { row.TenantId, row.Status }).HasDatabaseName("IX_Ticket_TenantId_Status");
            entity.HasIndex(row => new { row.TenantId, row.RequesterEmployeeId }).HasDatabaseName("IX_Ticket_TenantId_Requester");
            entity.HasIndex(row => new { row.TenantId, row.AssigneeEmployeeId }).HasDatabaseName("IX_Ticket_TenantId_Assignee");
            entity.Ignore(row => row.IsOpen);
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<TicketComment>(entity =>
        {
            entity.ToTable("TicketComment", Schema);
            entity.Property(row => row.Body).HasMaxLength(4000);
            entity.HasOne<Ticket>().WithMany().HasForeignKey(row => row.TicketId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.TicketId }).HasDatabaseName("IX_TicketComment_TenantId_Ticket");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<KnowledgeArticle>(entity =>
        {
            entity.ToTable("KnowledgeArticle", Schema);
            entity.Property(row => row.Title).HasMaxLength(200);
            entity.Property(row => row.Body).HasMaxLength(20000);
            entity.Property(row => row.Tags).HasMaxLength(300);
            entity.HasOne<HelpdeskCategory>().WithMany().HasForeignKey(row => row.CategoryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.TenantId, row.IsPublished }).HasDatabaseName("IX_KnowledgeArticle_TenantId_Published");
            entity.Ignore(row => row.DomainEvents);
        });
    }
}

internal static class HelpdeskData
{
    extension(AppDbContext db)
    {
        public DbSet<HelpdeskCategory> HelpdeskCategories => db.Set<HelpdeskCategory>();

        public DbSet<Ticket> Tickets => db.Set<Ticket>();

        public DbSet<TicketComment> TicketComments => db.Set<TicketComment>();

        public DbSet<KnowledgeArticle> KnowledgeArticles => db.Set<KnowledgeArticle>();
    }
}
