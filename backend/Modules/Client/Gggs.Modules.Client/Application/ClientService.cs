using System.Linq.Expressions;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Data;
using Gggs.Modules.Client.Contracts;
using Gggs.Modules.Client.Domain;
using Gggs.Modules.Client.Infrastructure;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.MasterData.Contracts;
using Microsoft.EntityFrameworkCore;
using ClientEntity = Gggs.Modules.Client.Domain.Client;

namespace Gggs.Modules.Client.Application;

public sealed record ClientListQuery
{
    public string? Search { get; init; }

    public ClientStatus? Status { get; init; }

    public string? IndustryCode { get; init; }

    public Guid? AccountManagerEmployeeId { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public string? Sort { get; init; }

    public PageRequest ToPageRequest() => new() { Page = Page ?? 1, PageSize = PageSize ?? PageRequest.DefaultPageSize, Sort = Sort, Search = Search };
}

public sealed record ClientItem(
    Guid Id,
    string Code,
    string Name,
    string? LegalName,
    string? IndustryCode,
    string? IndustryName,
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
    string? AccountManagerName,
    ClientStatus Status,
    string? Notes,
    string? PrimaryContactName,
    string? PrimaryContactEmail,
    int ContactCount,
    DateTime CreatedDate);

public sealed record ContactItem(
    Guid Id,
    Guid ClientId,
    string? ClientName,
    string Name,
    string? Designation,
    string? Email,
    string? Phone,
    string? Department,
    bool IsPrimary,
    string? Notes);

public sealed record ClientDetail(ClientItem Client, IReadOnlyList<ContactItem> Contacts);

public sealed record ClientRequest(
    string Code,
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
    string? Notes,
    ClientStatus? Status,
    ContactBody? PrimaryContact);

public sealed record ContactBody(string Name, string? Designation, string? Email, string? Phone, string? Department, string? Notes, bool? IsPrimary);

public sealed record ClientStatusBody(ClientStatus Status);

public sealed record ContactListQuery
{
    public string? Search { get; init; }

    public Guid? ClientId { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public PageRequest ToPageRequest() => new() { Page = Page ?? 1, PageSize = PageSize ?? PageRequest.DefaultPageSize, Search = Search };
}

/// <summary>Clients and their contacts (spec §37).</summary>
internal sealed class ClientService(AppDbContext dbContext, IEmployeeDirectory employees, IMasterDataLookup masterData)
{
    private static readonly Dictionary<string, Expression<Func<ClientEntity, object?>>> Sortable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = client => client.Name,
        ["code"] = client => client.Code,
        ["status"] = client => client.Status,
        ["city"] = client => client.City,
        ["createdDate"] = client => client.CreatedDate,
    };

    public async Task<PagedResult<ClientItem>> ListAsync(ClientListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        PageRequest page = query.ToPageRequest();
        IQueryable<ClientEntity> rows = dbContext.Clients.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(page.Search))
        {
            string term = page.Search.Trim();
            rows = rows.Where(client => client.Name.Contains(term) || client.Code.Contains(term) || (client.City != null && client.City.Contains(term)));
        }

        if (query.Status is { } status)
        {
            rows = rows.Where(client => client.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.IndustryCode))
        {
            rows = rows.Where(client => client.IndustryCode == query.IndustryCode);
        }

        if (query.AccountManagerEmployeeId is { } manager)
        {
            rows = rows.Where(client => client.AccountManagerEmployeeId == manager);
        }

        PagedResult<ClientEntity> result = await rows.ApplySort(page.Sort, Sortable, "name").ToPagedResultAsync(page, client => client, cancellationToken);
        return PagedResult.From(await ToItemsAsync(result.Items, cancellationToken), page, result.TotalCount);
    }

    public async Task<ClientDetail> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        ClientEntity client = await FindAsync(id, track: false, cancellationToken);
        List<ClientContact> contacts = await dbContext.ClientContacts.AsNoTracking()
            .Where(contact => contact.ClientId == id)
            .OrderByDescending(contact => contact.IsPrimary)
            .ThenBy(contact => contact.Name)
            .ToListAsync(cancellationToken);

        return new ClientDetail((await ToItemsAsync([client], cancellationToken))[0], [.. contacts.Select(contact => ToContact(contact, client.Name))]);
    }

    public async Task<ClientDetail> CreateAsync(ClientRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string code = (request.Code ?? string.Empty).Trim().ToUpperInvariant();

        if (await dbContext.Clients.AnyAsync(client => client.Code == code, cancellationToken))
        {
            throw new DomainRuleException("CLIENT_CODE_IN_USE", $"A client with code '{code}' already exists.");
        }

        ClientProfile profile = await ValidateAsync(request, cancellationToken);
        var client = ClientEntity.Create(code, profile, request.Status ?? ClientStatus.Active);
        dbContext.Clients.Add(client);

        if (request.PrimaryContact is { } contactBody)
        {
            var contact = ClientContact.Create(client.Id, ToProfile(contactBody));
            contact.MakePrimary();
            dbContext.ClientContacts.Add(contact);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(client.Id, cancellationToken);
    }

    public async Task<ClientDetail> UpdateAsync(Guid id, ClientRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ClientEntity client = await FindAsync(id, track: true, cancellationToken);
        client.Update(await ValidateAsync(request, cancellationToken));
        if (request.Status is { } status)
        {
            client.SetStatus(status);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task SetStatusAsync(Guid id, ClientStatus status, CancellationToken cancellationToken)
    {
        ClientEntity client = await FindAsync(id, track: true, cancellationToken);
        client.SetStatus(status);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- Contacts -----------------------------------------------------------------------------

    public async Task<PagedResult<ContactItem>> ContactsAsync(ContactListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        PageRequest page = query.ToPageRequest();

        var rows = dbContext.ClientContacts.AsNoTracking()
            .Join(dbContext.Clients.AsNoTracking(), contact => contact.ClientId, client => client.Id, (contact, client) => new { contact, ClientName = client.Name });

        if (query.ClientId is { } clientId)
        {
            rows = rows.Where(row => row.contact.ClientId == clientId);
        }

        if (!string.IsNullOrWhiteSpace(page.Search))
        {
            string term = page.Search.Trim();
            rows = rows.Where(row => row.contact.Name.Contains(term)
                || (row.contact.Email != null && row.contact.Email.Contains(term))
                || row.ClientName.Contains(term));
        }

        return await rows
            .OrderBy(row => row.ClientName)
            .ThenByDescending(row => row.contact.IsPrimary)
            .ThenBy(row => row.contact.Name)
            .ToPagedResultAsync(page, row => new ContactItem(
                row.contact.Id, row.contact.ClientId, row.ClientName, row.contact.Name, row.contact.Designation, row.contact.Email,
                row.contact.Phone, row.contact.Department, row.contact.IsPrimary, row.contact.Notes), cancellationToken);
    }

    public async Task<ContactItem> AddContactAsync(Guid clientId, ContactBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ClientEntity client = await FindAsync(clientId, track: false, cancellationToken);

        var contact = ClientContact.Create(clientId, ToProfile(body));
        bool hasPrimary = await dbContext.ClientContacts.AnyAsync(row => row.ClientId == clientId && row.IsPrimary, cancellationToken);
        if (body.IsPrimary == true && hasPrimary)
        {
            await ClearPrimaryAsync(clientId, cancellationToken);
        }

        if (body.IsPrimary == true || !hasPrimary)
        {
            contact.MakePrimary();
        }

        dbContext.ClientContacts.Add(contact);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToContact(contact, client.Name);
    }

    public async Task<ContactItem> UpdateContactAsync(Guid clientId, Guid contactId, ContactBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ClientEntity client = await FindAsync(clientId, track: false, cancellationToken);
        ClientContact contact = await FindContactAsync(clientId, contactId, cancellationToken);
        contact.Update(ToProfile(body));

        if (body.IsPrimary == true && !contact.IsPrimary)
        {
            await ClearPrimaryAsync(clientId, cancellationToken);
            contact.MakePrimary();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToContact(contact, client.Name);
    }

    public async Task RemoveContactAsync(Guid clientId, Guid contactId, CancellationToken cancellationToken)
    {
        ClientContact contact = await FindContactAsync(clientId, contactId, cancellationToken);
        bool wasPrimary = contact.IsPrimary;
        contact.ClearPrimary();
        dbContext.ClientContacts.Remove(contact);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (wasPrimary)
        {
            // The longest-standing remaining contact steps up, so a client with contacts always has a primary.
            ClientContact? next = await dbContext.ClientContacts
                .Where(row => row.ClientId == clientId)
                .OrderBy(row => row.CreatedDate)
                .FirstOrDefaultAsync(cancellationToken);
            if (next is not null)
            {
                next.MakePrimary();
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
    }

    // ----- Internals ----------------------------------------------------------------------------

    /// <summary>Clears the current primary in its own save: the filtered unique index allows one at a time.</summary>
    private async Task ClearPrimaryAsync(Guid clientId, CancellationToken cancellationToken)
    {
        List<ClientContact> primaries = await dbContext.ClientContacts.Where(row => row.ClientId == clientId && row.IsPrimary).ToListAsync(cancellationToken);
        foreach (ClientContact primary in primaries)
        {
            primary.ClearPrimary();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ClientProfile> ValidateAsync(ClientRequest request, CancellationToken cancellationToken)
    {
        await CheckCodeAsync(LookupLists.Industry, request.IndustryCode, "CLIENT_INDUSTRY_INVALID", "industry", cancellationToken);
        await CheckCodeAsync(LookupLists.Country, request.CountryCode, "CLIENT_COUNTRY_INVALID", "country", cancellationToken);
        await CheckCodeAsync(LookupLists.State, request.StateCode, "CLIENT_STATE_INVALID", "state", cancellationToken);

        if (request.AccountManagerEmployeeId is { } manager && !await employees.IsActiveAsync(manager, cancellationToken))
        {
            throw new DomainRuleException("CLIENT_ACCOUNT_MANAGER_INVALID", "The account manager must be an active employee.");
        }

        return new ClientProfile(
            request.Name, request.LegalName, request.IndustryCode, request.CountryCode, request.StateCode, request.City, request.Address,
            request.PostalCode, request.Website, request.Email, request.Phone, request.TaxId, request.AccountManagerEmployeeId, request.Notes);
    }

    private async Task CheckCodeAsync(string list, string? code, string errorCode, string label, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(code) && !await masterData.IsValidAsync(list, code, cancellationToken))
        {
            throw new DomainRuleException(errorCode, $"'{code}' is not a {label} this organization uses.");
        }
    }

    private static ContactProfile ToProfile(ContactBody body) => new(body.Name, body.Designation, body.Email, body.Phone, body.Department, body.Notes);

    private async Task<ClientEntity> FindAsync(Guid id, bool track, CancellationToken cancellationToken)
    {
        IQueryable<ClientEntity> source = track ? dbContext.Clients : dbContext.Clients.AsNoTracking();
        return await source.FirstOrDefaultAsync(client => client.Id == id, cancellationToken) ?? throw new NotFoundException("Client", id);
    }

    private async Task<ClientContact> FindContactAsync(Guid clientId, Guid contactId, CancellationToken cancellationToken)
        => await dbContext.ClientContacts.FirstOrDefaultAsync(contact => contact.Id == contactId && contact.ClientId == clientId, cancellationToken)
            ?? throw new NotFoundException(nameof(ClientContact), contactId);

    private async Task<List<ClientItem>> ToItemsAsync(IReadOnlyList<ClientEntity> clients, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. clients.Select(client => client.Id)];
        var contacts = await dbContext.ClientContacts.AsNoTracking()
            .Where(contact => ids.Contains(contact.ClientId))
            .Select(contact => new { contact.ClientId, contact.Name, contact.Email, contact.IsPrimary })
            .ToListAsync(cancellationToken);

        Dictionary<Guid, EmployeeSummary> managers = (await employees.GetManyAsync(
                clients.Select(client => client.AccountManagerEmployeeId).OfType<Guid>().Distinct(), cancellationToken))
            .ToDictionary(person => person.Id);
        IReadOnlyDictionary<string, string> industries = await masterData.GetLabelsAsync(
            LookupLists.Industry, clients.Select(client => client.IndustryCode).OfType<string>().Distinct(), cancellationToken);

        return [.. clients.Select(client =>
        {
            var primary = contacts.FirstOrDefault(contact => contact.ClientId == client.Id && contact.IsPrimary);
            return new ClientItem(
                client.Id, client.Code, client.Name, client.LegalName, client.IndustryCode,
                client.IndustryCode is null ? null : industries.GetValueOrDefault(client.IndustryCode),
                client.CountryCode, client.StateCode, client.City, client.Address, client.PostalCode, client.Website, client.Email, client.Phone,
                client.TaxId, client.AccountManagerEmployeeId,
                client.AccountManagerEmployeeId is { } manager ? managers.GetValueOrDefault(manager)?.DisplayName : null,
                client.Status, client.Notes, primary?.Name, primary?.Email,
                contacts.Count(contact => contact.ClientId == client.Id), client.CreatedDate);
        })];
    }

    private static ContactItem ToContact(ClientContact contact, string? clientName)
        => new(contact.Id, contact.ClientId, clientName, contact.Name, contact.Designation, contact.Email, contact.Phone, contact.Department, contact.IsPrimary, contact.Notes);
}

/// <summary>Client lookups for contracts and projects.</summary>
internal sealed class ClientDirectory(AppDbContext dbContext) : IClientDirectory
{
    public async Task<ClientSummary?> GetAsync(Guid clientId, CancellationToken cancellationToken = default)
        => (await GetManyAsync([clientId], cancellationToken)).GetValueOrDefault(clientId);

    public async Task<IReadOnlyDictionary<Guid, ClientSummary>> GetManyAsync(IEnumerable<Guid> clientIds, CancellationToken cancellationToken = default)
    {
        List<Guid> ids = [.. clientIds.Distinct()];
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, ClientSummary>();
        }

        return await dbContext.Clients.AsNoTracking()
            .Where(client => ids.Contains(client.Id))
            .Select(client => new ClientSummary(client.Id, client.Code, client.Name, client.Status != ClientStatus.Inactive, client.AccountManagerEmployeeId))
            .ToDictionaryAsync(client => client.Id, cancellationToken);
    }
}
