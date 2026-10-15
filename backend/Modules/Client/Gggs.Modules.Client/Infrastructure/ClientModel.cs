using Gggs.Data;
using Gggs.Modules.Client.Domain;
using Microsoft.EntityFrameworkCore;
using ClientEntity = Gggs.Modules.Client.Domain.Client;

namespace Gggs.Modules.Client.Infrastructure;

/// <summary>The Client module's tables, in the <c>crm</c> schema.</summary>
internal sealed class ClientModel : IModuleModel
{
    public const string Schema = "crm";

    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<ClientEntity>(entity =>
        {
            entity.ToTable("Client", Schema);
            entity.Property(client => client.Code).HasMaxLength(32).IsRequired();
            entity.Property(client => client.Name).HasMaxLength(200).IsRequired();
            entity.Property(client => client.LegalName).HasMaxLength(200);
            entity.Property(client => client.IndustryCode).HasMaxLength(64);
            entity.Property(client => client.CountryCode).HasMaxLength(64);
            entity.Property(client => client.StateCode).HasMaxLength(64);
            entity.Property(client => client.City).HasMaxLength(100);
            entity.Property(client => client.Address).HasMaxLength(500);
            entity.Property(client => client.PostalCode).HasMaxLength(20);
            entity.Property(client => client.Website).HasMaxLength(200);
            entity.Property(client => client.Email).HasMaxLength(256);
            entity.Property(client => client.Phone).HasMaxLength(32);
            entity.Property(client => client.TaxId).HasMaxLength(32);
            entity.Property(client => client.Notes).HasMaxLength(2000);
            entity.Property(client => client.Status).HasConversion<byte>();
            entity.HasIndex(client => new { client.TenantId, client.Code })
                .HasDatabaseName("UX_Client_TenantId_Code")
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            entity.HasIndex(client => new { client.TenantId, client.Name }).HasDatabaseName("IX_Client_TenantId_Name");
            entity.Ignore(client => client.CanTakeWork);
            entity.Ignore(client => client.DomainEvents);
        });

        modelBuilder.Entity<ClientContact>(entity =>
        {
            entity.ToTable("ClientContact", Schema);
            entity.Property(contact => contact.Name).HasMaxLength(200).IsRequired();
            entity.Property(contact => contact.Designation).HasMaxLength(100);
            entity.Property(contact => contact.Email).HasMaxLength(256);
            entity.Property(contact => contact.Phone).HasMaxLength(32);
            entity.Property(contact => contact.Department).HasMaxLength(100);
            entity.Property(contact => contact.Notes).HasMaxLength(1000);
            entity.HasOne<ClientEntity>().WithMany().HasForeignKey(contact => contact.ClientId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(contact => new { contact.TenantId, contact.ClientId }).HasDatabaseName("IX_ClientContact_TenantId_Client");
            entity.HasIndex(contact => new { contact.TenantId, contact.ClientId }, "UX_ClientContact_TenantId_Client_Primary")
                .IsUnique()
                .HasFilter("[IsPrimary] = 1 AND [IsDeleted] = 0");
            entity.Ignore(contact => contact.DomainEvents);
        });
    }
}

internal static class ClientData
{
    extension(AppDbContext db)
    {
        public DbSet<ClientEntity> Clients => db.Set<ClientEntity>();

        public DbSet<ClientContact> ClientContacts => db.Set<ClientContact>();
    }
}
