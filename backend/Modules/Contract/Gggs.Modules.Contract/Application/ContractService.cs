using System.Globalization;
using System.Linq.Expressions;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Data;
using Gggs.Modules.Client.Contracts;
using Gggs.Modules.Contract.Contracts;
using Gggs.Modules.Contract.Domain;
using Gggs.Modules.Contract.Infrastructure;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.MasterData.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Workflow.Contracts;
using Microsoft.EntityFrameworkCore;
using ContractEntity = Gggs.Modules.Contract.Domain.Contract;

namespace Gggs.Modules.Contract.Application;

public sealed record ContractListQuery
{
    public string? Search { get; init; }

    public Guid? ClientId { get; init; }

    public ContractType? Type { get; init; }

    public ContractStatus? Status { get; init; }

    /// <summary>Active contracts whose renewal (or end) falls within this many days.</summary>
    public int? RenewalWithinDays { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public string? Sort { get; init; }

    public PageRequest ToPageRequest() => new() { Page = Page ?? 1, PageSize = PageSize ?? PageRequest.DefaultPageSize, Sort = Sort, Search = Search };
}

public sealed record ContractItem(
    Guid Id,
    string Number,
    Guid ClientId,
    string? ClientName,
    string Title,
    ContractType Type,
    Guid? ParentContractId,
    string? ParentNumber,
    DateOnly StartDate,
    DateOnly? EndDate,
    string Currency,
    decimal? Value,
    BillingModel BillingModel,
    int? PaymentTermsDays,
    string? PaymentTerms,
    string? TaxTerms,
    DateOnly? RenewalDate,
    bool AutoRenew,
    Guid? OwnerEmployeeId,
    string? OwnerName,
    Guid? ClientContactId,
    string? Notes,
    ContractStatus Status,
    Guid? WorkflowInstanceId,
    DateOnly? TerminatedOn,
    string? TerminationReason,
    int? DaysToRenewal);

public sealed record ContractDetail(ContractItem Contract, IReadOnlyList<ContractItem> Children);

public sealed record ContractRequest(
    string? Number,
    Guid ClientId,
    string Title,
    ContractType Type,
    Guid? ParentContractId,
    DateOnly StartDate,
    DateOnly? EndDate,
    string Currency,
    decimal? Value,
    BillingModel BillingModel,
    int? PaymentTermsDays,
    string? PaymentTerms,
    string? TaxTerms,
    DateOnly? RenewalDate,
    bool AutoRenew,
    Guid? OwnerEmployeeId,
    Guid? ClientContactId,
    string? Notes);

public sealed record TerminateContractBody(DateOnly On, string Reason);

public sealed record ActivationResult(ContractStatus Status, Guid? WorkflowInstanceId);

/// <summary>
/// Contracts (spec §38): drafting, activation - through approval when the tenant has a workflow
/// for <see cref="SubjectType"/> - amendment, renewal, termination and expiry.
/// </summary>
internal sealed class ContractService(
    AppDbContext dbContext,
    IClientDirectory clients,
    IEmployeeDirectory employees,
    IMasterDataLookup masterData,
    IWorkflowStarter workflow,
    INotifier notifier,
    ICurrentUser currentUser,
    ITenantClock clock,
    TimeProvider timeProvider)
{
    public const string SubjectType = "contract.activation";

    /// <summary>How far ahead of renewal the owner is reminded.</summary>
    public const int RenewalLeadDays = 30;

    private static readonly Dictionary<string, Expression<Func<ContractEntity, object?>>> Sortable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["number"] = contract => contract.Number,
        ["title"] = contract => contract.Title,
        ["startDate"] = contract => contract.StartDate,
        ["endDate"] = contract => contract.EndDate,
        ["value"] = contract => contract.Value,
        ["status"] = contract => contract.Status,
    };

    public static WorkflowSubject Subject { get; } = new(
        SubjectType,
        "Activate a contract",
        "contracts",
        [
            new WorkflowSubjectField("value", "Contract value", WorkflowFieldType.Number),
            new WorkflowSubjectField("type", "Contract type", WorkflowFieldType.Text),
            new WorkflowSubjectField("billingModel", "Billing model", WorkflowFieldType.Text),
            new WorkflowSubjectField("currency", "Currency", WorkflowFieldType.Text),
        ]);

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<PagedResult<ContractItem>> ListAsync(ContractListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        PageRequest page = query.ToPageRequest();
        IQueryable<ContractEntity> rows = dbContext.Contracts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(page.Search))
        {
            string term = page.Search.Trim();
            rows = rows.Where(contract => contract.Number.Contains(term) || contract.Title.Contains(term));
        }

        if (query.ClientId is { } clientId)
        {
            rows = rows.Where(contract => contract.ClientId == clientId);
        }

        if (query.Type is { } type)
        {
            rows = rows.Where(contract => contract.Type == type);
        }

        if (query.Status is { } status)
        {
            rows = rows.Where(contract => contract.Status == status);
        }

        if (query.RenewalWithinDays is { } days)
        {
            DateOnly today = await clock.TodayAsync(cancellationToken);
            DateOnly limit = today.AddDays(Math.Clamp(days, 0, 3650));
            rows = rows.Where(contract => contract.Status == ContractStatus.Active
                && ((contract.RenewalDate != null && contract.RenewalDate <= limit) || (contract.RenewalDate == null && contract.EndDate != null && contract.EndDate <= limit)));
        }

        PagedResult<ContractEntity> result = await rows.ApplySort(page.Sort, Sortable, "-startDate").ToPagedResultAsync(page, contract => contract, cancellationToken);
        return PagedResult.From(await ToItemsAsync(result.Items, cancellationToken), page, result.TotalCount);
    }

    public async Task<ContractDetail> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        ContractEntity contract = await FindAsync(id, track: false, cancellationToken);
        List<ContractEntity> children = await dbContext.Contracts.AsNoTracking()
            .Where(child => child.ParentContractId == id)
            .OrderBy(child => child.StartDate)
            .ToListAsync(cancellationToken);

        List<ContractItem> items = await ToItemsAsync([contract, .. children], cancellationToken);
        return new ContractDetail(items[0], [.. items.Skip(1)]);
    }

    public async Task<ContractDetail> CreateAsync(ContractRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ClientSummary client = await clients.GetAsync(request.ClientId, cancellationToken)
            ?? throw new DomainRuleException("CONTRACT_CLIENT_UNKNOWN", "Choose a client for the contract.");

        if (!client.CanTakeWork)
        {
            throw new DomainRuleException("CONTRACT_CLIENT_INACTIVE", $"{client.Name} is inactive and takes no new contracts.");
        }

        string number = string.IsNullOrWhiteSpace(request.Number) ? await NextNumberAsync(request.StartDate, cancellationToken) : request.Number.Trim().ToUpperInvariant();
        if (await dbContext.Contracts.AnyAsync(contract => contract.Number == number, cancellationToken))
        {
            throw new DomainRuleException("CONTRACT_NUMBER_IN_USE", $"Contract number '{number}' is already in use.");
        }

        ContractTerms terms = await ValidateAsync(request, client.Id, cancellationToken);
        var contract = ContractEntity.Draft(number, client.Id, terms);
        dbContext.Contracts.Add(contract);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(contract.Id, cancellationToken);
    }

    public async Task<ContractDetail> UpdateAsync(Guid id, ContractRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ContractEntity contract = await FindAsync(id, track: true, cancellationToken);
        if (request.ClientId != contract.ClientId)
        {
            throw new DomainRuleException("CONTRACT_CLIENT_FIXED", "A contract stays with the client it was drafted for.");
        }

        ContractTerms terms = await ValidateAsync(request, contract.ClientId, cancellationToken, selfId: id);
        contract.Amend(terms, await clock.TodayAsync(cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Activates a draft, or sends it for approval when the tenant has a workflow for activating contracts.</summary>
    public async Task<ActivationResult> ActivateAsync(Guid id, CancellationToken cancellationToken)
    {
        ContractEntity contract = await FindAsync(id, track: true, cancellationToken);
        if (contract.Status != ContractStatus.Draft)
        {
            throw new DomainRuleException("CONTRACT_STATE", $"Only a draft can be activated; this contract is {contract.Status}.");
        }

        // A request sent back for changes is still open in Workflow; activating the edited draft
        // replaces it with a fresh request, saved on its own first so the two never collide.
        await workflow.WithdrawAsync(SubjectType, contract.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        WorkflowStartResult started = await workflow.StartAsync(
            new WorkflowStartRequest
            {
                SubjectType = SubjectType,
                SubjectId = contract.Id,
                Title = $"Activate contract {contract.Number} - {contract.Title}",
                RequesterEmployeeId = currentUser.EmployeeId,
                Link = $"/contracts?open={contract.Id}",
                Fields = new Dictionary<string, string>
                {
                    ["value"] = (contract.Value ?? 0).ToString(CultureInfo.InvariantCulture),
                    ["type"] = contract.Type.ToString(),
                    ["billingModel"] = contract.BillingModel.ToString(),
                    ["currency"] = contract.Currency,
                },
            },
            cancellationToken);

        if (started.Outcome == WorkflowStartOutcome.Pending)
        {
            contract.SubmitForApproval(started.InstanceId!.Value);
        }
        else
        {
            contract.Activate(Now, await clock.TodayAsync(cancellationToken));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new ActivationResult(contract.Status, started.InstanceId);
    }

    public async Task TerminateAsync(Guid id, TerminateContractBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ContractEntity contract = await FindAsync(id, track: true, cancellationToken);
        contract.Terminate(body.On, body.Reason);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Called when approval comes through.</summary>
    public async Task CompleteApprovedAsync(Guid contractId, CancellationToken cancellationToken)
    {
        ContractEntity? contract = await dbContext.Contracts.FirstOrDefaultAsync(row => row.Id == contractId, cancellationToken);
        if (contract?.Status == ContractStatus.PendingApproval)
        {
            contract.Activate(Now, await clock.TodayAsync(cancellationToken));
        }
    }

    /// <summary>Called when approval is refused, sent back or withdrawn.</summary>
    public async Task ReturnToDraftAsync(Guid contractId, CancellationToken cancellationToken)
    {
        ContractEntity? contract = await dbContext.Contracts.FirstOrDefaultAsync(row => row.Id == contractId, cancellationToken);
        contract?.ReturnToDraft();
    }

    /// <summary>
    /// The daily sweep for the current tenant: expires (or rolls on) contracts past their end date and
    /// reminds owners of renewals coming up.
    /// </summary>
    public async Task<(int Expired, int Reminded)> SweepAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        DateOnly horizon = today.AddDays(RenewalLeadDays);

        List<ContractEntity> candidates = await dbContext.Contracts
            .Where(contract => contract.Status == ContractStatus.Active
                && ((contract.EndDate != null && contract.EndDate <= horizon) || (contract.RenewalDate != null && contract.RenewalDate <= horizon)))
            .ToListAsync(cancellationToken);

        int expired = candidates.Count(contract => contract.ExpireIfDue(today));
        IReadOnlyDictionary<Guid, ClientSummary> clientNames = await clients.GetManyAsync(candidates.Select(contract => contract.ClientId), cancellationToken);

        int reminded = 0;
        foreach (ContractEntity contract in candidates.Where(contract => contract.NeedsRenewalReminder(today, RenewalLeadDays)))
        {
            ClientSummary? client = clientNames.GetValueOrDefault(contract.ClientId);
            Guid? recipient = contract.OwnerEmployeeId ?? client?.AccountManagerEmployeeId;
            if (recipient is { } employeeId)
            {
                await notifier.NotifyAsync(
                    new NotificationRequest(
                        NotificationTemplates.ContractRenewalDue,
                        [new NotificationRecipient(employeeId)],
                        new Dictionary<string, string>
                        {
                            ["contractNumber"] = contract.Number,
                            ["contractTitle"] = contract.Title,
                            ["clientName"] = client?.Name ?? string.Empty,
                            ["dueDate"] = contract.RenewalDue!.Value.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
                        },
                        $"/contracts?open={contract.Id}"),
                    cancellationToken);
                reminded++;
            }

            contract.RecordRenewalReminder();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (expired, reminded);
    }

    // ----- Internals ----------------------------------------------------------------------------

    private async Task<string> NextNumberAsync(DateOnly startDate, CancellationToken cancellationToken)
    {
        string prefix = $"CTR-{startDate.Year}-";
        int count = await dbContext.Contracts
            .IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete])
            .CountAsync(contract => contract.Number.StartsWith(prefix), cancellationToken);

        string number;
        do
        {
            count++;
            number = prefix + count.ToString("0000", CultureInfo.InvariantCulture);
        }
        while (await dbContext.Contracts.IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete]).AnyAsync(contract => contract.Number == number, cancellationToken));

        return number;
    }

    private async Task<ContractTerms> ValidateAsync(ContractRequest request, Guid clientId, CancellationToken cancellationToken, Guid? selfId = null)
    {
        if (!await masterData.IsValidAsync(LookupLists.Currency, request.Currency ?? string.Empty, cancellationToken))
        {
            throw new DomainRuleException("CONTRACT_CURRENCY_INVALID", $"'{request.Currency}' is not a currency this organization uses.");
        }

        if (request.ParentContractId is { } parentId)
        {
            ContractEntity parent = await dbContext.Contracts.AsNoTracking().FirstOrDefaultAsync(contract => contract.Id == parentId, cancellationToken)
                ?? throw new DomainRuleException("CONTRACT_PARENT_UNKNOWN", "The parent contract does not exist.");

            if (parent.Id == selfId || parent.Type != ContractType.Msa || parent.ClientId != clientId)
            {
                throw new DomainRuleException("CONTRACT_PARENT", "A contract can only sit under a master services agreement with the same client.");
            }
        }

        if (request.OwnerEmployeeId is { } owner && !await employees.IsActiveAsync(owner, cancellationToken))
        {
            throw new DomainRuleException("CONTRACT_OWNER_INVALID", "The contract owner must be an active employee.");
        }

        return new ContractTerms(
            request.Title, request.Type, request.ParentContractId, request.StartDate, request.EndDate, request.Currency ?? string.Empty, request.Value,
            request.BillingModel, request.PaymentTermsDays, request.PaymentTerms, request.TaxTerms, request.RenewalDate, request.AutoRenew,
            request.OwnerEmployeeId, request.ClientContactId, request.Notes);
    }

    private async Task<ContractEntity> FindAsync(Guid id, bool track, CancellationToken cancellationToken)
    {
        IQueryable<ContractEntity> source = track ? dbContext.Contracts : dbContext.Contracts.AsNoTracking();
        return await source.FirstOrDefaultAsync(contract => contract.Id == id, cancellationToken) ?? throw new NotFoundException("Contract", id);
    }

    private async Task<List<ContractItem>> ToItemsAsync(IReadOnlyList<ContractEntity> contracts, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        IReadOnlyDictionary<Guid, ClientSummary> clientNames = await clients.GetManyAsync(contracts.Select(contract => contract.ClientId), cancellationToken);
        Dictionary<Guid, EmployeeSummary> owners = (await employees.GetManyAsync(
                contracts.Select(contract => contract.OwnerEmployeeId).OfType<Guid>().Distinct(), cancellationToken))
            .ToDictionary(person => person.Id);

        List<Guid> parentIds = [.. contracts.Select(contract => contract.ParentContractId).OfType<Guid>().Distinct()];
        Dictionary<Guid, string> parents = parentIds.Count == 0
            ? []
            : await dbContext.Contracts.AsNoTracking().Where(contract => parentIds.Contains(contract.Id)).ToDictionaryAsync(contract => contract.Id, contract => contract.Number, cancellationToken);

        return [.. contracts.Select(contract => new ContractItem(
            contract.Id, contract.Number, contract.ClientId, clientNames.GetValueOrDefault(contract.ClientId)?.Name, contract.Title, contract.Type,
            contract.ParentContractId, contract.ParentContractId is { } parent ? parents.GetValueOrDefault(parent) : null,
            contract.StartDate, contract.EndDate, contract.Currency, contract.Value, contract.BillingModel, contract.PaymentTermsDays,
            contract.PaymentTerms, contract.TaxTerms, contract.RenewalDate, contract.AutoRenew, contract.OwnerEmployeeId,
            contract.OwnerEmployeeId is { } owner ? owners.GetValueOrDefault(owner)?.DisplayName : null, contract.ClientContactId, contract.Notes,
            contract.Status, contract.WorkflowInstanceId, contract.TerminatedOn, contract.TerminationReason,
            contract.Status == ContractStatus.Active && contract.RenewalDue is { } due ? due.DayNumber - today.DayNumber : null))];
    }
}

/// <summary>Contract lookups for projects.</summary>
internal sealed class ContractDirectory(AppDbContext dbContext) : IContractDirectory
{
    public async Task<IReadOnlyDictionary<Guid, ContractSummary>> GetManyAsync(IEnumerable<Guid> contractIds, CancellationToken cancellationToken = default)
    {
        List<Guid> ids = [.. contractIds.Distinct()];
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, ContractSummary>();
        }

        List<ContractEntity> rows = await dbContext.Contracts.AsNoTracking().Where(contract => ids.Contains(contract.Id)).ToListAsync(cancellationToken);
        return rows.ToDictionary(
            contract => contract.Id,
            contract => new ContractSummary(
                contract.Id, contract.Number, contract.Title, contract.ClientId, contract.Type.ToString(), contract.Status.ToString(),
                contract.BillingModel.ToString(), contract.Currency, contract.Value, contract.StartDate, contract.EndDate));
    }
}
