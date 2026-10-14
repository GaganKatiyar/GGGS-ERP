using System.Globalization;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Data;
using Gggs.Modules.Employee.Infrastructure;
using Gggs.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gggs.Modules.Employee.Application;

/// <summary>
/// One row of a bulk hire, as a spreadsheet holds it: codes and text, not ids. Dates are
/// <c>yyyy-MM-dd</c>. A manager may be someone earlier in the same file.
/// </summary>
public sealed record EmployeeImportRow(
    string? EmployeeCode,
    string? FirstName,
    string? MiddleName,
    string? LastName,
    string? OfficialEmail,
    string? MobilePhone,
    string? JoiningDate,
    string? DepartmentCode,
    string? PositionCode,
    string? JobCode,
    string? GradeCode,
    string? LocationCode,
    string? ManagerEmployeeCode,
    string? EmploymentTypeCode,
    string? WorkModeCode,
    string? Designation);

public sealed record EmployeeImportRequest(IReadOnlyList<EmployeeImportRow> Rows);

public sealed record ImportRowResult(int RowNumber, string? EmployeeCode, bool Succeeded, IReadOnlyList<string> Errors);

public sealed record EmployeeImportResult(bool Committed, int Total, int Succeeded, int Failed, IReadOnlyList<ImportRowResult> Rows);

/// <summary>
/// Bulk hire (spec §116) with a preview, per-row errors and all-or-nothing commit.
/// <para>
/// Every row runs through the same <see cref="EmployeeService.HireAsync"/> a single hire uses, inside
/// one transaction - so the preview reports exactly what a real import would hit, including clashes
/// between rows of the same file. A preview always rolls back. A commit keeps the rows only if every
/// one of them succeeded; otherwise nothing is written and the errors come back.
/// </para>
/// </summary>
public sealed class EmployeeImportService(AppDbContext dbContext, EmployeeService employees, IOrgStructure orgStructure)
{
    public const int MaxRows = 1000;

    public async Task<EmployeeImportResult> RunAsync(EmployeeImportRequest request, bool commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Rows.Count == 0)
        {
            throw new DomainRuleException("IMPORT_EMPTY", "The file has no rows.");
        }

        if (request.Rows.Count > MaxRows)
        {
            throw new DomainRuleException("IMPORT_TOO_LARGE", $"Import at most {MaxRows} rows at a time.");
        }

        CodeMaps codes = await ResolveCodesAsync(request.Rows, cancellationToken);
        var results = new List<ImportRowResult>(request.Rows.Count);
        bool keep = false;

        // Retriable as a unit: a transient failure replays the whole file, never half of it.
        IExecutionStrategy strategy = dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async ct =>
        {
            results.Clear();
            dbContext.ChangeTracker.Clear();

            await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync(ct);

            for (int index = 0; index < request.Rows.Count; index++)
            {
                results.Add(await ImportRowAsync(index + 2, request.Rows[index], codes, ct));
            }

            keep = commit && results.TrueForAll(result => result.Succeeded);

            if (keep)
            {
                await transaction.CommitAsync(ct);
            }
            else
            {
                await transaction.RollbackAsync(ct);
            }
        }, cancellationToken);

        dbContext.ChangeTracker.Clear();

        int succeeded = results.Count(result => result.Succeeded);
        return new EmployeeImportResult(keep, results.Count, succeeded, results.Count - succeeded, results);
    }

    /// <summary>Row numbers count the header as row 1, as a spreadsheet does.</summary>
    private async Task<ImportRowResult> ImportRowAsync(int rowNumber, EmployeeImportRow row, CodeMaps codes, CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(row.FirstName))
        {
            errors.Add("First name is required.");
        }

        if (!DateOnly.TryParseExact(row.JoiningDate?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly joiningDate))
        {
            errors.Add("Joining date must be a date written yyyy-MM-dd.");
        }

        Guid? department = Lookup(codes.Departments, row.DepartmentCode, "department", errors);
        Guid? position = Lookup(codes.Positions, row.PositionCode, "position", errors);
        Guid? job = Lookup(codes.Jobs, row.JobCode, "job", errors);
        Guid? grade = Lookup(codes.Grades, row.GradeCode, "grade", errors);
        Guid? location = Lookup(codes.Locations, row.LocationCode, "location", errors);

        Guid? manager = null;
        if (!string.IsNullOrWhiteSpace(row.ManagerEmployeeCode))
        {
            // Looked up now rather than up front: the manager may have been created by an earlier row.
            string managerCode = Domain.Employee.NormalizeCode(row.ManagerEmployeeCode);
            manager = await dbContext.Employees.Where(employee => employee.EmployeeCode == managerCode).Select(employee => (Guid?)employee.Id).FirstOrDefaultAsync(cancellationToken);
            if (manager is null)
            {
                errors.Add($"No employee has the manager code '{managerCode}'.");
            }
        }

        if (department is null && position is null && string.IsNullOrWhiteSpace(row.DepartmentCode) && string.IsNullOrWhiteSpace(row.PositionCode))
        {
            errors.Add("A department code (or a position code) is required.");
        }

        if (errors.Count > 0)
        {
            return new ImportRowResult(rowNumber, row.EmployeeCode, false, errors);
        }

        try
        {
            Domain.Employee hired = await employees.HireAsync(new HireEmployeeRequest(
                row.EmployeeCode, row.FirstName!, row.MiddleName, row.LastName, null, null, null, null, row.OfficialEmail, row.MobilePhone,
                joiningDate, null,
                new AssignmentInput(
                    department, position, job, grade, location, null, null, null, manager, null,
                    row.EmploymentTypeCode ?? string.Empty, row.WorkModeCode ?? string.Empty, row.Designation)),
                cancellationToken);

            return new ImportRowResult(rowNumber, hired.EmployeeCode, true, []);
        }
        catch (Exception exception) when (exception is DomainRuleException or ArgumentException or DbUpdateException)
        {
            // The failed row's half-built entities must not ride along on the next row's save.
            dbContext.ChangeTracker.Clear();
            string message = exception is DbUpdateException ? "The row conflicts with an existing record." : exception.Message;
            return new ImportRowResult(rowNumber, row.EmployeeCode, false, [message]);
        }
    }

    private static Guid? Lookup(IReadOnlyDictionary<string, Guid> codes, string? code, string field, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        if (codes.TryGetValue(code.Trim(), out Guid id))
        {
            return id;
        }

        errors.Add($"There is no active {field} with the code '{code.Trim()}'.");
        return null;
    }

    private async Task<CodeMaps> ResolveCodesAsync(IReadOnlyList<EmployeeImportRow> rows, CancellationToken cancellationToken) => new(
        await orgStructure.ResolveCodesAsync(OrgUnitKind.Department, rows.Select(row => row.DepartmentCode!), cancellationToken),
        await orgStructure.ResolveCodesAsync(OrgUnitKind.Position, rows.Select(row => row.PositionCode!), cancellationToken),
        await orgStructure.ResolveCodesAsync(OrgUnitKind.Job, rows.Select(row => row.JobCode!), cancellationToken),
        await orgStructure.ResolveCodesAsync(OrgUnitKind.Grade, rows.Select(row => row.GradeCode!), cancellationToken),
        await orgStructure.ResolveCodesAsync(OrgUnitKind.Location, rows.Select(row => row.LocationCode!), cancellationToken));

    private sealed record CodeMaps(
        IReadOnlyDictionary<string, Guid> Departments,
        IReadOnlyDictionary<string, Guid> Positions,
        IReadOnlyDictionary<string, Guid> Jobs,
        IReadOnlyDictionary<string, Guid> Grades,
        IReadOnlyDictionary<string, Guid> Locations);
}
