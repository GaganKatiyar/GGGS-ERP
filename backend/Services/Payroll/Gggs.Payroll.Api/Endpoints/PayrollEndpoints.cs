using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Payroll.Api.Contracts;
using Gggs.Payroll.Application.Payroll;
using Gggs.Payroll.Domain.Payroll;
using Gggs.Payroll.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Payroll.Api.Endpoints;

/// <summary>
/// Salary components, structures and their lines, employee salary assignment, payroll runs, and the
/// payslips a finalized run publishes. No statutory compliance in this v1 slice - see
/// <c>EmployeeSalary</c>'s, <c>PayrollRun</c>'s and <c>Payslip</c>'s own remarks.
/// </summary>
public static class PayrollEndpoints
{
    public static IEndpointRouteBuilder MapPayrollEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        MapComponentEndpoints(endpoints);
        MapStructureEndpoints(endpoints);
        MapEmployeeSalaryEndpoints(endpoints);
        MapPayGroupEndpoints(endpoints);
        MapPayPeriodEndpoints(endpoints);
        MapPayrollRunEndpoints(endpoints);
        MapPayslipEndpoints(endpoints);

        return endpoints;
    }

    private static void MapComponentEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/payroll/components").WithTags("Payroll");

        group.MapGet("/", ListComponentsAsync).WithName("ListSalaryComponents").RequirePermission(Permissions.Payroll.ComponentManage);
        group.MapPost("/", CreateComponentAsync).WithName("CreateSalaryComponent").RequirePermission(Permissions.Payroll.ComponentManage);
        group.MapPost("/{id:guid}/deactivate", DeactivateComponentAsync)
            .WithName("DeactivateSalaryComponent").RequirePermission(Permissions.Payroll.ComponentManage);
        group.MapPost("/{id:guid}/reactivate", ReactivateComponentAsync)
            .WithName("ReactivateSalaryComponent").RequirePermission(Permissions.Payroll.ComponentManage);
    }

    private static async Task<Ok<IReadOnlyList<SalaryComponentResponse>>> ListComponentsAsync(PayrollDbContext db, CancellationToken ct)
    {
        List<SalaryComponentResponse> items = await db.SalaryComponents.AsNoTracking()
            .OrderBy(x => x.DisplayOrder)
            .Select(x => ToResponse(x))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<SalaryComponentResponse>>(items);
    }

    private static async Task<Created<SalaryComponentResponse>> CreateComponentAsync(
        CreateSalaryComponentBody body, SalaryComponentService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        SalaryComponent entity = await service.CreateAsync(
            body.Code, body.Name, body.Kind, body.CalcType, body.IsTaxable, body.DisplayOrder, ct);

        return TypedResults.Created($"/api/v1/payroll/components/{entity.Id}", ToResponse(entity));
    }

    private static async Task<Ok<SalaryComponentResponse>> DeactivateComponentAsync(Guid id, SalaryComponentService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.DeactivateAsync(id, ct)));

    private static async Task<Ok<SalaryComponentResponse>> ReactivateComponentAsync(Guid id, SalaryComponentService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.ReactivateAsync(id, ct)));

    private static void MapStructureEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/payroll/structures").WithTags("Payroll");

        group.MapGet("/", ListStructuresAsync).WithName("ListSalaryStructures").RequirePermission(Permissions.Payroll.StructureManage);
        group.MapGet("/{id:guid}", GetStructureAsync).WithName("GetSalaryStructure").RequirePermission(Permissions.Payroll.StructureManage);
        group.MapPost("/", CreateStructureAsync).WithName("CreateSalaryStructure").RequirePermission(Permissions.Payroll.StructureManage);

        group.MapGet("/{id:guid}/components", ListStructureComponentsAsync)
            .WithName("ListSalaryStructureComponents").RequirePermission(Permissions.Payroll.StructureManage);
        group.MapPost("/{id:guid}/components", AddStructureComponentAsync)
            .WithName("AddSalaryStructureComponent").RequirePermission(Permissions.Payroll.StructureManage);
    }

    private static async Task<Ok<IReadOnlyList<SalaryStructureResponse>>> ListStructuresAsync(PayrollDbContext db, CancellationToken ct)
    {
        List<SalaryStructureResponse> items = await db.SalaryStructures.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => ToResponse(x))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<SalaryStructureResponse>>(items);
    }

    private static async Task<Results<Ok<SalaryStructureResponse>, NotFound>> GetStructureAsync(Guid id, PayrollDbContext db, CancellationToken ct)
    {
        SalaryStructure? entity = await db.SalaryStructures.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return entity is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(entity));
    }

    private static async Task<Created<SalaryStructureResponse>> CreateStructureAsync(
        CreateSalaryStructureBody body, SalaryStructureService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        SalaryStructure entity = await service.CreateAsync(body.Code, body.Name, ct);
        return TypedResults.Created($"/api/v1/payroll/structures/{entity.Id}", ToResponse(entity));
    }

    private static async Task<Ok<IReadOnlyList<SalaryStructureComponentResponse>>> ListStructureComponentsAsync(
        Guid id, PayrollDbContext db, CancellationToken ct)
    {
        List<SalaryStructureComponentResponse> items = await db.SalaryStructureComponents.AsNoTracking()
            .Where(x => x.StructureId == id)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => ToResponse(x))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<SalaryStructureComponentResponse>>(items);
    }

    private static async Task<Created<SalaryStructureComponentResponse>> AddStructureComponentAsync(
        Guid id, AddSalaryStructureComponentBody body, SalaryStructureService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        SalaryStructureComponent entity = await service.AddComponentAsync(id, body.ComponentId, body.Value, body.DisplayOrder, ct);
        return TypedResults.Created($"/api/v1/payroll/structures/{id}/components/{entity.Id}", ToResponse(entity));
    }

    private static void MapEmployeeSalaryEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/payroll/employees/{employeeId:guid}/salary").WithTags("Payroll");

        group.MapGet("/", GetEmployeeSalaryAsync).WithName("GetEmployeeSalary").RequirePermission(Permissions.Payroll.EmployeeSalaryView);

        endpoints.MapPost("/api/v1/payroll/employee-salaries", AssignEmployeeSalaryAsync)
            .WithName("AssignEmployeeSalary").WithTags("Payroll").RequirePermission(Permissions.Payroll.EmployeeSalaryManage);
    }

    private static async Task<Results<Ok<EmployeeSalaryResponse>, NotFound>> GetEmployeeSalaryAsync(
        Guid employeeId, PayrollDbContext db, CancellationToken ct)
    {
        EmployeeSalary? entity = await db.EmployeeSalaries.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.EffectiveFrom)
            .ThenByDescending(x => x.CreatedDate)
            .FirstOrDefaultAsync(ct);

        if (entity is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await ToResponseAsync(entity, db, ct));
    }

    private static async Task<Created<EmployeeSalaryResponse>> AssignEmployeeSalaryAsync(
        AssignEmployeeSalaryBody body, EmployeeSalaryService service, PayrollDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        EmployeeSalary entity = await service.AssignAsync(
            body.EmployeeId, body.StructureId, body.AnnualCtc, body.Currency, body.EffectiveFrom, ct);

        return TypedResults.Created(
            $"/api/v1/payroll/employees/{body.EmployeeId}/salary", await ToResponseAsync(entity, db, ct));
    }

    private static async Task<EmployeeSalaryResponse> ToResponseAsync(EmployeeSalary entity, PayrollDbContext db, CancellationToken ct)
    {
        List<EmployeeSalaryComponentResponse> components = await db.EmployeeSalaryComponents.AsNoTracking()
            .Where(x => x.EmployeeSalaryId == entity.Id)
            .Select(x => new EmployeeSalaryComponentResponse(x.Id, x.ComponentId, x.MonthlyAmount, x.AnnualAmount))
            .ToListAsync(ct);

        return new EmployeeSalaryResponse(
            entity.Id, entity.EmployeeId, entity.StructureId, entity.AnnualCtc, entity.Currency, entity.EffectiveFrom, components);
    }

    private static SalaryComponentResponse ToResponse(SalaryComponent entity) => new(
        entity.Id, entity.Code, entity.Name, entity.Kind, entity.CalcType, entity.IsTaxable, entity.DisplayOrder, entity.IsActive);

    private static SalaryStructureResponse ToResponse(SalaryStructure entity) => new(entity.Id, entity.Code, entity.Name, entity.IsActive);

    private static SalaryStructureComponentResponse ToResponse(SalaryStructureComponent entity) => new(
        entity.Id, entity.StructureId, entity.ComponentId, entity.Value, entity.DisplayOrder);

    private static void MapPayGroupEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/payroll/pay-groups").WithTags("Payroll");

        group.MapGet("/", ListPayGroupsAsync).WithName("ListPayGroups").RequirePermission(Permissions.Payroll.PayGroupManage);
        group.MapPost("/", CreatePayGroupAsync).WithName("CreatePayGroup").RequirePermission(Permissions.Payroll.PayGroupManage);
        group.MapPost("/{id:guid}/deactivate", DeactivatePayGroupAsync)
            .WithName("DeactivatePayGroup").RequirePermission(Permissions.Payroll.PayGroupManage);
        group.MapPost("/{id:guid}/reactivate", ReactivatePayGroupAsync)
            .WithName("ReactivatePayGroup").RequirePermission(Permissions.Payroll.PayGroupManage);
    }

    private static async Task<Ok<IReadOnlyList<PayGroupResponse>>> ListPayGroupsAsync(PayrollDbContext db, CancellationToken ct)
    {
        List<PayGroupResponse> items = await db.PayGroups.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => ToResponse(x))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<PayGroupResponse>>(items);
    }

    private static async Task<Created<PayGroupResponse>> CreatePayGroupAsync(CreatePayGroupBody body, PayGroupService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        PayGroup entity = await service.CreateAsync(body.Code, body.Name, body.Frequency, body.Currency, ct);
        return TypedResults.Created($"/api/v1/payroll/pay-groups/{entity.Id}", ToResponse(entity));
    }

    private static async Task<Ok<PayGroupResponse>> DeactivatePayGroupAsync(Guid id, PayGroupService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.DeactivateAsync(id, ct)));

    private static async Task<Ok<PayGroupResponse>> ReactivatePayGroupAsync(Guid id, PayGroupService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.ReactivateAsync(id, ct)));

    private static void MapPayPeriodEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/payroll/pay-groups/{payGroupId:guid}/periods").WithTags("Payroll");

        group.MapGet("/", ListPayPeriodsAsync).WithName("ListPayPeriods").RequirePermission(Permissions.Payroll.PayGroupManage);
        group.MapPost("/", CreatePayPeriodAsync).WithName("CreatePayPeriod").RequirePermission(Permissions.Payroll.PayGroupManage);
    }

    private static async Task<Ok<IReadOnlyList<PayPeriodResponse>>> ListPayPeriodsAsync(Guid payGroupId, PayrollDbContext db, CancellationToken ct)
    {
        List<PayPeriodResponse> items = await db.PayPeriods.AsNoTracking()
            .Where(x => x.PayGroupId == payGroupId)
            .OrderByDescending(x => x.StartDate)
            .Select(x => ToResponse(x))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<PayPeriodResponse>>(items);
    }

    private static async Task<Created<PayPeriodResponse>> CreatePayPeriodAsync(
        Guid payGroupId, CreatePayPeriodBody body, PayPeriodService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        PayPeriod entity = await service.CreateAsync(payGroupId, body.PeriodCode, body.StartDate, body.EndDate, body.PayDate, ct);
        return TypedResults.Created($"/api/v1/payroll/pay-groups/{payGroupId}/periods/{entity.Id}", ToResponse(entity));
    }

    private static void MapPayrollRunEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/payroll/runs").WithTags("Payroll");

        group.MapGet("/", ListPayrollRunsAsync).WithName("ListPayrollRuns").RequirePermission(Permissions.Payroll.RunView);
        group.MapGet("/{id:guid}", GetPayrollRunAsync).WithName("GetPayrollRun").RequirePermission(Permissions.Payroll.RunView);
        group.MapGet("/{id:guid}/employees", ListPayrollRunEmployeesAsync)
            .WithName("ListPayrollRunEmployees").RequirePermission(Permissions.Payroll.RunView);

        group.MapPost("/", CreatePayrollRunAsync).WithName("CreatePayrollRun").RequirePermission(Permissions.Payroll.RunProcess);
        group.MapPost("/{id:guid}/calculate", CalculatePayrollRunAsync)
            .WithName("CalculatePayrollRun").RequirePermission(Permissions.Payroll.RunProcess);
        group.MapPost("/{id:guid}/finalize", FinalizePayrollRunAsync)
            .WithName("FinalizePayrollRun").RequirePermission(Permissions.Payroll.RunFinalize);
    }

    private static async Task<Ok<IReadOnlyList<PayrollRunResponse>>> ListPayrollRunsAsync(PayrollDbContext db, CancellationToken ct)
    {
        List<PayrollRunResponse> items = await db.PayrollRuns.AsNoTracking()
            .OrderByDescending(x => x.CreatedDate)
            .Select(x => ToResponse(x))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<PayrollRunResponse>>(items);
    }

    private static async Task<Results<Ok<PayrollRunResponse>, NotFound>> GetPayrollRunAsync(Guid id, PayrollDbContext db, CancellationToken ct)
    {
        PayrollRun? entity = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return entity is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(entity));
    }

    private static async Task<Ok<IReadOnlyList<PayrollRunEmployeeResponse>>> ListPayrollRunEmployeesAsync(
        Guid id, PayrollDbContext db, CancellationToken ct)
    {
        List<PayrollRunEmployee> runEmployees = await db.PayrollRunEmployees.AsNoTracking()
            .Where(x => x.PayrollRunId == id)
            .ToListAsync(ct);

        List<PayrollRunLine> lines = await db.PayrollRunLines.AsNoTracking()
            .Where(x => runEmployees.Select(e => e.Id).Contains(x.PayrollRunEmployeeId))
            .ToListAsync(ct);

        List<PayrollRunEmployeeResponse> items = [.. runEmployees.Select(e => new PayrollRunEmployeeResponse(
            e.Id, e.EmployeeId, e.Gross, e.NetPay,
            [.. lines.Where(l => l.PayrollRunEmployeeId == e.Id).Select(l => new PayrollRunEmployeeLineResponse(l.ComponentId, l.Amount))]))];

        return TypedResults.Ok<IReadOnlyList<PayrollRunEmployeeResponse>>(items);
    }

    private static async Task<Created<PayrollRunResponse>> CreatePayrollRunAsync(
        CreatePayrollRunBody body, PayrollRunService service, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        PayrollRun entity = await service.CreateAsync(body.PayGroupId, body.PayPeriodId, body.RunNo, ct);
        return TypedResults.Created($"/api/v1/payroll/runs/{entity.Id}", ToResponse(entity));
    }

    private static async Task<Ok<PayrollRunResponse>> CalculatePayrollRunAsync(Guid id, PayrollRunService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.CalculateAsync(id, ct)));

    private static async Task<Ok<PayrollRunResponse>> FinalizePayrollRunAsync(Guid id, PayrollRunService service, CancellationToken ct)
        => TypedResults.Ok(ToResponse(await service.FinalizeAsync(id, ct)));

    private static PayGroupResponse ToResponse(PayGroup entity) => new(entity.Id, entity.Code, entity.Name, entity.Frequency, entity.Currency, entity.IsActive);

    private static PayPeriodResponse ToResponse(PayPeriod entity) => new(
        entity.Id, entity.PayGroupId, entity.PeriodCode, entity.StartDate, entity.EndDate, entity.PayDate, entity.Status);

    private static PayrollRunResponse ToResponse(PayrollRun entity) => new(
        entity.Id, entity.PayGroupId, entity.PayPeriodId, entity.RunNo, entity.Status, entity.EmployeeCount,
        entity.GrossTotal, entity.NetTotal, entity.CalculatedDate, entity.FinalizedDate);

    /// <summary>
    /// My own payslips (<see cref="Permissions.Self.PayslipView"/>) and, for admins, any employee's
    /// (<see cref="Permissions.Payroll.RunView"/>) - the same "own" versus "anyone's" split
    /// Attendance's self-service endpoints already use.
    /// </summary>
    private static void MapPayslipEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/payroll/payslips").WithTags("Payroll");

        group.MapGet("/me", ListMyPayslipsAsync).WithName("ListMyPayslips").RequirePermission(Permissions.Self.PayslipView);

        // Either permission reaches the handler; which payslips a caller may actually read is
        // decided inside it, against the payslip's own EmployeeId - see GetPayslipAsync's own remarks.
        group.MapGet("/{id:guid}", GetPayslipAsync)
            .WithName("GetPayslip").RequirePermission(Permissions.Self.PayslipView, Permissions.Payroll.RunView);

        endpoints.MapGet("/api/v1/payroll/employees/{employeeId:guid}/payslips", ListEmployeePayslipsAsync)
            .WithName("ListEmployeePayslips").WithTags("Payroll").RequirePermission(Permissions.Payroll.RunView);
    }

    private static async Task<Ok<IReadOnlyList<PayslipListItem>>> ListMyPayslipsAsync(
        ICurrentUser currentUser, PayrollDbContext db, CancellationToken ct)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return TypedResults.Ok<IReadOnlyList<PayslipListItem>>([]);
        }

        List<PayslipListItem> items = await ListPayslipItemsAsync(employeeId, db, ct);
        return TypedResults.Ok<IReadOnlyList<PayslipListItem>>(items);
    }

    private static async Task<Ok<IReadOnlyList<PayslipListItem>>> ListEmployeePayslipsAsync(
        Guid employeeId, PayrollDbContext db, CancellationToken ct)
    {
        List<PayslipListItem> items = await ListPayslipItemsAsync(employeeId, db, ct);
        return TypedResults.Ok<IReadOnlyList<PayslipListItem>>(items);
    }

    private static async Task<List<PayslipListItem>> ListPayslipItemsAsync(Guid employeeId, PayrollDbContext db, CancellationToken ct)
    {
        var rows = await db.Payslips.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .Join(db.PayrollRuns.AsNoTracking(), p => p.PayrollRunId, r => r.Id, (p, r) => new { Payslip = p, Run = r })
            .Join(db.PayPeriods.AsNoTracking(), x => x.Run.PayPeriodId, period => period.Id, (x, period) => new { x.Payslip, x.Run, Period = period })
            .Join(db.PayrollRunEmployees.AsNoTracking(), x => x.Payslip.PayrollRunEmployeeId, e => e.Id, (x, runEmployee) => new { x.Payslip, x.Run, x.Period, RunEmployee = runEmployee })
            .OrderByDescending(x => x.Payslip.PublishedDate)
            .ToListAsync(ct);

        return [.. rows.Select(x => new PayslipListItem(
            x.Payslip.Id, x.Payslip.PayrollRunId, x.Run.RunNo, x.Period.PayDate, x.RunEmployee.NetPay, x.Payslip.PublishedDate))];
    }

    /// <summary>
    /// A caller with just <see cref="Permissions.Self.PayslipView"/> may only reach their own
    /// payslip - checked against the payslip's own <see cref="Payslip.EmployeeId"/>, not a blanket
    /// grant, the same "own record" shape self-service permissions elsewhere in this catalog use. A
    /// caller who also holds <see cref="Permissions.Payroll.RunView"/> may reach anyone's.
    /// </summary>
    private static async Task<Results<Ok<PayslipResponse>, NotFound, ForbidHttpResult>> GetPayslipAsync(
        Guid id, ICurrentUser currentUser, IPermissionProvider permissions, PayrollDbContext db, CancellationToken ct)
    {
        Payslip? payslip = await db.Payslips.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (payslip is null)
        {
            return TypedResults.NotFound();
        }

        if (payslip.EmployeeId != currentUser.EmployeeId)
        {
            PermissionSet held = await permissions.GetCurrentAsync(ct);
            if (!held.Has(Permissions.Payroll.RunView))
            {
                return TypedResults.Forbid();
            }
        }

        PayrollRun run = await db.PayrollRuns.AsNoTracking().FirstAsync(x => x.Id == payslip.PayrollRunId, ct);
        PayPeriod period = await db.PayPeriods.AsNoTracking().FirstAsync(x => x.Id == run.PayPeriodId, ct);
        PayrollRunEmployee runEmployee = await db.PayrollRunEmployees.AsNoTracking().FirstAsync(x => x.Id == payslip.PayrollRunEmployeeId, ct);

        List<PayrollRunLine> runLines = await db.PayrollRunLines.AsNoTracking()
            .Where(x => x.PayrollRunEmployeeId == runEmployee.Id)
            .ToListAsync(ct);

        List<SalaryComponent> lineComponents = await db.SalaryComponents.AsNoTracking()
            .Where(x => runLines.Select(l => l.ComponentId).Contains(x.Id))
            .ToListAsync(ct);
        Dictionary<Guid, SalaryComponent> componentsById = lineComponents.ToDictionary(x => x.Id);

        List<PayslipLineResponse> lines = [.. runLines.Select(l => new PayslipLineResponse(
            l.ComponentId, componentsById[l.ComponentId].Name, componentsById[l.ComponentId].Kind, l.Amount))];

        return TypedResults.Ok(new PayslipResponse(
            payslip.Id, payslip.EmployeeId, payslip.PayrollRunId, run.RunNo, run.PayPeriodId, period.PeriodCode, period.PayDate,
            runEmployee.Gross, runEmployee.NetPay, payslip.PublishedDate, lines));
    }
}
