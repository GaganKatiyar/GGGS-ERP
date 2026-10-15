using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Client.Domain;

public enum ClientStatus
{
    /// <summary>Being pursued: contracts can be drafted, nothing is signed yet.</summary>
    Prospect = 1,
    Active = 2,

    /// <summary>No longer a client. Kept for history; takes no new contracts or projects.</summary>
    Inactive = 3,
}

/// <summary>Everything about a client that its editors set, in one value so create and update agree.</summary>
public sealed record ClientProfile(
    string Name,
    string? LegalName,
    string? IndustryCode,
    string? CountryCode,
    string? StateCode,
    string? City,
    string? Address,
    string? PostalCode,
    string? Website,
    string? Email,
    string? Phone,
    string? TaxId,
    Guid? AccountManagerEmployeeId,
    string? Notes);

/// <summary>A client (spec §37): who the company does work for.</summary>
public sealed class Client : TenantAggregateRoot
{
    private Client()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string? LegalName { get; private set; }

    public string? IndustryCode { get; private set; }

    public string? CountryCode { get; private set; }

    public string? StateCode { get; private set; }

    public string? City { get; private set; }

    public string? Address { get; private set; }

    public string? PostalCode { get; private set; }

    public string? Website { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    /// <summary>GSTIN, VAT or similar registration number, as invoices will need it.</summary>
    public string? TaxId { get; private set; }

    public Guid? AccountManagerEmployeeId { get; private set; }

    public ClientStatus Status { get; private set; }

    public string? Notes { get; private set; }

    public bool CanTakeWork => Status != ClientStatus.Inactive;

    public static Client Create(string code, ClientProfile profile, ClientStatus status)
    {
        var client = new Client { Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 32) };
        client.Update(profile);
        client.SetStatus(status);
        return client;
    }

    public void Update(ClientProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(profile.Name).Trim(), 200);
        LegalName = Clean(profile.LegalName, 200);
        IndustryCode = Clean(profile.IndustryCode, 64);
        CountryCode = Clean(profile.CountryCode, 64);
        StateCode = Clean(profile.StateCode, 64);
        City = Clean(profile.City, 100);
        Address = Clean(profile.Address, 500);
        PostalCode = Clean(profile.PostalCode, 20);
        Website = Clean(profile.Website, 200);
        Email = Clean(profile.Email, 256);
        Phone = Clean(profile.Phone, 32);
        TaxId = Clean(profile.TaxId, 32);
        AccountManagerEmployeeId = profile.AccountManagerEmployeeId;
        Notes = Clean(profile.Notes, 2000);

        if (Email is not null && !Email.Contains('@', StringComparison.Ordinal))
        {
            throw new DomainRuleException("CLIENT_EMAIL_INVALID", "The client email address is not valid.");
        }
    }

    public void SetStatus(ClientStatus status)
        => Status = Enum.IsDefined(status) ? status : throw new DomainRuleException("CLIENT_STATUS", "Unknown client status.");

    internal static string? Clean(string? value, int maxLength)
        => string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim(), maxLength);
}

public sealed record ContactProfile(
    string Name,
    string? Designation,
    string? Email,
    string? Phone,
    string? Department,
    string? Notes);

/// <summary>A person at a client (spec §37). At most one per client is the primary contact.</summary>
public sealed class ClientContact : TenantAggregateRoot
{
    private ClientContact()
    {
        Name = null!;
    }

    public Guid ClientId { get; private set; }

    public string Name { get; private set; }

    public string? Designation { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public string? Department { get; private set; }

    public bool IsPrimary { get; private set; }

    public string? Notes { get; private set; }

    public static ClientContact Create(Guid clientId, ContactProfile profile)
    {
        var contact = new ClientContact { ClientId = Guard.NotEmpty(clientId) };
        contact.Update(profile);
        return contact;
    }

    public void Update(ContactProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(profile.Name).Trim(), 200);
        Designation = Client.Clean(profile.Designation, 100);
        Email = Client.Clean(profile.Email, 256);
        Phone = Client.Clean(profile.Phone, 32);
        Department = Client.Clean(profile.Department, 100);
        Notes = Client.Clean(profile.Notes, 1000);

        if (Email is not null && !Email.Contains('@', StringComparison.Ordinal))
        {
            throw new DomainRuleException("CLIENT_CONTACT_EMAIL_INVALID", "The contact's email address is not valid.");
        }
    }

    public void MakePrimary() => IsPrimary = true;

    public void ClearPrimary() => IsPrimary = false;
}
