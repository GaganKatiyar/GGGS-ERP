using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Compliance.Domain;
using Gggs.Modules.Compliance.Infrastructure;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Notification.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Compliance.Application;

public sealed record ObligationItem(
    Guid Id,
    string Code,
    string Name,
    FilingKind Kind,
    FilingFrequency Frequency,
    int DueDay,
    int DueMonthOffset,
    string? StateCode,
    Guid? OwnerEmployeeId,
    string? OwnerName,
    string? Description,
    DateOnly StartsFrom,
    bool IsActive,
    DateOnly? NextDue);

public sealed record ObligationBody(string? Code, string Name, FilingKind Kind, FilingFrequency Frequency, int DueDay, int DueMonthOffset, string? StateCode, Guid? OwnerEmployeeId, string? Description);

public sealed record FilingItem(
    Guid Id,
    Guid ObligationId,
    string Name,
    FilingKind Kind,
    string PeriodLabel,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly DueDate,
    FilingStatus Status,
    bool IsOverdue,
    int DaysToDue,
    DateOnly? FiledOn,
    string? FiledByName,
    string? Reference,
    decimal? Amount,
    string? Notes,
    Guid? DocumentId,
    string? OwnerName);

public sealed record FilingQuery
{
    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public FilingStatus? Status { get; init; }

    public FilingKind? Kind { get; init; }

    public bool? OverdueOnly { get; init; }
}

public sealed record FileBody(DateOnly FiledOn, string? Reference, decimal? Amount, string? Notes, Guid? DocumentId);

public sealed record WaiveBody(string Reason);

public sealed record RescheduleBody(DateOnly DueDate);

public sealed record RegistrationItem(
    Guid Id,
    RegistrationKind Kind,
    string Name,
    string? Number,
    string? Authority,
    string? StateCode,
    Guid? LegalEntityId,
    DateOnly? IssuedOn,
    DateOnly? ExpiresOn,
    int RenewalLeadDays,
    Guid? OwnerEmployeeId,
    string? OwnerName,
    string? Notes,
    Guid? DocumentId,
    string Validity,
    int? DaysToExpiry);

public sealed record ComplianceDashboard(
    int RulesInForce,
    int DraftRules,
    int DueIn30Days,
    int Overdue,
    int FiledThisMonth,
    int ExpiringRegistrations,
    int ExpiredRegistrations,
    IReadOnlyList<FilingItem> Upcoming,
    IReadOnlyList<RegistrationItem> Expiring);

/// <summary>
/// The compliance calendar (spec §107): obligations generate a filing for each period, owners are
/// reminded before the due date and when it slips, and registrations are watched for expiry.
/// </summary>
internal sealed class ComplianceCalendar(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    INotifier notifier,
    ICurrentUser currentUser,
    ITenantClock clock)
{
    /// <summary>How far ahead filings are generated.</summary>
    private const int HorizonDays = 60;

    // ----- Obligations --------------------------------------------------------------------------

    public async Task<IReadOnlyList<ObligationItem>> ObligationsAsync(CancellationToken cancellationToken)
    {
        List<ComplianceObligation> obligations = await dbContext.ComplianceObligations.AsNoTracking().OrderBy(obligation => obligation.Name).ToListAsync(cancellationToken);
        Dictionary<Guid, DateOnly> nextDue = await dbContext.ComplianceFilings.AsNoTracking()
            .Where(filing => filing.Status == FilingStatus.Pending)
            .GroupBy(filing => filing.ObligationId)
            .Select(group => new { group.Key, Next = group.Min(filing => filing.DueDate) })
            .ToDictionaryAsync(row => row.Key, row => row.Next, cancellationToken);
        Dictionary<Guid, string> names = await NamesAsync(obligations.Select(obligation => obligation.OwnerEmployeeId), cancellationToken);
        return [.. obligations.Select(obligation => new ObligationItem(
            obligation.Id, obligation.Code, obligation.Name, obligation.Kind, obligation.Frequency, obligation.DueDay, obligation.DueMonthOffset, obligation.StateCode,
            obligation.OwnerEmployeeId, Name(names, obligation.OwnerEmployeeId), obligation.Description, obligation.StartsFrom, obligation.IsActive,
            nextDue.TryGetValue(obligation.Id, out DateOnly due) ? due : null))];
    }

    public async Task<ObligationItem> SaveObligationAsync(Guid? id, ObligationBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var details = new ObligationDetails(body.Name, body.Kind, body.Frequency, body.DueDay, body.DueMonthOffset, body.StateCode, body.OwnerEmployeeId, body.Description);
        ComplianceObligation obligation;
        if (id is { } existing)
        {
            obligation = await dbContext.ComplianceObligations.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken)
                ?? throw new NotFoundException(nameof(ComplianceObligation), existing);
            obligation.Update(details);
        }
        else
        {
            string code = (body.Code ?? string.Empty).Trim().ToUpperInvariant();
            if (code.Length == 0 || await dbContext.ComplianceObligations.AnyAsync(row => row.Code == code, cancellationToken))
            {
                throw new DomainRuleException("OBLIGATION_CODE", "Give the obligation a code no other obligation uses.");
            }

            DateOnly today = await clock.TodayAsync(cancellationToken);
            obligation = ComplianceObligation.Create(code, details, new DateOnly(today.Year, today.Month, 1));
            dbContext.ComplianceObligations.Add(obligation);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await GenerateAsync(cancellationToken);
        return (await ObligationsAsync(cancellationToken)).First(item => item.Id == obligation.Id);
    }

    public async Task SetObligationActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        ComplianceObligation obligation = await dbContext.ComplianceObligations.FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(ComplianceObligation), id);
        if (active)
        {
            obligation.Activate();
        }
        else
        {
            obligation.Deactivate();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- Filings ------------------------------------------------------------------------------

    public async Task<IReadOnlyList<FilingItem>> FilingsAsync(FilingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        IQueryable<ComplianceFiling> filings = dbContext.ComplianceFilings.AsNoTracking();
        if (query.From is { } from)
        {
            filings = filings.Where(filing => filing.DueDate >= from);
        }

        if (query.To is { } to)
        {
            filings = filings.Where(filing => filing.DueDate <= to);
        }

        if (query.Status is { } status)
        {
            filings = filings.Where(filing => filing.Status == status);
        }

        if (query.Kind is { } kind)
        {
            filings = filings.Where(filing => filing.Kind == kind);
        }

        if (query.OverdueOnly == true)
        {
            filings = filings.Where(filing => filing.Status == FilingStatus.Pending && filing.DueDate < today);
        }

        return await ItemsAsync(await filings.OrderBy(filing => filing.DueDate).ThenBy(filing => filing.Name).Take(500).ToListAsync(cancellationToken), today, cancellationToken);
    }

    public async Task<FilingItem> FileAsync(Guid id, FileBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ComplianceFiling filing = await FilingAsync(id, cancellationToken);
        filing.MarkFiled(body.FiledOn, currentUser.EmployeeId, body.Reference, body.Amount, body.Notes, body.DocumentId);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ItemsAsync([filing], await clock.TodayAsync(cancellationToken), cancellationToken))[0];
    }

    public async Task<FilingItem> WaiveAsync(Guid id, string reason, CancellationToken cancellationToken)
    {
        ComplianceFiling filing = await FilingAsync(id, cancellationToken);
        filing.Waive(reason);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ItemsAsync([filing], await clock.TodayAsync(cancellationToken), cancellationToken))[0];
    }

    public async Task<FilingItem> ReopenAsync(Guid id, CancellationToken cancellationToken)
    {
        ComplianceFiling filing = await FilingAsync(id, cancellationToken);
        filing.Reopen();
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ItemsAsync([filing], await clock.TodayAsync(cancellationToken), cancellationToken))[0];
    }

    public async Task<FilingItem> RescheduleAsync(Guid id, DateOnly dueDate, CancellationToken cancellationToken)
    {
        ComplianceFiling filing = await FilingAsync(id, cancellationToken);
        filing.Reschedule(dueDate);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ItemsAsync([filing], await clock.TodayAsync(cancellationToken), cancellationToken))[0];
    }

    /// <summary>Creates the filings of every active obligation falling due between its start and the horizon. Returns how many were added.</summary>
    public async Task<int> GenerateAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        DateOnly horizon = today.AddDays(HorizonDays);
        List<ComplianceObligation> obligations = await dbContext.ComplianceObligations.AsNoTracking().Where(obligation => obligation.IsActive).ToListAsync(cancellationToken);
        List<Guid> ids = [.. obligations.Select(obligation => obligation.Id)];
        HashSet<(Guid, DateOnly)> existing = [.. (await dbContext.ComplianceFilings.AsNoTracking()
                .IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete])
                .Where(filing => ids.Contains(filing.ObligationId))
                .Select(filing => new { filing.ObligationId, filing.PeriodStart })
                .ToListAsync(cancellationToken))
            .Select(row => (row.ObligationId, row.PeriodStart))];

        int added = 0;
        foreach (ComplianceObligation obligation in obligations)
        {
            // Walk periods from a year before the obligation's start, so a period ending before
            // the start but falling due after it is still picked up.
            FilingPeriod period = obligation.PeriodOf(obligation.StartsFrom.AddMonths(-(obligation.DueMonthOffset + 12)));
            while (period.Start <= horizon)
            {
                DateOnly due = obligation.DueDateOf(period);
                if (due >= obligation.StartsFrom && due <= horizon && existing.Add((obligation.Id, period.Start)))
                {
                    dbContext.ComplianceFilings.Add(ComplianceFiling.For(obligation, period));
                    added++;
                }

                period = obligation.PeriodOf(period.End.AddDays(1));
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return added;
    }

    /// <summary>Reminds owners a week before a filing is due and again once it is overdue; and of registrations nearing expiry. Returns how many reminders went out.</summary>
    public async Task<int> RemindAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        int sent = 0;

        var pending = await dbContext.ComplianceFilings
            .Where(filing => filing.Status == FilingStatus.Pending && filing.DueDate <= today.AddDays(7))
            .Join(dbContext.ComplianceObligations.AsNoTracking(), filing => filing.ObligationId, obligation => obligation.Id, (filing, obligation) => new { filing, obligation.OwnerEmployeeId })
            .ToListAsync(cancellationToken);
        foreach (var row in pending.Where(row => row.OwnerEmployeeId is not null))
        {
            ComplianceFiling filing = row.filing;
            bool overdue = filing.IsOverdue(today);
            bool due = overdue ? filing.RemindedOn is null || filing.RemindedOn <= filing.DueDate : filing.RemindedOn is null;
            if (!due)
            {
                continue;
            }

            await notifier.NotifyAsync(
                new NotificationRequest(
                    NotificationTemplates.ComplianceFilingDue,
                    [new NotificationRecipient(row.OwnerEmployeeId!.Value)],
                    new Dictionary<string, string>
                    {
                        ["filingName"] = filing.Name,
                        ["period"] = filing.PeriodLabel,
                        ["dueDate"] = filing.DueDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
                        ["state"] = overdue ? "overdue" : "due soon",
                    },
                    "/compliance/filings"),
                cancellationToken);
            filing.Reminded(today);
            sent++;
        }

        List<ComplianceRegistration> registrations = await dbContext.ComplianceRegistrations
            .Where(registration => registration.ExpiresOn != null && registration.OwnerEmployeeId != null && registration.ExpiresOn >= today.AddDays(-30))
            .ToListAsync(cancellationToken);
        foreach (ComplianceRegistration registration in registrations.Where(registration => registration.Validity(today) is "ExpiringSoon" or "Expired"))
        {
            if (registration.RemindedOn is { } last && last > today.AddDays(-7))
            {
                continue;
            }

            await notifier.NotifyAsync(
                new NotificationRequest(
                    NotificationTemplates.ComplianceRegistrationExpiring,
                    [new NotificationRecipient(registration.OwnerEmployeeId!.Value)],
                    new Dictionary<string, string>
                    {
                        ["registrationName"] = registration.Name,
                        ["number"] = registration.Number ?? "-",
                        ["expiresOn"] = registration.ExpiresOn!.Value.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
                    },
                    "/compliance/registrations"),
                cancellationToken);
            registration.Reminded(today);
            sent++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return sent;
    }

    // ----- Registrations ------------------------------------------------------------------------

    public async Task<IReadOnlyList<RegistrationItem>> RegistrationsAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<ComplianceRegistration> rows = await dbContext.ComplianceRegistrations.AsNoTracking().OrderBy(row => row.ExpiresOn == null).ThenBy(row => row.ExpiresOn).ThenBy(row => row.Name).ToListAsync(cancellationToken);
        Dictionary<Guid, string> names = await NamesAsync(rows.Select(row => row.OwnerEmployeeId), cancellationToken);
        return [.. rows.Select(row => Registration(row, today, names))];
    }

    public async Task<RegistrationItem> SaveRegistrationAsync(Guid? id, RegistrationDetails details, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        ComplianceRegistration registration;
        if (id is { } existing)
        {
            registration = await dbContext.ComplianceRegistrations.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken)
                ?? throw new NotFoundException(nameof(ComplianceRegistration), existing);
            registration.Update(details);
        }
        else
        {
            registration = ComplianceRegistration.Create(details);
            dbContext.ComplianceRegistrations.Add(registration);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        Dictionary<Guid, string> names = await NamesAsync([registration.OwnerEmployeeId], cancellationToken);
        return Registration(registration, await clock.TodayAsync(cancellationToken), names);
    }

    public async Task DeleteRegistrationAsync(Guid id, CancellationToken cancellationToken)
    {
        ComplianceRegistration registration = await dbContext.ComplianceRegistrations.FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(ComplianceRegistration), id);
        dbContext.ComplianceRegistrations.Remove(registration);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- Dashboard ----------------------------------------------------------------------------

    public async Task<ComplianceDashboard> DashboardAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        List<StatutoryRule> rules = await dbContext.StatutoryRules.AsNoTracking().ToListAsync(cancellationToken);
        List<ComplianceFiling> filings = await dbContext.ComplianceFilings.AsNoTracking()
            .Where(filing => filing.Status == FilingStatus.Pending || (filing.FiledOn != null && filing.FiledOn >= monthStart))
            .ToListAsync(cancellationToken);
        IReadOnlyList<RegistrationItem> registrations = await RegistrationsAsync(cancellationToken);

        List<ComplianceFiling> pending = [.. filings.Where(filing => filing.Status == FilingStatus.Pending).OrderBy(filing => filing.DueDate)];
        return new ComplianceDashboard(
            rules.Count(rule => rule.InForceOn(today)),
            rules.Count(rule => rule.Status == RuleStatus.Draft),
            pending.Count(filing => filing.DueDate >= today && filing.DueDate <= today.AddDays(30)),
            pending.Count(filing => filing.IsOverdue(today)),
            filings.Count(filing => filing.Status == FilingStatus.Filed),
            registrations.Count(registration => registration.Validity == "ExpiringSoon"),
            registrations.Count(registration => registration.Validity == "Expired"),
            await ItemsAsync([.. pending.Take(10)], today, cancellationToken),
            [.. registrations.Where(registration => registration.Validity is "ExpiringSoon" or "Expired").Take(10)]);
    }

    // ----- Internals ----------------------------------------------------------------------------

    private async Task<ComplianceFiling> FilingAsync(Guid id, CancellationToken cancellationToken)
        => await dbContext.ComplianceFilings.FirstOrDefaultAsync(filing => filing.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(ComplianceFiling), id);

    private async Task<List<FilingItem>> ItemsAsync(IReadOnlyList<ComplianceFiling> filings, DateOnly today, CancellationToken cancellationToken)
    {
        List<Guid> obligationIds = [.. filings.Select(filing => filing.ObligationId).Distinct()];
        Dictionary<Guid, Guid?> owners = await dbContext.ComplianceObligations.AsNoTracking()
            .Where(obligation => obligationIds.Contains(obligation.Id))
            .ToDictionaryAsync(obligation => obligation.Id, obligation => obligation.OwnerEmployeeId, cancellationToken);
        Dictionary<Guid, string> names = await NamesAsync(filings.Select(filing => filing.FiledByEmployeeId).Concat(owners.Values), cancellationToken);
        return [.. filings.Select(filing => new FilingItem(
            filing.Id, filing.ObligationId, filing.Name, filing.Kind, filing.PeriodLabel, filing.PeriodStart, filing.PeriodEnd, filing.DueDate, filing.Status,
            filing.IsOverdue(today), filing.DueDate.DayNumber - today.DayNumber, filing.FiledOn, Name(names, filing.FiledByEmployeeId), filing.Reference,
            filing.Amount, filing.Notes, filing.DocumentId, Name(names, owners.GetValueOrDefault(filing.ObligationId))))];
    }

    private static RegistrationItem Registration(ComplianceRegistration row, DateOnly today, Dictionary<Guid, string> names)
        => new(
            row.Id, row.Kind, row.Name, row.Number, row.Authority, row.StateCode, row.LegalEntityId, row.IssuedOn, row.ExpiresOn, row.RenewalLeadDays,
            row.OwnerEmployeeId, Name(names, row.OwnerEmployeeId), row.Notes, row.DocumentId, row.Validity(today),
            row.ExpiresOn is { } expires ? expires.DayNumber - today.DayNumber : null);

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
        => (await employees.GetManyAsync(ids.OfType<Guid>().Distinct(), cancellationToken)).ToDictionary(person => person.Id, person => person.DisplayName);

    private static string? Name(Dictionary<Guid, string> names, Guid? id) => id is { } value ? names.GetValueOrDefault(value) : null;
}
