using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Contract.Domain;

public enum ContractType
{
    /// <summary>Master services agreement: the umbrella terms that statements of work sit under.</summary>
    Msa = 1,
    Sow = 2,
    PurchaseOrder = 3,
    Sla = 4,
    Retainer = 5,
    TimeAndMaterial = 6,
}

public enum BillingModel
{
    FixedPrice = 1,
    TimeAndMaterial = 2,
    Milestone = 3,
    Retainer = 4,
    NonBillable = 5,
}

public enum ContractStatus
{
    Draft = 1,
    PendingApproval = 2,
    Active = 3,
    Expired = 4,
    Terminated = 5,
}

/// <summary>The negotiated terms of a contract, in one value so create and amend agree.</summary>
public sealed record ContractTerms(
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

/// <summary>
/// A contract with a client (spec §38). Drafted, activated - through approval when the tenant has
/// a workflow for it - and then either runs out, is renewed by moving its dates, or is terminated.
/// </summary>
public sealed class Contract : TenantAggregateRoot
{
    private Contract()
    {
        Number = null!;
        Title = null!;
        Currency = null!;
    }

    public string Number { get; private set; }

    public Guid ClientId { get; private set; }

    public string Title { get; private set; }

    public ContractType Type { get; private set; }

    /// <summary>The MSA a statement of work or purchase order sits under.</summary>
    public Guid? ParentContractId { get; private set; }

    public DateOnly StartDate { get; private set; }

    /// <summary>Null for an open-ended agreement.</summary>
    public DateOnly? EndDate { get; private set; }

    public string Currency { get; private set; }

    public decimal? Value { get; private set; }

    public BillingModel BillingModel { get; private set; }

    public int? PaymentTermsDays { get; private set; }

    public string? PaymentTerms { get; private set; }

    public string? TaxTerms { get; private set; }

    /// <summary>When the renewal conversation is due; reminders go out ahead of it.</summary>
    public DateOnly? RenewalDate { get; private set; }

    public bool AutoRenew { get; private set; }

    public Guid? OwnerEmployeeId { get; private set; }

    public Guid? ClientContactId { get; private set; }

    public string? Notes { get; private set; }

    public ContractStatus Status { get; private set; }

    public Guid? WorkflowInstanceId { get; private set; }

    public DateTime? ActivatedDate { get; private set; }

    public DateOnly? TerminatedOn { get; private set; }

    public string? TerminationReason { get; private set; }

    /// <summary>The renewal date a reminder was last sent for, so each one is sent once.</summary>
    public DateOnly? RenewalReminderFor { get; private set; }

    public static Contract Draft(string number, Guid clientId, ContractTerms terms)
    {
        var contract = new Contract
        {
            Number = Guard.MaxLength(Guard.NotNullOrWhiteSpace(number).Trim().ToUpperInvariant(), 40),
            ClientId = Guard.NotEmpty(clientId),
            Status = ContractStatus.Draft,
        };
        contract.Apply(terms);
        return contract;
    }

    /// <summary>
    /// Changes the terms. A draft changes freely; an active contract is amended - its type and
    /// currency are what it was signed as and stay put. Moving the end date of an expired
    /// contract into the future renews it.
    /// </summary>
    public void Amend(ContractTerms terms, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(terms);

        switch (Status)
        {
            case ContractStatus.PendingApproval:
                throw new DomainRuleException("CONTRACT_AWAITING_APPROVAL", "This contract is waiting for approval and cannot change now.");
            case ContractStatus.Terminated:
                throw new DomainRuleException("CONTRACT_TERMINATED", "A terminated contract cannot change.");
            case ContractStatus.Active or ContractStatus.Expired when terms.Type != Type || !string.Equals(terms.Currency, Currency, StringComparison.OrdinalIgnoreCase):
                throw new DomainRuleException("CONTRACT_SIGNED_TERMS", "The type and currency of a signed contract cannot change; draft a new contract instead.");
        }

        Apply(terms);

        if (Status == ContractStatus.Expired && (EndDate is null || EndDate >= today))
        {
            Status = ContractStatus.Active;
            RenewalReminderFor = null;
        }
    }

    public void SubmitForApproval(Guid workflowInstanceId)
    {
        EnsureDraft();
        Status = ContractStatus.PendingApproval;
        WorkflowInstanceId = workflowInstanceId;
    }

    public void Activate(DateTime now, DateOnly today)
    {
        if (Status is not (ContractStatus.Draft or ContractStatus.PendingApproval))
        {
            throw new DomainRuleException("CONTRACT_STATE", $"A {Status} contract cannot be activated.");
        }

        Status = EndDate is { } end && end < today ? ContractStatus.Expired : ContractStatus.Active;
        ActivatedDate = now;
    }

    /// <summary>Approval was refused, sent back or withdrawn: the contract is a draft again.</summary>
    public void ReturnToDraft()
    {
        if (Status == ContractStatus.PendingApproval)
        {
            Status = ContractStatus.Draft;
            WorkflowInstanceId = null;
        }
    }

    /// <summary>The end date has passed. An auto-renewing contract rolls on by its original length instead.</summary>
    public bool ExpireIfDue(DateOnly today)
    {
        if (Status != ContractStatus.Active || EndDate is not { } end || end >= today)
        {
            return false;
        }

        if (AutoRenew)
        {
            int length = Math.Max(1, end.DayNumber - StartDate.DayNumber + 1);
            DateOnly newEnd = end;
            while (newEnd < today)
            {
                newEnd = newEnd.AddDays(length);
            }

            int renewalLead = RenewalDate is { } renewal ? end.DayNumber - renewal.DayNumber : 0;
            EndDate = newEnd;
            RenewalDate = RenewalDate is null ? null : newEnd.AddDays(-renewalLead);
            RenewalReminderFor = null;
            return true;
        }

        Status = ContractStatus.Expired;
        return true;
    }

    public void Terminate(DateOnly on, string reason)
    {
        if (Status is ContractStatus.Terminated)
        {
            throw new DomainRuleException("CONTRACT_TERMINATED", "This contract is already terminated.");
        }

        if (Status == ContractStatus.PendingApproval)
        {
            throw new DomainRuleException("CONTRACT_AWAITING_APPROVAL", "Withdraw the approval request before terminating.");
        }

        if (on < StartDate)
        {
            throw new DomainRuleException("CONTRACT_TERMINATION_DATE", "A contract cannot end before it starts.");
        }

        Status = ContractStatus.Terminated;
        TerminatedOn = on;
        TerminationReason = Guard.MaxLength(Guard.NotNullOrWhiteSpace(reason).Trim(), 500);
    }

    /// <summary>The date a renewal reminder is about: the renewal date, else the end date.</summary>
    public DateOnly? RenewalDue => RenewalDate ?? EndDate;

    public bool NeedsRenewalReminder(DateOnly today, int leadDays)
        => Status == ContractStatus.Active
            && RenewalDue is { } due
            && due >= today
            && due.DayNumber - today.DayNumber <= leadDays
            && RenewalReminderFor != due;

    public void RecordRenewalReminder() => RenewalReminderFor = RenewalDue;

    private void Apply(ContractTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);

        if (!Enum.IsDefined(terms.Type) || !Enum.IsDefined(terms.BillingModel))
        {
            throw new DomainRuleException("CONTRACT_TYPE", "Unknown contract type or billing model.");
        }

        if (terms.EndDate is { } end && end < terms.StartDate)
        {
            throw new DomainRuleException("CONTRACT_DATES", "The end date is before the start date.");
        }

        if (terms.RenewalDate is { } renewal && (renewal < terms.StartDate || (terms.EndDate is { } last && renewal > last)))
        {
            throw new DomainRuleException("CONTRACT_RENEWAL_DATE", "The renewal date must fall within the contract's term.");
        }

        if (terms.Value is < 0)
        {
            throw new DomainRuleException("CONTRACT_VALUE", "The contract value cannot be negative.");
        }

        if (terms.PaymentTermsDays is < 0 or > 365)
        {
            throw new DomainRuleException("CONTRACT_PAYMENT_TERMS", "Payment terms must be between 0 and 365 days.");
        }

        if (terms.ParentContractId is not null && terms.Type == ContractType.Msa)
        {
            throw new DomainRuleException("CONTRACT_PARENT", "A master services agreement does not sit under another contract.");
        }

        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(terms.Title).Trim(), 200);
        Type = terms.Type;
        ParentContractId = terms.ParentContractId;
        StartDate = terms.StartDate;
        EndDate = terms.EndDate;
        Currency = Guard.MaxLength(Guard.NotNullOrWhiteSpace(terms.Currency).Trim().ToUpperInvariant(), 8);
        Value = terms.Value;
        BillingModel = terms.BillingModel;
        PaymentTermsDays = terms.PaymentTermsDays;
        PaymentTerms = Clean(terms.PaymentTerms, 500);
        TaxTerms = Clean(terms.TaxTerms, 500);
        RenewalDate = terms.RenewalDate;
        AutoRenew = terms.AutoRenew;
        OwnerEmployeeId = terms.OwnerEmployeeId;
        ClientContactId = terms.ClientContactId;
        Notes = Clean(terms.Notes, 2000);
    }

    private void EnsureDraft()
    {
        if (Status != ContractStatus.Draft)
        {
            throw new DomainRuleException("CONTRACT_STATE", $"Only a draft can be sent for approval; this contract is {Status}.");
        }
    }

    private static string? Clean(string? value, int maxLength)
        => string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim(), maxLength);
}
