using Gggs.Data;
using Microsoft.EntityFrameworkCore;
using ContractEntity = Gggs.Modules.Contract.Domain.Contract;

namespace Gggs.Modules.Contract.Infrastructure;

/// <summary>The Contract module's tables, in the <c>ctr</c> schema.</summary>
internal sealed class ContractModel : IModuleModel
{
    public const string Schema = "ctr";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<ContractEntity>(entity =>
        {
            entity.ToTable("Contract", Schema);
            entity.Property(contract => contract.Number).HasMaxLength(40).IsRequired();
            entity.Property(contract => contract.Title).HasMaxLength(200).IsRequired();
            entity.Property(contract => contract.Currency).HasMaxLength(8).IsRequired();
            entity.Property(contract => contract.Value).HasPrecision(18, 2);
            entity.Property(contract => contract.PaymentTerms).HasMaxLength(500);
            entity.Property(contract => contract.TaxTerms).HasMaxLength(500);
            entity.Property(contract => contract.Notes).HasMaxLength(2000);
            entity.Property(contract => contract.TerminationReason).HasMaxLength(500);
            entity.Property(contract => contract.Type).HasConversion<byte>();
            entity.Property(contract => contract.BillingModel).HasConversion<byte>();
            entity.Property(contract => contract.Status).HasConversion<byte>();
            entity.HasOne<ContractEntity>().WithMany().HasForeignKey(contract => contract.ParentContractId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(contract => new { contract.TenantId, contract.Number })
                .HasDatabaseName("UX_Contract_TenantId_Number")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.HasIndex(contract => new { contract.TenantId, contract.ClientId }).HasDatabaseName("IX_Contract_TenantId_Client");
            entity.HasIndex(contract => new { contract.TenantId, contract.Status, contract.EndDate }).HasDatabaseName("IX_Contract_TenantId_Status_EndDate");
            entity.Ignore(contract => contract.RenewalDue);
            entity.Ignore(contract => contract.DomainEvents);
        });
    }
}

internal static class ContractData
{
    extension(AppDbContext db)
    {
        public DbSet<ContractEntity> Contracts => db.Set<ContractEntity>();
    }
}
