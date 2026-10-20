using Gggs.Data;
using Gggs.Modules.Ai.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Ai.Infrastructure;

/// <summary>The AI module's tables, in the <c>ai</c> schema.</summary>
internal sealed class AiModel : IModuleModel
{
    public const string Schema = "ai";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<AiConversation>(entity =>
        {
            entity.ToTable("Conversation", Schema);
            entity.Property(row => row.AssistantKey).HasMaxLength(40);
            entity.Property(row => row.Title).HasMaxLength(120);
            entity.HasIndex(row => new { row.TenantId, row.UserId, row.LastActivityAt }).HasDatabaseName("IX_Conversation_TenantId_User_Activity");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<AiMessage>(entity =>
        {
            entity.ToTable("Message", Schema);
            entity.Property(row => row.Role).HasMaxLength(10);
            entity.Property(row => row.BlocksJson).IsRequired();
            entity.HasOne<AiConversation>().WithMany().HasForeignKey(row => row.ConversationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(row => new { row.TenantId, row.ConversationId, row.Sequence }).HasDatabaseName("UX_Message_TenantId_Conversation_Sequence").IsUnique().HasFilter("[IsDeleted] = 0");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<AiToolCall>(entity =>
        {
            entity.ToTable("ToolCall", Schema);
            entity.Property(row => row.ToolName).HasMaxLength(64);
            entity.HasOne<AiConversation>().WithMany().HasForeignKey(row => row.ConversationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(row => new { row.TenantId, row.ConversationId }).HasDatabaseName("IX_ToolCall_TenantId_Conversation");
            entity.Ignore(row => row.DomainEvents);
        });

        modelBuilder.Entity<AiAction>(entity =>
        {
            entity.ToTable("Action", Schema);
            entity.Property(row => row.ToolName).HasMaxLength(64);
            entity.Property(row => row.Summary).HasMaxLength(1000);
            entity.Property(row => row.PayloadJson).IsRequired();
            entity.Property(row => row.Status).HasConversion<byte>();
            entity.Property(row => row.Result).HasMaxLength(1000);
            entity.HasOne<AiConversation>().WithMany().HasForeignKey(row => row.ConversationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(row => new { row.TenantId, row.UserId, row.Status }).HasDatabaseName("IX_Action_TenantId_User_Status");
            entity.Ignore(row => row.DomainEvents);
        });
    }
}

internal static class AiData
{
    extension(AppDbContext db)
    {
        public DbSet<AiConversation> AiConversations => db.Set<AiConversation>();

        public DbSet<AiMessage> AiMessages => db.Set<AiMessage>();

        public DbSet<AiToolCall> AiToolCalls => db.Set<AiToolCall>();

        public DbSet<AiAction> AiActions => db.Set<AiAction>();
    }
}
