using System.Linq.Expressions;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Domain;
using Gggs.Modules.Employee.Infrastructure;
using Gggs.Modules.MasterData.Contracts;
using Gggs.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;
using EmployeeEntity = Gggs.Modules.Employee.Domain.Employee;

namespace Gggs.Modules.Employee.Application;

public sealed record EmployeeListQuery
{
    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public string? Sort { get; init; }

    /// <summary>Matches name, code or official email.</summary>
    public string? Search { get; init; }

    public Guid? DepartmentId { get; init; }

    public Guid? LocationId { get; init; }

    public Guid? ManagerEmployeeId { get; init; }

    public EmployeeStatus? Status { get; init; }

    /// <summary>Exited employees are left out unless asked for.</summary>
    public bool? IncludeExited { get; init; }

    public PageRequest ToPageRequest() => new() { Page = Page ?? 1, PageSize = PageSize ?? PageRequest.DefaultPageSize, Sort = Sort, Search = Search };
}

public sealed record NamedRef(Guid Id, string Name);

public sealed record CodedValue(string Code, string Label);

public sealed record EmployeeListItem(
    Guid Id,
    string EmployeeCode,
    string DisplayName,
    string? OfficialEmail,
    string? Designation,
    EmployeeStatus Status,
    DateOnly JoiningDate,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? ManagerEmployeeId,
    string? ManagerName,
    Guid? LocationId,
    string? LocationName);

public sealed record AddressView(string? Line1, string? Line2, string? City, string? StateCode, string? CountryCode, string? PostalCode);

public sealed record CurrentAssignmentView(
    DateOnly EffectiveFrom,
    NamedRef? LegalEntity,
    NamedRef? BusinessUnit,
    NamedRef? Division,
    NamedRef? Department,
    NamedRef? Job,
    NamedRef? Position,
    NamedRef? Grade,
    NamedRef? Location,
    NamedRef? Manager,
    NamedRef? Hrbp,
    CodedValue EmploymentType,
    CodedValue WorkMode,
    string? Designation);

/// <summary>Statutory and bank details as most callers see them: masked hints, never the values.</summary>
public sealed record SensitiveSummary(string? PanHint, string? UanHint, string? EsicHint, string? NationalIdHint, string? ProfessionalTaxStateCode, string? BankName, string? BankIfsc, string? BankAccountHint);

public sealed record EmployeeDetail(
    Guid Id,
    string EmployeeCode,
    string FirstName,
    string? MiddleName,
    string? LastName,
    string? PreferredName,
    string DisplayName,
    CodedValue? Gender,
    DateOnly? DateOfBirth,
    CodedValue? MaritalStatus,
    CodedValue? Nationality,
    CodedValue? BloodGroup,
    string? PersonalEmail,
    string? OfficialEmail,
    string? MobilePhone,
    string? AlternatePhone,
    AddressView? CurrentAddress,
    AddressView? PermanentAddress,
    EmployeeStatus Status,
    DateOnly JoiningDate,
    DateOnly? ProbationEndDate,
    DateOnly? ConfirmationDate,
    DateOnly? NoticeStartDate,
    DateOnly? ExitDate,
    CodedValue? ExitReason,
    CurrentAssignmentView Assignment,
    SensitiveSummary Sensitive);

public sealed record StatutoryView(string? Pan, string? Uan, string? EsicNumber, string? NationalId, string? ProfessionalTaxStateCode);

public sealed record BankView(string? BankName, string? Ifsc, string? AccountHolder, string? AccountNumber);

public sealed record EmploymentHistoryItem(
    Guid Id,
    AssignmentChangeType ChangeType,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string? DepartmentName,
    string? JobName,
    string? PositionName,
    string? GradeName,
    string? LocationName,
    string? ManagerName,
    string? Designation,
    string EmploymentTypeCode,
    string WorkModeCode,
    string? Reason);

public sealed record DependentItem(Guid Id, string FullName, string RelationshipCode, DateOnly? DateOfBirth, string? GenderCode);

public sealed record EmergencyContactItem(Guid Id, string FullName, string RelationshipCode, string Phone, string? Email, bool IsPrimary);

/// <summary>One section of Employee 360, and whether it has anything behind it yet.</summary>
public sealed record Employee360Section(string Key, string Title, bool Available, string? Feature, string? Milestone);

public sealed record Employee360(
    EmployeeDetail Profile,
    int DirectReportCount,
    int TenureMonths,
    IReadOnlyList<Employee360Section> Sections);

public sealed record OrgChartNode(Guid Id, string DisplayName, string? Designation, string? DepartmentName, int DirectReportCount, IReadOnlyList<OrgChartNode> Reports);

/// <summary>
/// The read side of Core HR. Everything here goes through <see cref="EmployeeScope"/>, so a list,
/// a profile and a history all agree on what the caller may see.
/// </summary>
public sealed class EmployeeQueries(
    AppDbContext dbContext,
    EmployeeScope scope,
    IOrgStructure orgStructure,
    IMasterDataLookup lookups,
    ISensitiveDataProtector protector,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
{
    private static readonly Dictionary<string, Expression<Func<EmployeeEntity, object?>>> Sortable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = employee => employee.DisplayName,
        ["code"] = employee => employee.EmployeeCode,
        ["joiningDate"] = employee => employee.JoiningDate,
        ["status"] = employee => employee.Status,
        ["designation"] = employee => employee.Designation,
    };

    public async Task<PagedResult<EmployeeListItem>> ListAsync(EmployeeListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        PageRequest page = query.ToPageRequest();

        IQueryable<EmployeeEntity> rows = await scope.ApplyAsync(dbContext.Employees.AsNoTracking(), Permissions.Employee.View, cancellationToken);

        if (!string.IsNullOrWhiteSpace(page.Search))
        {
            string term = page.Search.Trim();
            rows = rows.Where(employee =>
                employee.DisplayName.Contains(term) ||
                employee.EmployeeCode.Contains(term) ||
                (employee.OfficialEmail != null && employee.OfficialEmail.Contains(term)));
        }

        if (query.DepartmentId is { } departmentId)
        {
            rows = rows.Where(employee => employee.DepartmentId == departmentId);
        }

        if (query.LocationId is { } locationId)
        {
            rows = rows.Where(employee => employee.LocationId == locationId);
        }

        if (query.ManagerEmployeeId is { } managerId)
        {
            rows = rows.Where(employee => employee.ManagerEmployeeId == managerId);
        }

        if (query.Status is { } status)
        {
            rows = rows.Where(employee => employee.Status == status);
        }
        else if (query.IncludeExited != true)
        {
            rows = rows.Where(employee => employee.Status != EmployeeStatus.Exited);
        }

        PagedResult<EmployeeRow> result = await rows
            .ApplySort(page.Sort, Sortable, "name")
            .ToPagedResultAsync(page, employee => new EmployeeRow(
                employee.Id, employee.EmployeeCode, employee.DisplayName, employee.OfficialEmail, employee.Designation, employee.Status,
                employee.JoiningDate, employee.DepartmentId, employee.ManagerEmployeeId, employee.LocationId), cancellationToken);

        // Names for the page in three batched lookups, not one per row.
        IReadOnlyDictionary<Guid, string> departments = await orgStructure.GetNamesAsync(OrgUnitKind.Department, result.Items.Select(row => row.DepartmentId).OfType<Guid>(), cancellationToken);
        IReadOnlyDictionary<Guid, string> locations = await orgStructure.GetNamesAsync(OrgUnitKind.Location, result.Items.Select(row => row.LocationId).OfType<Guid>(), cancellationToken);
        Dictionary<Guid, string> managers = await NamesOfAsync(result.Items.Select(row => row.ManagerEmployeeId), cancellationToken);

        return PagedResult.From<EmployeeListItem>(
            [
                .. result.Items.Select(row => new EmployeeListItem(
                    row.Id, row.EmployeeCode, row.DisplayName, row.OfficialEmail, row.Designation, row.Status, row.JoiningDate,
                    row.DepartmentId, Name(departments, row.DepartmentId),
                    row.ManagerEmployeeId, Name(managers, row.ManagerEmployeeId),
                    row.LocationId, Name(locations, row.LocationId))),
            ],
            page,
            result.TotalCount);
    }

    public async Task<EmployeeDetail> GetAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => await GetAsync(employeeId, Permissions.Employee.View, cancellationToken);

    /// <summary>The caller's own record, under the self-service permission.</summary>
    public async Task<EmployeeDetail> GetSelfAsync(Guid employeeId, CancellationToken cancellationToken = default)
        => await BuildDetailAsync(await LoadAsync(dbContext.Employees.AsNoTracking().Where(employee => employee.Id == employeeId), employeeId, cancellationToken), cancellationToken);

    public async Task<EmployeeDetail> GetAsync(Guid employeeId, string permission, CancellationToken cancellationToken)
    {
        IQueryable<EmployeeEntity> visible = await scope.ApplyAsync(dbContext.Employees.AsNoTracking(), permission, cancellationToken);
        EmployeeEntity employee = await LoadAsync(visible, employeeId, cancellationToken);
        return await BuildDetailAsync(employee, cancellationToken);
    }

    /// <summary>The real statutory values. Only for callers holding the identity-document permission; the endpoint checks it.</summary>
    public async Task<StatutoryView> RevealStatutoryAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        IQueryable<EmployeeEntity> visible = await scope.ApplyAsync(dbContext.Employees.AsNoTracking(), Permissions.Employee.IdentityDocumentView, cancellationToken);
        EmployeeEntity employee = await LoadAsync(visible, employeeId, cancellationToken);

        return new StatutoryView(
            Reveal(SensitiveDataPurposes.Pan, employee.Pan),
            Reveal(SensitiveDataPurposes.Uan, employee.Uan),
            Reveal(SensitiveDataPurposes.Esic, employee.EsicNumber),
            Reveal(SensitiveDataPurposes.NationalId, employee.NationalId),
            employee.ProfessionalTaxStateCode);
    }

    /// <summary>The real bank account number. Only for callers holding the bank permission; the endpoint checks it.</summary>
    public async Task<BankView> RevealBankAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        IQueryable<EmployeeEntity> visible = await scope.ApplyAsync(dbContext.Employees.AsNoTracking(), Permissions.Employee.BankView, cancellationToken);
        EmployeeEntity employee = await LoadAsync(visible, employeeId, cancellationToken);

        return new BankView(employee.BankName, employee.BankIfsc, employee.BankAccountHolder, Reveal(SensitiveDataPurposes.BankAccount, employee.BankAccountNumber));
    }

    public async Task<IReadOnlyList<EmploymentHistoryItem>> HistoryAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        IQueryable<EmployeeEntity> visible = await scope.ApplyAsync(dbContext.Employees.AsNoTracking(), Permissions.Employee.HistoryView, cancellationToken);
        if (!await visible.AnyAsync(employee => employee.Id == employeeId, cancellationToken))
        {
            throw new NotFoundException("Employee", employeeId);
        }

        List<EmploymentRecord> records = await dbContext.EmploymentRecords
            .AsNoTracking()
            .Where(record => record.EmployeeId == employeeId)
            .OrderByDescending(record => record.EffectiveFrom)
            .ToListAsync(cancellationToken);

        IReadOnlyDictionary<Guid, string> departments = await orgStructure.GetNamesAsync(OrgUnitKind.Department, records.Select(record => record.DepartmentId), cancellationToken);
        IReadOnlyDictionary<Guid, string> jobs = await orgStructure.GetNamesAsync(OrgUnitKind.Job, records.Select(record => record.JobId).OfType<Guid>(), cancellationToken);
        IReadOnlyDictionary<Guid, string> positions = await orgStructure.GetNamesAsync(OrgUnitKind.Position, records.Select(record => record.PositionId).OfType<Guid>(), cancellationToken);
        IReadOnlyDictionary<Guid, string> grades = await orgStructure.GetNamesAsync(OrgUnitKind.Grade, records.Select(record => record.GradeId).OfType<Guid>(), cancellationToken);
        IReadOnlyDictionary<Guid, string> locations = await orgStructure.GetNamesAsync(OrgUnitKind.Location, records.Select(record => record.LocationId).OfType<Guid>(), cancellationToken);
        Dictionary<Guid, string> managers = await NamesOfAsync(records.Select(record => record.ManagerEmployeeId), cancellationToken);

        return
        [
            .. records.Select(record => new EmploymentHistoryItem(
                record.Id, record.ChangeType, record.EffectiveFrom, record.EffectiveTo,
                Name(departments, record.DepartmentId), Name(jobs, record.JobId), Name(positions, record.PositionId),
                Name(grades, record.GradeId), Name(locations, record.LocationId), Name(managers, record.ManagerEmployeeId),
                record.Designation, record.EmploymentTypeCode, record.WorkModeCode, record.Reason)),
        ];
    }

    public async Task<IReadOnlyList<DependentItem>> DependentsAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        await EnsureVisibleAsync(employeeId, cancellationToken);
        return await dbContext.EmployeeDependents.AsNoTracking()
            .Where(dependent => dependent.EmployeeId == employeeId)
            .OrderBy(dependent => dependent.FullName)
            .Select(dependent => new DependentItem(dependent.Id, dependent.FullName, dependent.RelationshipCode, dependent.DateOfBirth, dependent.GenderCode))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EmergencyContactItem>> EmergencyContactsAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        await EnsureVisibleAsync(employeeId, cancellationToken);
        return await dbContext.EmergencyContacts.AsNoTracking()
            .Where(contact => contact.EmployeeId == employeeId)
            .OrderByDescending(contact => contact.IsPrimary)
            .ThenBy(contact => contact.FullName)
            .Select(contact => new EmergencyContactItem(contact.Id, contact.FullName, contact.RelationshipCode, contact.Phone, contact.Email, contact.IsPrimary))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Employee 360 (spec §15, §121): the profile plus a map of every section and whether it has
    /// anything behind it yet. Sections light up as their modules ship and the tenant's plan
    /// includes them; the page never has to know which milestone it is.
    /// </summary>
    public async Task<Employee360> Get360Async(Guid employeeId, CancellationToken cancellationToken = default)
    {
        EmployeeDetail profile = await GetAsync(employeeId, cancellationToken);

        int directReports = await dbContext.Employees.CountAsync(
            employee => employee.ManagerEmployeeId == employeeId && employee.Status != EmployeeStatus.Exited, cancellationToken);

        DateOnly until = profile.ExitDate ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        int tenureMonths = Math.Max(0, ((until.Year - profile.JoiningDate.Year) * 12) + until.Month - profile.JoiningDate.Month - (until.Day < profile.JoiningDate.Day ? 1 : 0));

        IReadOnlyList<string> features = tenantContext.Tenant?.EnabledFeatures ?? [];
        bool Has(string? feature) => feature is null || features.Contains(feature, StringComparer.OrdinalIgnoreCase);

        Employee360Section Section(string key, string title, bool built, string? feature, string? milestone)
            => new(key, title, built && Has(feature), feature, built ? null : milestone);

        return new Employee360(profile, directReports, tenureMonths,
        [
            Section("overview", "Overview", true, null, null),
            Section("employment", "Employment", true, null, null),
            Section("organization", "Organization", true, null, null),
            Section("personal", "Personal", true, null, null),
            Section("family", "Family & emergency", true, null, null),
            Section("statutory", "Statutory & bank", true, null, null),
            Section("history", "History", true, null, null),
            Section("onboarding", "Onboarding", true, Features.Onboarding, null),
            Section("attendance", "Attendance", true, Features.Time, null),
            Section("leave", "Leave", true, Features.Time, null),
            Section("projects", "Projects", true, Features.Projects, null),
            Section("resources", "Allocation", true, Features.Resources, null),
            Section("timesheet", "Timesheet", true, Features.Timesheets, null),
            Section("skills", "Skills", true, null, null),
            Section("payroll", "Payroll", true, Features.Payroll, null),
            Section("compensation", "Compensation", true, Features.Compensation, null),
            Section("benefits", "Benefits", true, Features.Benefits, null),
            Section("performance", "Performance", true, Features.Performance, null),
            Section("goals", "Goals", true, Features.Performance, null),
            Section("learning", "Learning", true, Features.Learning, null),
            Section("career", "Career", true, Features.Career, null),
            Section("rewards", "Rewards", false, Features.Rewards, "M10"),
            Section("documents", "Documents", true, Features.Documents, null),
            Section("assets", "Assets", false, Features.Assets, "M10"),
            Section("expenses", "Expenses", false, Features.Expenses, "M10"),
            Section("helpdesk", "Helpdesk", false, Features.Helpdesk, "M10"),
        ]);
    }

    /// <summary>The reporting tree below one employee (or the people with no manager), to a bounded depth.</summary>
    public async Task<IReadOnlyList<OrgChartNode>> OrgChartAsync(Guid? rootEmployeeId, int depth, CancellationToken cancellationToken = default)
    {
        IQueryable<EmployeeEntity> visible = await scope.ApplyAsync(dbContext.Employees.AsNoTracking(), Permissions.Employee.View, cancellationToken);

        var people = await visible
            .Where(employee => employee.Status != EmployeeStatus.Exited)
            .Select(employee => new { employee.Id, employee.DisplayName, employee.Designation, employee.DepartmentId, employee.ManagerEmployeeId })
            .ToListAsync(cancellationToken);

        IReadOnlyDictionary<Guid, string> departments = await orgStructure.GetNamesAsync(OrgUnitKind.Department, people.Select(person => person.DepartmentId).OfType<Guid>(), cancellationToken);
        ILookup<Guid?, Guid> reportsOf = people.ToLookup(person => person.ManagerEmployeeId, person => person.Id);
        var byId = people.ToDictionary(person => person.Id);

        int limit = Math.Clamp(depth, 1, 10);

        OrgChartNode Node(Guid id, int level)
        {
            var person = byId[id];
            List<Guid> reports = [.. reportsOf[id].Where(byId.ContainsKey)];
            return new OrgChartNode(
                id, person.DisplayName, person.Designation, Name(departments, person.DepartmentId), reports.Count,
                level >= limit ? [] : [.. reports.Select(report => Node(report, level + 1)).OrderBy(node => node.DisplayName)]);
        }

        if (rootEmployeeId is { } root)
        {
            return byId.ContainsKey(root) ? [Node(root, 1)] : throw new NotFoundException("Employee", root);
        }

        // Tops of the visible tree: nobody above them that the caller can see.
        return [.. people.Where(person => person.ManagerEmployeeId is null || !byId.ContainsKey(person.ManagerEmployeeId.Value))
            .Select(person => Node(person.Id, 1))
            .OrderBy(node => node.DisplayName)];
    }

    private async Task EnsureVisibleAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        IQueryable<EmployeeEntity> visible = await scope.ApplyAsync(dbContext.Employees.AsNoTracking(), Permissions.Employee.View, cancellationToken);
        if (!await visible.AnyAsync(employee => employee.Id == employeeId, cancellationToken))
        {
            throw new NotFoundException("Employee", employeeId);
        }
    }

    private static async Task<EmployeeEntity> LoadAsync(IQueryable<EmployeeEntity> query, Guid employeeId, CancellationToken cancellationToken)
        => await query.FirstOrDefaultAsync(employee => employee.Id == employeeId, cancellationToken)
            ?? throw new NotFoundException("Employee", employeeId);

    private async Task<EmployeeDetail> BuildDetailAsync(EmployeeEntity employee, CancellationToken cancellationToken)
    {
        async Task<NamedRef?> Org(OrgUnitKind kind, Guid? id)
        {
            if (id is null)
            {
                return null;
            }

            IReadOnlyDictionary<Guid, string> names = await orgStructure.GetNamesAsync(kind, [id.Value], cancellationToken);
            return names.TryGetValue(id.Value, out string? name) ? new NamedRef(id.Value, name) : null;
        }

        async Task<CodedValue?> Lookup(string list, string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return null;
            }

            IReadOnlyDictionary<string, string> labels = await lookups.GetLabelsAsync(list, [code], cancellationToken);
            return new CodedValue(code, labels.GetValueOrDefault(code) ?? code);
        }

        Dictionary<Guid, string> people = await NamesOfAsync([employee.ManagerEmployeeId, employee.HrbpEmployeeId], cancellationToken);
        DateOnly effectiveFrom = await dbContext.EmploymentRecords.AsNoTracking()
            .Where(record => record.EmployeeId == employee.Id)
            .OrderByDescending(record => record.EffectiveFrom)
            .Select(record => record.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

        var assignment = new CurrentAssignmentView(
            effectiveFrom,
            await Org(OrgUnitKind.LegalEntity, employee.LegalEntityId),
            await Org(OrgUnitKind.BusinessUnit, employee.BusinessUnitId),
            await Org(OrgUnitKind.Division, employee.DivisionId),
            await Org(OrgUnitKind.Department, employee.DepartmentId),
            await Org(OrgUnitKind.Job, employee.JobId),
            await Org(OrgUnitKind.Position, employee.PositionId),
            await Org(OrgUnitKind.Grade, employee.GradeId),
            await Org(OrgUnitKind.Location, employee.LocationId),
            employee.ManagerEmployeeId is { } managerId && people.TryGetValue(managerId, out string? manager) ? new NamedRef(managerId, manager) : null,
            employee.HrbpEmployeeId is { } hrbpId && people.TryGetValue(hrbpId, out string? hrbp) ? new NamedRef(hrbpId, hrbp) : null,
            await Lookup(LookupLists.EmploymentType, employee.EmploymentTypeCode) ?? new CodedValue(employee.EmploymentTypeCode, employee.EmploymentTypeCode),
            await Lookup(LookupLists.WorkMode, employee.WorkModeCode) ?? new CodedValue(employee.WorkModeCode, employee.WorkModeCode),
            employee.Designation);

        return new EmployeeDetail(
            employee.Id, employee.EmployeeCode, employee.FirstName, employee.MiddleName, employee.LastName, employee.PreferredName, employee.DisplayName,
            await Lookup(LookupLists.Gender, employee.GenderCode), employee.DateOfBirth,
            await Lookup(LookupLists.MaritalStatus, employee.MaritalStatusCode),
            await Lookup(LookupLists.Country, employee.NationalityCode),
            await Lookup(LookupLists.BloodGroup, employee.BloodGroupCode),
            employee.PersonalEmail, employee.OfficialEmail, employee.MobilePhone, employee.AlternatePhone,
            ToView(employee.CurrentAddress), ToView(employee.PermanentAddress),
            employee.Status, employee.JoiningDate, employee.ProbationEndDate, employee.ConfirmationDate, employee.NoticeStartDate, employee.ExitDate,
            await Lookup(LookupLists.ExitReason, employee.ExitReasonCode),
            assignment,
            new SensitiveSummary(
                employee.Pan?.Hint, employee.Uan?.Hint, employee.EsicNumber?.Hint, employee.NationalId?.Hint, employee.ProfessionalTaxStateCode,
                employee.BankName, employee.BankIfsc, employee.BankAccountNumber?.Hint));
    }

    private async Task<Dictionary<Guid, string>> NamesOfAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        List<Guid> wanted = [.. ids.OfType<Guid>().Distinct()];
        if (wanted.Count == 0)
        {
            return [];
        }

        return await dbContext.Employees.AsNoTracking()
            .Where(employee => wanted.Contains(employee.Id))
            .ToDictionaryAsync(employee => employee.Id, employee => employee.DisplayName, cancellationToken);
    }

    private string? Reveal(string purpose, ProtectedValue? value) => value is null ? null : protector.Reveal(purpose, value);

    private static string? Name(IReadOnlyDictionary<Guid, string> names, Guid? id)
        => id is { } key && names.TryGetValue(key, out string? name) ? name : null;

    private static AddressView? ToView(HomeAddress? address)
        => address is null ? null : new AddressView(address.Line1, address.Line2, address.City, address.StateCode, address.CountryCode, address.PostalCode);

    private sealed record EmployeeRow(
        Guid Id, string EmployeeCode, string DisplayName, string? OfficialEmail, string? Designation, EmployeeStatus Status,
        DateOnly JoiningDate, Guid? DepartmentId, Guid? ManagerEmployeeId, Guid? LocationId);
}
