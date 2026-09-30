using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.CoreHr.Application.Organization;
using Gggs.CoreHr.Domain.Employees;
using Gggs.CoreHr.Domain.Organization;

namespace Gggs.CoreHr.Application.Employees;

/// <summary>One row of a bulk hire request - the same shape <see cref="EmployeeService.HireAsync"/> needs,
/// except organizational references are the codes a spreadsheet actually has, not GUIDs.</summary>
public sealed record BulkHireRow(
    int RowNumber,
    string EmployeeCode,
    string FirstName,
    string? MiddleName,
    string? LastName,
    string? WorkEmail,
    DateOnly DateOfJoining,
    string LegalEntityCode,
    string BranchCode,
    string DepartmentCode,
    string DesignationCode,
    string? GradeCode,
    EmploymentType EmploymentType,
    DateOnly? ProbationEndDate);

public sealed record BulkHireRowResult(int RowNumber, bool Succeeded, Guid? EmployeeId, string? ErrorCode, string? ErrorMessage);

public sealed record BulkHireResult(int TotalRows, int SuccessCount, int FailureCount, IReadOnlyList<BulkHireRowResult> Rows);

/// <summary>
/// Synchronous bulk employee import (architecture doc's <c>POST /employees/bulk</c> ≤500-row carve-out):
/// no import job, no async infrastructure, one row at a time through the exact same
/// <see cref="EmployeeService.HireAsync"/> path a single hire uses, so a bulk import can never bypass
/// a rule the single-hire endpoint enforces.
/// <para>
/// Each row is resolved and hired independently and one bad row (typo'd code, duplicate employee code)
/// never fails the rows around it - the caller gets back a per-row result and decides what to do with
/// the failures, matching the import framework's own "row-level errors" principle even in this
/// lighter, synchronous form.
/// </para>
/// </summary>
public sealed class EmployeeBulkImportService(EmployeeService employeeService, IOrganizationRepository organization)
{
    public const int MaxRows = 500;

    public async Task<BulkHireResult> HireBulkAsync(IReadOnlyList<BulkHireRow> rows, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Count == 0)
        {
            throw new DomainRuleException("BULK_IMPORT_EMPTY", "At least one row is required.");
        }

        if (rows.Count > MaxRows)
        {
            throw new DomainRuleException("BULK_IMPORT_TOO_LARGE", $"A bulk import accepts at most {MaxRows} rows; got {rows.Count}.");
        }

        var results = new List<BulkHireRowResult>(rows.Count);

        foreach (BulkHireRow row in rows)
        {
            try
            {
                AssignmentDetails assignment = await ResolveAssignmentAsync(row, cancellationToken);

                var employee = await employeeService.HireAsync(
                    row.EmployeeCode, row.FirstName, row.MiddleName, row.LastName, row.WorkEmail,
                    row.DateOfJoining, assignment, cancellationToken);

                results.Add(new BulkHireRowResult(row.RowNumber, true, employee.Id, null, null));
            }
            catch (GggsException ex)
            {
                results.Add(new BulkHireRowResult(row.RowNumber, false, null, ex.ErrorCode, ex.Message));
            }
        }

        int successCount = results.Count(r => r.Succeeded);
        return new BulkHireResult(rows.Count, successCount, rows.Count - successCount, results);
    }

    private async Task<AssignmentDetails> ResolveAssignmentAsync(BulkHireRow row, CancellationToken cancellationToken)
    {
        LegalEntity legalEntity = await organization.GetLegalEntityByCodeAsync(OrganizationCode.Normalize(row.LegalEntityCode), cancellationToken)
            ?? throw new NotFoundException(nameof(LegalEntity), row.LegalEntityCode);

        Branch branch = await organization.GetBranchByCodeAsync(OrganizationCode.Normalize(row.BranchCode), cancellationToken)
            ?? throw new NotFoundException(nameof(Branch), row.BranchCode);

        Department department = await organization.GetDepartmentByCodeAsync(OrganizationCode.Normalize(row.DepartmentCode), cancellationToken)
            ?? throw new NotFoundException(nameof(Department), row.DepartmentCode);

        Designation designation = await organization.GetDesignationByCodeAsync(OrganizationCode.Normalize(row.DesignationCode), cancellationToken)
            ?? throw new NotFoundException(nameof(Designation), row.DesignationCode);

        Guid? gradeId = null;
        if (!string.IsNullOrWhiteSpace(row.GradeCode))
        {
            Grade grade = await organization.GetGradeByCodeAsync(OrganizationCode.Normalize(row.GradeCode), cancellationToken)
                ?? throw new NotFoundException(nameof(Grade), row.GradeCode);
            gradeId = grade.Id;
        }

        return new AssignmentDetails(
            legalEntity.Id, branch.Id, department.Id, designation.Id, gradeId,
            CostCenterId: null, ReportingManagerId: null, row.EmploymentType, row.ProbationEndDate);
    }
}
