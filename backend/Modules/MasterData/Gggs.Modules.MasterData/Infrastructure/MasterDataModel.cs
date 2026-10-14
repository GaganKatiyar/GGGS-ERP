using Gggs.Data;
using Gggs.Modules.MasterData.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.MasterData.Infrastructure;

/// <summary>The MasterData module's table, in the <c>mdm</c> schema.</summary>
internal sealed class MasterDataModel : IModuleModel
{
    public const string Schema = "mdm";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<LookupValue>(entity =>
        {
            entity.ToTable("LookupValue", Schema);
            entity.Property(value => value.ListCode).HasMaxLength(32).IsRequired();
            entity.Property(value => value.Code).HasMaxLength(64).IsRequired();
            entity.Property(value => value.Label).HasMaxLength(200).IsRequired();
            entity.Property(value => value.ParentCode).HasMaxLength(64);

            // A code means one thing per list per tenant, forever: other modules store it.
            entity.HasIndex(value => new { value.TenantId, value.ListCode, value.Code })
                .HasDatabaseName("UX_LookupValue_TenantId_ListCode_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            // The one read path: a list's values in display order, optionally under a parent.
            entity.HasIndex(value => new { value.TenantId, value.ListCode, value.ParentCode, value.SortOrder })
                .HasDatabaseName("IX_LookupValue_TenantId_ListCode_Parent");

            entity.Ignore(value => value.DomainEvents);
        });
    }
}

internal static class MasterDataData
{
    extension(AppDbContext db)
    {
        public DbSet<LookupValue> LookupValues => db.Set<LookupValue>();
    }
}
