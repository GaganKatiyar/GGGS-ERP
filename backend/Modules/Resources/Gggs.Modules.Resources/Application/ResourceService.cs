using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.MasterData.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Organization.Contracts;
using Gggs.Modules.Project.Contracts;
using Gggs.Modules.Resources.Domain;
using Gggs.Modules.Resources.Infrastructure;
using Gggs.Modules.Skills.Contracts;
using Gggs.Modules.Workflow.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Resources.Application;

public sealed record ResourceListQuery
{
    public string? Search { get; init; }

    public Guid? SkillId { get; init; }

    public int? MinLevel { get; init; }

    public Guid? DepartmentId { get; init; }

    public Guid? LocationId { get; init; }

    public CapacityState? State { get; init; }

    /// <summary>Only people with at least this much free capacity today.</summary>
    public int? MinAvailable { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public PageRequest ToPageRequest() => new() { Page = Page ?? 1, PageSize = PageSize ?? 50, Search = Search };
}

public sealed record SkillTag(Guid SkillId, string Name, int Level, bool IsPrimary);

public sealed record ResourceRow(
    Guid EmployeeId,
    string EmployeeName,
    string EmployeeCode,
    string? Designation,
    string? DepartmentName,
    string? LocationName,
    IReadOnlyList<SkillTag> Skills,
    decimal AllocatedPercent,
    decimal AvailablePercent,
    CapacityState State,
    IReadOnlyList<string> CurrentProjects,
    bool IsBillable,
    decimal? CostRate,
    decimal? BillingRate,
    DateOnly? ExitDate);

public sealed record AllocationItem(
    Guid MemberId,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    string RoleName,
    int AllocationPercent,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool Billable,
    decimal? BillingRate,
    decimal? CostRate,
    bool IsActive);

public sealed record ProfileRates(decimal? CostRate, decimal? BillingRate, string Currency, decimal CapacityHoursPerWeek, int? NoticePeriodDays, bool IsBillable, string? Notes);

public sealed record ResourceProfileView(
    Guid EmployeeId,
    string EmployeeName,
    string EmployeeCode,
    string? Designation,
    string? DepartmentName,
    string? LocationName,
    string? WorkModeCode,
    DateOnly? JoiningDate,
    decimal YearsInCompany,
    DateOnly? ExitDate,
    IReadOnlyList<SkillTag> Skills,
    IReadOnlyList<string> Certifications,
    IReadOnlyList<AllocationItem> Current,
    IReadOnlyList<AllocationItem> Upcoming,
    IReadOnlyList<AllocationItem> History,
    IReadOnlyList<CapacityCell> Capacity,
    ProfileRates? Rates,
    bool CanEditRates);

public sealed record ProfileBody(decimal? CostRate, decimal? BillingRate, string? Currency, decimal? CapacityHoursPerWeek, int? NoticePeriodDays, bool IsBillable, string? Notes);

public sealed record ResourceDashboard(
    int TotalPeople,
    int Billable,
    int NonBillable,
    int OnBench,
    int Overallocated,
    int PartiallyAvailable,
    int FullyAllocated,
    int UpcomingReleases,
    int OpenRequests,
    int OpenDemand,
    decimal BenchCostPerDay,
    IReadOnlyList<ReleaseItem> Releases);

public sealed record ReleaseItem(Guid EmployeeId, string EmployeeName, string ProjectCode, DateOnly EndDate, int AllocationPercent, DateOnly? NextAssignment);

public sealed record CalendarRow(Guid EmployeeId, string EmployeeName, string? Designation, IReadOnlyList<string> TopSkills, IReadOnlyList<CapacityCell> Cells);

public sealed record CalendarQuery
{
    public DateOnly? From { get; init; }

    public int? Months { get; init; }

    public Guid? DepartmentId { get; init; }

    public Guid? SkillId { get; init; }

    public Guid? ProjectId { get; init; }

    public string? Search { get; init; }
}

public sealed record BenchItem(
    Guid EmployeeId,
    string EmployeeName,
    string? Designation,
    string? LocationName,
    IReadOnlyList<SkillTag> Skills,
    string? PreviousProject,
    DateOnly? BenchSince,
    int BenchDays,
    decimal AvailablePercent,
    decimal? CostRate,
    string? NextProject,
    DateOnly? NextAssignment);

public sealed record AllocateBody(Guid ProjectId, Guid EmployeeId, Guid? ProjectRoleId, DateOnly StartDate, DateOnly? EndDate, int AllocationPercent, bool Billable, decimal? BillingRate, decimal? CostRate);

public sealed record UpdateAllocationBody(int AllocationPercent, DateOnly StartDate, DateOnly? EndDate, bool Billable);

public sealed record ReleaseAllocationBody(DateOnly On);

public sealed record AllocationOutcome(Guid MemberId, int PeakAllocationPercent, bool Overallocated);

public sealed record TransferBody(Guid FromMemberId, Guid ToProjectId, Guid? ProjectRoleId, int AllocationPercent, DateOnly EffectiveDate, DateOnly? EndDate, bool Billable, string Reason);

public sealed record TransferItem(
    Guid Id,
    Guid EmployeeId,
    string? EmployeeName,
    string? FromProject,
    string? ToProject,
    int AllocationPercent,
    DateOnly EffectiveDate,
    TransferStatus Status,
    string Reason,
    Guid? WorkflowInstanceId,
    DateTime CreatedDate);

/// <summary>
/// Resource management (spec §57-60, §63, §65, §67-69, §80): who is free, who is stretched, who sits on
/// the bench, and moving people onto, between and off projects.
/// </summary>
internal sealed class ResourceService(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IOrgStructure orgStructure,
    IMasterDataLookup masterData,
    IProjectDirectory projects,
    IProjectStaffing staffing,
    ISkillDirectory skills,
    WorkingCalendar calendar,
    IWorkflowStarter workflow,
    INotifier notifier,
    IPermissionProvider permissions,
    ICurrentUser currentUser,
    ITenantClock clock,
    TimeProvider timeProvider)
{
    public const string TransferSubjectType = "resource.transfer";

    public static WorkflowSubject TransferSubject { get; } = new(
        TransferSubjectType,
        "Transfer someone between projects",
        "resources",
        [new WorkflowSubjectField("allocationPercent", "Allocation %", WorkflowFieldType.Number)]);

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // ----- Directory and profile ----------------------------------------------------------------

    public async Task<PagedResult<ResourceRow>> ListAsync(ResourceListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        PageRequest page = query.ToPageRequest();
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<EmployeeSummary> people = await PeopleAsync(query.DepartmentId, query.LocationId, query.Search, cancellationToken);

        IReadOnlyList<EmployeeSkillLevel> levels = await skills.LevelsAsync(people.Select(person => person.Id), cancellationToken);
        if (query.SkillId is { } skillId)
        {
            int minimum = query.MinLevel ?? 1;
            HashSet<Guid> holders = [.. levels.Where(level => level.SkillId == skillId && level.Level >= minimum).Select(level => level.EmployeeId)];
            people = [.. people.Where(person => holders.Contains(person.Id))];
        }

        List<Guid> ids = [.. people.Select(person => person.Id)];
        IReadOnlyDictionary<Guid, PersonCalendar> calendars = await calendar.ForAsync(ids, today, today, cancellationToken);
        ILookup<Guid, ProjectAllocation> allocations = (await projects.AllocationsAsync(ids, today, today, cancellationToken)).Where(row => row.IsActive).ToLookup(row => row.EmployeeId);
        Dictionary<Guid, ResourceProfile> profiles = await ProfilesAsync(ids, cancellationToken);
        bool seeRates = await CanSeeRatesAsync(cancellationToken);
        Names names = await NamesAsync(people, cancellationToken);
        ILookup<Guid, EmployeeSkillLevel> skillLookup = levels.ToLookup(level => level.EmployeeId);

        List<ResourceRow> rows = [.. people.Select(person =>
        {
            CapacityCell cell = CellToday(today, calendars.GetValueOrDefault(person.Id), allocations[person.Id]);
            ResourceProfile? profile = profiles.GetValueOrDefault(person.Id);
            return new ResourceRow(
                person.Id, person.DisplayName, person.EmployeeCode, person.Designation, names.Department(person.DepartmentId), names.Location(person.LocationId),
                Tags(skillLookup[person.Id], 4), cell.AllocatedPercent, cell.AvailablePercent, cell.State,
                [.. allocations[person.Id].Select(row => row.ProjectCode).Distinct()], profile?.IsBillable ?? true,
                seeRates ? profile?.CostRate : null, seeRates ? profile?.BillingRate : null, person.ExitDate);
        })];

        if (query.State is { } state)
        {
            rows = [.. rows.Where(row => row.State == state)];
        }

        if (query.MinAvailable is { } available)
        {
            rows = [.. rows.Where(row => row.AvailablePercent >= available)];
        }

        rows = [.. rows.OrderByDescending(row => row.AvailablePercent).ThenBy(row => row.EmployeeName, StringComparer.CurrentCulture)];
        return PagedResult.From<ResourceRow>([.. rows.Skip(page.Skip).Take(page.PageSize)], page, rows.Count);
    }

    public async Task<ResourceProfileView> ProfileAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        if (!await employees.IsInScopeAsync(employeeId, Permissions.Resource.ProfileView, cancellationToken))
        {
            throw new NotFoundException("Employee", employeeId);
        }

        EmployeeSummary person = await employees.GetAsync(employeeId, cancellationToken) ?? throw new NotFoundException("Employee", employeeId);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        DateOnly horizon = today.AddMonths(6);

        IReadOnlyList<ProjectAllocation> all = await projects.AllocationsAsync([employeeId], DateOnly.MinValue, DateOnly.MaxValue, cancellationToken);
        IReadOnlyDictionary<Guid, PersonCalendar> calendars = await calendar.ForAsync([employeeId], new DateOnly(today.Year, today.Month, 1), horizon, cancellationToken);
        IReadOnlyList<EmployeeSkillLevel> levels = await skills.LevelsAsync([employeeId], cancellationToken);
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> certifications = await skills.CertificationsAsync([employeeId], cancellationToken);
        ResourceProfile? profile = await dbContext.ResourceProfiles.AsNoTracking().FirstOrDefaultAsync(row => row.EmployeeId == employeeId, cancellationToken);
        bool seeRates = await CanSeeRatesAsync(cancellationToken);
        Names names = await NamesAsync([person], cancellationToken);

        List<AllocationSpan> spans = [.. all.Where(row => row.IsActive).Select(Span)];
        PersonCalendar? personCalendar = calendars.GetValueOrDefault(employeeId);
        List<CapacityCell> capacity = [.. WorkingCalendar.Months(new DateOnly(today.Year, today.Month, 1), horizon)
            .Select(month => Capacity.For(month.From, month.To, personCalendar?.WorkingDays ?? [], personCalendar?.LeaveFractions ?? new Dictionary<DateOnly, decimal>(), spans))];

        AllocationItem Item(ProjectAllocation row) => new(
            row.MemberId, row.ProjectId, row.ProjectCode, row.ProjectName, row.RoleName, row.AllocationPercent, row.StartDate, row.EndDate, row.Billable,
            seeRates ? row.BillingRate : null, seeRates ? row.CostRate : null, row.IsActive);

        return new ResourceProfileView(
            person.Id, person.DisplayName, person.EmployeeCode, person.Designation, names.Department(person.DepartmentId), names.Location(person.LocationId),
            person.WorkModeCode, person.JoiningDate, YearsSince(person.JoiningDate, today), person.ExitDate, Tags(levels, 50),
            certifications.GetValueOrDefault(employeeId) ?? [],
            [.. all.Where(row => row.IsActive && row.StartDate <= today && (row.EndDate is null || row.EndDate >= today)).Select(Item)],
            [.. all.Where(row => row.IsActive && row.StartDate > today).OrderBy(row => row.StartDate).Select(Item)],
            [.. all.Where(row => !row.IsActive || (row.EndDate is { } end && end < today)).OrderByDescending(row => row.EndDate ?? row.StartDate).Select(Item)],
            capacity,
            seeRates && profile is not null ? new ProfileRates(profile.CostRate, profile.BillingRate, profile.Currency, profile.CapacityHoursPerWeek, profile.NoticePeriodDays, profile.IsBillable, profile.Notes) : null,
            seeRates);
    }

    public async Task<ResourceProfileView> SaveProfileAsync(Guid employeeId, ProfileBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!await employees.IsInScopeAsync(employeeId, Permissions.Resource.ProfileManage, cancellationToken))
        {
            throw new NotFoundException("Employee", employeeId);
        }

        string currency = string.IsNullOrWhiteSpace(body.Currency) ? "INR" : body.Currency;
        if (!await masterData.IsValidAsync(LookupLists.Currency, currency, cancellationToken))
        {
            throw new DomainRuleException("RESOURCE_CURRENCY_INVALID", $"'{currency}' is not a currency this organization uses.");
        }

        ResourceProfile? profile = await dbContext.ResourceProfiles.FirstOrDefaultAsync(row => row.EmployeeId == employeeId, cancellationToken);
        if (profile is null)
        {
            profile = ResourceProfile.Create(employeeId);
            dbContext.ResourceProfiles.Add(profile);
        }

        profile.Update(body.CostRate, body.BillingRate, currency, body.CapacityHoursPerWeek ?? 40, body.NoticePeriodDays, body.IsBillable, body.Notes);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ProfileAsync(employeeId, cancellationToken);
    }

    // ----- Dashboard, calendar, bench -----------------------------------------------------------

    public async Task<ResourceDashboard> DashboardAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<EmployeeSummary> people = await PeopleAsync(null, null, null, cancellationToken);
        List<Guid> ids = [.. people.Select(person => person.Id)];
        IReadOnlyDictionary<Guid, PersonCalendar> calendars = await calendar.ForAsync(ids, today, today, cancellationToken);
        List<ProjectAllocation> allocations = [.. (await projects.AllocationsAsync(ids, today, today.AddDays(60), cancellationToken)).Where(row => row.IsActive)];
        ILookup<Guid, ProjectAllocation> current = allocations.Where(row => row.StartDate <= today).ToLookup(row => row.EmployeeId);
        Dictionary<Guid, ResourceProfile> profiles = await ProfilesAsync(ids, cancellationToken);

        List<(EmployeeSummary Person, CapacityCell Cell)> cells = [.. people.Select(person => (person, CellToday(today, calendars.GetValueOrDefault(person.Id), current[person.Id])))];
        HashSet<Guid> billableNow = [.. current.SelectMany(group => group).Where(row => row.Billable).Select(row => row.EmployeeId)];

        DateOnly soon = today.AddDays(30);
        List<ReleaseItem> releases = [.. allocations
            .Where(row => row.StartDate <= today && row.EndDate is { } end && end >= today && end <= soon)
            .OrderBy(row => row.EndDate)
            .Take(20)
            .Select(row => new ReleaseItem(
                row.EmployeeId,
                people.FirstOrDefault(person => person.Id == row.EmployeeId)?.DisplayName ?? "?",
                row.ProjectCode,
                row.EndDate!.Value,
                row.AllocationPercent,
                allocations.Where(next => next.EmployeeId == row.EmployeeId && next.StartDate > row.EndDate).Select(next => (DateOnly?)next.StartDate).Min()))];

        var openRequests = await dbContext.ResourceRequests.AsNoTracking()
            .Where(request => request.Status == RequestStatus.Open || request.Status == RequestStatus.PartiallyFulfilled)
            .Select(request => new { request.Quantity, request.FulfilledCount })
            .ToListAsync(cancellationToken);

        // The same people the bench page lists: billable resources with no billable work today.
        List<EmployeeSummary> bench = [.. people.Where(person => (profiles.GetValueOrDefault(person.Id)?.IsBillable ?? true) && !billableNow.Contains(person.Id))];
        return new ResourceDashboard(
            people.Count,
            billableNow.Count,
            people.Count - billableNow.Count,
            bench.Count,
            cells.Count(pair => pair.Cell.State == CapacityState.Overallocated),
            cells.Count(pair => pair.Cell.State == CapacityState.PartiallyAvailable),
            cells.Count(pair => pair.Cell.State == CapacityState.FullyAllocated),
            releases.Count,
            openRequests.Count,
            openRequests.Sum(request => Math.Max(0, request.Quantity - request.FulfilledCount)),
            bench.Sum(person => (profiles.GetValueOrDefault(person.Id)?.CostRate ?? 0) * (profiles.GetValueOrDefault(person.Id)?.HoursPerDay ?? 8)),
            releases);
    }

    /// <summary>The resource calendar (spec §63, §119): each person's month-by-month allocation and state.</summary>
    public async Task<IReadOnlyList<CalendarRow>> CalendarAsync(CalendarQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        DateOnly from = query.From is { } start ? new DateOnly(start.Year, start.Month, 1) : new DateOnly(today.Year, today.Month, 1);
        DateOnly to = from.AddMonths(Math.Clamp(query.Months ?? 3, 1, 12)).AddDays(-1);

        List<EmployeeSummary> people = await PeopleAsync(query.DepartmentId, null, query.Search, cancellationToken);
        IReadOnlyList<EmployeeSkillLevel> levels = await skills.LevelsAsync(people.Select(person => person.Id), cancellationToken);
        if (query.SkillId is { } skillId)
        {
            HashSet<Guid> holders = [.. levels.Where(level => level.SkillId == skillId).Select(level => level.EmployeeId)];
            people = [.. people.Where(person => holders.Contains(person.Id))];
        }

        List<Guid> ids = [.. people.Select(person => person.Id)];
        List<ProjectAllocation> allocations = [.. (await projects.AllocationsAsync(ids, from, to, cancellationToken)).Where(row => row.IsActive)];
        if (query.ProjectId is { } projectId)
        {
            HashSet<Guid> onProject = [.. allocations.Where(row => row.ProjectId == projectId).Select(row => row.EmployeeId)];
            people = [.. people.Where(person => onProject.Contains(person.Id))];
            ids = [.. people.Select(person => person.Id)];
        }

        people = [.. people.OrderBy(person => person.DisplayName, StringComparer.CurrentCulture).Take(200)];
        ids = [.. people.Select(person => person.Id)];
        IReadOnlyDictionary<Guid, PersonCalendar> calendars = await calendar.ForAsync(ids, from, to, cancellationToken);
        ILookup<Guid, ProjectAllocation> byPerson = allocations.ToLookup(row => row.EmployeeId);
        ILookup<Guid, EmployeeSkillLevel> skillLookup = levels.ToLookup(level => level.EmployeeId);
        IReadOnlyList<(DateOnly From, DateOnly To)> months = WorkingCalendar.Months(from, to);

        return [.. people.Select(person =>
        {
            PersonCalendar? personCalendar = calendars.GetValueOrDefault(person.Id);
            List<AllocationSpan> spans = [.. byPerson[person.Id].Select(Span)];
            return new CalendarRow(
                person.Id, person.DisplayName, person.Designation, [.. Tags(skillLookup[person.Id], 3).Select(tag => tag.Name)],
                [.. months.Select(month => Capacity.For(month.From, month.To, personCalendar?.WorkingDays ?? [], personCalendar?.LeaveFractions ?? new Dictionary<DateOnly, decimal>(), spans))]);
        })];
    }

    /// <summary>The bench (spec §67): people with no billable work today, how long they have waited, and what is next.</summary>
    public async Task<IReadOnlyList<BenchItem>> BenchAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<EmployeeSummary> people = await PeopleAsync(null, null, null, cancellationToken);
        Dictionary<Guid, ResourceProfile> profiles = await ProfilesAsync([.. people.Select(person => person.Id)], cancellationToken);
        people = [.. people.Where(person => profiles.GetValueOrDefault(person.Id)?.IsBillable ?? true)];
        List<Guid> ids = [.. people.Select(person => person.Id)];

        IReadOnlyList<ProjectAllocation> all = await projects.AllocationsAsync(ids, DateOnly.MinValue, DateOnly.MaxValue, cancellationToken);
        ILookup<Guid, ProjectAllocation> byPerson = all.ToLookup(row => row.EmployeeId);
        IReadOnlyDictionary<Guid, PersonCalendar> calendars = await calendar.ForAsync(ids, today, today, cancellationToken);
        ILookup<Guid, EmployeeSkillLevel> skillLookup = (await skills.LevelsAsync(ids, cancellationToken)).ToLookup(level => level.EmployeeId);
        Names names = await NamesAsync(people, cancellationToken);
        bool seeRates = await CanSeeRatesAsync(cancellationToken);

        List<BenchItem> bench = [];
        foreach (EmployeeSummary person in people)
        {
            List<ProjectAllocation> mine = [.. byPerson[person.Id]];
            bool billableToday = mine.Any(row => row.IsActive && row.Billable && row.StartDate <= today && (row.EndDate is null || row.EndDate >= today));
            if (billableToday)
            {
                continue;
            }

            ProjectAllocation? previous = mine.Where(row => row.Billable && (row.EndDate ?? DateOnly.MaxValue) < today).OrderByDescending(row => row.EndDate).FirstOrDefault();
            ProjectAllocation? next = mine.Where(row => row.IsActive && row.StartDate > today).OrderBy(row => row.StartDate).FirstOrDefault();
            DateOnly? since = previous?.EndDate?.AddDays(1) ?? person.JoiningDate;
            CapacityCell cell = CellToday(today, calendars.GetValueOrDefault(person.Id), mine.Where(row => row.IsActive));

            bench.Add(new BenchItem(
                person.Id, person.DisplayName, person.Designation, names.Location(person.LocationId), Tags(skillLookup[person.Id], 4), previous?.ProjectCode,
                since, since is { } start ? Math.Max(0, today.DayNumber - start.DayNumber) : 0, cell.AvailablePercent,
                seeRates ? profiles.GetValueOrDefault(person.Id)?.CostRate : null, next?.ProjectCode, next?.StartDate));
        }

        return [.. bench.OrderByDescending(item => item.BenchDays)];
    }

    // ----- Allocation commands ------------------------------------------------------------------

    public async Task<AllocationOutcome> AllocateAsync(AllocateBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ResourceProfile? profile = await dbContext.ResourceProfiles.AsNoTracking().FirstOrDefaultAsync(row => row.EmployeeId == body.EmployeeId, cancellationToken);
        Guid memberId = await staffing.AllocateAsync(
            new StaffingRequest(body.ProjectId, body.EmployeeId, body.ProjectRoleId, body.StartDate, body.EndDate, body.AllocationPercent, body.Billable,
                body.BillingRate ?? profile?.BillingRate, body.CostRate ?? profile?.CostRate, null),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await OutcomeAsync(memberId, body.EmployeeId, body.StartDate, body.EndDate, cancellationToken);
    }

    public async Task<AllocationOutcome> UpdateAllocationAsync(Guid memberId, UpdateAllocationBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        await staffing.UpdateAllocationAsync(memberId, body.AllocationPercent, body.StartDate, body.EndDate, body.Billable, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        ProjectAllocation allocation = (await projects.AllocationsAsync(null, body.StartDate, body.EndDate ?? body.StartDate, cancellationToken)).First(row => row.MemberId == memberId);
        return await OutcomeAsync(memberId, allocation.EmployeeId, body.StartDate, body.EndDate, cancellationToken);
    }

    public async Task ReleaseAsync(Guid memberId, DateOnly on, CancellationToken cancellationToken)
    {
        await staffing.ReleaseAsync(memberId, on, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- Transfers ----------------------------------------------------------------------------

    public async Task<IReadOnlyList<TransferItem>> TransfersAsync(TransferStatus? status, CancellationToken cancellationToken)
    {
        IQueryable<ResourceTransfer> rows = dbContext.ResourceTransfers.AsNoTracking();
        if (status is { } wanted)
        {
            rows = rows.Where(row => row.Status == wanted);
        }

        List<ResourceTransfer> transfers = await rows.OrderByDescending(row => row.CreatedDate).Take(200).ToListAsync(cancellationToken);
        IReadOnlyDictionary<Guid, ProjectSummary> projectNames = await projects.GetManyAsync(transfers.SelectMany(row => new[] { row.FromProjectId, row.ToProjectId }), cancellationToken);
        Dictionary<Guid, string> people = (await employees.GetManyAsync(transfers.Select(row => row.EmployeeId), cancellationToken)).ToDictionary(person => person.Id, person => person.DisplayName);
        return [.. transfers.Select(row => new TransferItem(
            row.Id, row.EmployeeId, people.GetValueOrDefault(row.EmployeeId), projectNames.GetValueOrDefault(row.FromProjectId)?.Code,
            projectNames.GetValueOrDefault(row.ToProjectId)?.Code, row.AllocationPercent, row.EffectiveDate, row.Status, row.Reason, row.WorkflowInstanceId, row.CreatedDate))];
    }

    /// <summary>Asks to move someone (spec §69). Without an approval workflow the move happens at once.</summary>
    public async Task<TransferItem> RequestTransferAsync(TransferBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ProjectAllocation from = (await projects.AllocationsAsync(null, DateOnly.MinValue, DateOnly.MaxValue, cancellationToken)).FirstOrDefault(row => row.MemberId == body.FromMemberId && row.IsActive)
            ?? throw new DomainRuleException("RESOURCE_TRANSFER_FROM", "Choose a current allocation to move the person from.");

        if (await dbContext.ResourceTransfers.AnyAsync(row => row.FromMemberId == body.FromMemberId && row.Status == TransferStatus.Pending, cancellationToken))
        {
            throw new DomainRuleException("RESOURCE_TRANSFER_PENDING", "A transfer for this allocation is already awaiting a decision.");
        }

        ProjectSummary target = (await projects.GetManyAsync([body.ToProjectId], cancellationToken)).GetValueOrDefault(body.ToProjectId)
            ?? throw new DomainRuleException("RESOURCE_TRANSFER_TO", "The project to move to does not exist.");

        var transfer = ResourceTransfer.Request(from.EmployeeId, from.MemberId, from.ProjectId, target.Id, body.ProjectRoleId, body.AllocationPercent, body.EffectiveDate, body.EndDate, body.Billable, body.Reason);
        dbContext.ResourceTransfers.Add(transfer);
        await dbContext.SaveChangesAsync(cancellationToken);

        WorkflowStartResult started = await workflow.StartAsync(
            new WorkflowStartRequest
            {
                SubjectType = TransferSubjectType,
                SubjectId = transfer.Id,
                Title = $"Move to {target.Code} from {from.ProjectCode} on {body.EffectiveDate:d MMM yyyy}",
                RequesterEmployeeId = currentUser.EmployeeId,
                Link = "/resources/transfers",
                Fields = new Dictionary<string, string> { ["allocationPercent"] = body.AllocationPercent.ToString(CultureInfo.InvariantCulture) },
            },
            cancellationToken);

        if (started.Outcome == WorkflowStartOutcome.Pending)
        {
            transfer.AwaitApproval(started.InstanceId!.Value);
        }
        else
        {
            await CompleteAsync(transfer, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await TransfersAsync(null, cancellationToken)).First(row => row.Id == transfer.Id);
    }

    public async Task CancelTransferAsync(Guid id, CancellationToken cancellationToken)
    {
        ResourceTransfer transfer = await dbContext.ResourceTransfers.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(ResourceTransfer), id);
        transfer.Cancel();
        await workflow.WithdrawAsync(TransferSubjectType, transfer.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Workflow's verdict on a transfer, applied by the worker.</summary>
    public async Task ApplyTransferDecisionAsync(Guid transferId, bool approved, CancellationToken cancellationToken)
    {
        ResourceTransfer? transfer = await dbContext.ResourceTransfers.FirstOrDefaultAsync(row => row.Id == transferId, cancellationToken);
        if (transfer?.Status != TransferStatus.Pending)
        {
            return;
        }

        if (approved)
        {
            await CompleteAsync(transfer, cancellationToken);
        }
        else
        {
            transfer.Reject(Now);
        }
    }

    // ----- Internals ----------------------------------------------------------------------------

    /// <summary>Releases from the old project the day before and allocates on the new one, keeping both rows as history.</summary>
    private async Task CompleteAsync(ResourceTransfer transfer, CancellationToken cancellationToken)
    {
        await staffing.ReleaseAsync(transfer.FromMemberId, transfer.EffectiveDate.AddDays(-1), cancellationToken);
        ResourceProfile? profile = await dbContext.ResourceProfiles.AsNoTracking().FirstOrDefaultAsync(row => row.EmployeeId == transfer.EmployeeId, cancellationToken);
        Guid memberId = await staffing.AllocateAsync(
            new StaffingRequest(transfer.ToProjectId, transfer.EmployeeId, transfer.ProjectRoleId, transfer.EffectiveDate, transfer.EndDate, transfer.AllocationPercent,
                transfer.Billable, profile?.BillingRate, profile?.CostRate, $"Transferred: {transfer.Reason}"),
            cancellationToken);
        transfer.Complete(memberId, Now);

        ProjectSummary? target = (await projects.GetManyAsync([transfer.ToProjectId], cancellationToken)).GetValueOrDefault(transfer.ToProjectId);
        await notifier.NotifyAsync(
            new NotificationRequest(
                NotificationTemplates.ResourceTransferDecided,
                [new NotificationRecipient(transfer.EmployeeId)],
                new Dictionary<string, string>
                {
                    ["projectName"] = target?.Name ?? string.Empty,
                    ["effectiveDate"] = transfer.EffectiveDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
                },
                $"/projects/{transfer.ToProjectId}"),
            cancellationToken);
    }

    private async Task<AllocationOutcome> OutcomeAsync(Guid memberId, Guid employeeId, DateOnly from, DateOnly? to, CancellationToken cancellationToken)
    {
        DateOnly end = to ?? from.AddMonths(6);
        IReadOnlyList<ProjectAllocation> allocations = await projects.AllocationsAsync([employeeId], from, end, cancellationToken);
        int peak = Capacity.PeakAllocation(from, end, allocations.Where(row => row.IsActive).Select(Span));
        return new AllocationOutcome(memberId, peak, peak > 100);
    }

    /// <summary>Active people the caller may see under <c>resource.profile.view</c>.</summary>
    internal async Task<List<EmployeeSummary>> PeopleAsync(Guid? departmentId, Guid? locationId, string? search, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Resource.ProfileView, cancellationToken);
        IReadOnlyList<Guid> active = await employees.ListActiveIdsAsync(departmentId is { } department ? [department] : null, locationId is { } location ? [location] : null, cancellationToken);
        IEnumerable<Guid> ids = scope is null ? active : active.Where(scope.Contains);
        IEnumerable<EmployeeSummary> people = await employees.GetManyAsync(ids, cancellationToken);

        if (departmentId is not null && locationId is not null)
        {
            people = people.Where(person => person.DepartmentId == departmentId && person.LocationId == locationId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            people = people.Where(person => person.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) || person.EmployeeCode.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return [.. people];
    }

    internal async Task<Dictionary<Guid, ResourceProfile>> ProfilesAsync(IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken)
        => await dbContext.ResourceProfiles.AsNoTracking().Where(row => employeeIds.Contains(row.EmployeeId)).ToDictionaryAsync(row => row.EmployeeId, cancellationToken);

    private async Task<bool> CanSeeRatesAsync(CancellationToken cancellationToken)
        => (await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Resource.ProfileManage);

    internal static AllocationSpan Span(ProjectAllocation row) => new(row.ProjectId, row.ProjectCode, row.AllocationPercent, row.StartDate, row.EndDate, row.Billable);

    internal static decimal YearsSince(DateOnly? date, DateOnly today) => date is { } start ? Math.Round((today.DayNumber - start.DayNumber) / 365.25m, 1) : 0;

    internal static IReadOnlyList<SkillTag> Tags(IEnumerable<EmployeeSkillLevel> levels, int take)
        => [.. levels.OrderByDescending(level => level.IsPrimary).ThenByDescending(level => level.Level).Take(take).Select(level => new SkillTag(level.SkillId, level.SkillName, level.Level, level.IsPrimary))];

    private static CapacityCell CellToday(DateOnly today, PersonCalendar? personCalendar, IEnumerable<ProjectAllocation> allocations)
    {
        // Today may be a day off; capacity is then judged as though it were a working day.
        IReadOnlyCollection<DateOnly> days = personCalendar is { WorkingDays.Count: > 0 } ? personCalendar.WorkingDays : [today];
        return Capacity.For(today, today, days, personCalendar?.LeaveFractions ?? new Dictionary<DateOnly, decimal>(), allocations.Where(row => row.IsActive).Select(Span));
    }

    private async Task<Names> NamesAsync(IReadOnlyCollection<EmployeeSummary> people, CancellationToken cancellationToken)
        => new(
            await orgStructure.GetNamesAsync(OrgUnitKind.Department, people.Select(person => person.DepartmentId).OfType<Guid>().Distinct(), cancellationToken),
            await orgStructure.GetNamesAsync(OrgUnitKind.Location, people.Select(person => person.LocationId).OfType<Guid>().Distinct(), cancellationToken));

    private sealed record Names(IReadOnlyDictionary<Guid, string> Departments, IReadOnlyDictionary<Guid, string> Locations)
    {
        public string? Department(Guid? id) => id is { } value ? Departments.GetValueOrDefault(value) : null;

        public string? Location(Guid? id) => id is { } value ? Locations.GetValueOrDefault(value) : null;
    }
}
