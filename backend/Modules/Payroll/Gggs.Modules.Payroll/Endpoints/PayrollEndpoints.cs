using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Modules.Payroll.Application;
using Gggs.Modules.Payroll.Domain;

namespace Gggs.Modules.Payroll.Endpoints;

/// <summary>Payroll under <c>/api/v1/payroll</c>; one's own payslips, salary and tax declaration under <c>/api/v1/me</c> (spec §21-22).</summary>
internal static class PayrollEndpoints
{
    public static IEndpointRouteBuilder MapPayrollEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapSelfService(endpoints.MapGroup("/api/v1/me").WithTags("Payroll"));

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/payroll").WithTags("Payroll");
        MapSetup(group);
        MapSalaries(group);
        MapRuns(group);
        MapReports(group);
        return endpoints;
    }

    private static void MapSelfService(RouteGroupBuilder me)
    {
        me.MapGet("/payslips", async (int? year, PayslipService payslips, CancellationToken ct) => TypedResults.Ok(await payslips.MineAsync(year, ct)))
            .WithName("MyPayslips").RequirePermission(Permissions.Self.PayslipView);
        me.MapGet("/payslips/{id:guid}", async (Guid id, PayslipService payslips, CancellationToken ct) => TypedResults.Ok(await payslips.MyPayslipAsync(id, ct)))
            .WithName("MyPayslip").RequirePermission(Permissions.Self.PayslipView);
        me.MapGet("/tax-statement", async (int? year, PayslipService payslips, CancellationToken ct) => TypedResults.Ok(await payslips.MyTaxStatementAsync(year, ct)))
            .WithName("MyTaxStatement").RequirePermission(Permissions.Self.PayslipView);
        me.MapGet("/salary", async (SalaryService salaries, CancellationToken ct) => TypedResults.Ok(await salaries.MineAsync(ct)))
            .WithName("MySalary").RequirePermission(Permissions.Self.PayslipView, Permissions.Self.CompensationView);
        me.MapGet("/tax-declaration", async (int? year, DeclarationService declarations, CancellationToken ct) => TypedResults.Ok(await declarations.MineAsync(year, ct)))
            .WithName("MyTaxDeclaration").RequirePermission(Permissions.Self.TaxDeclarationManage);
        me.MapPut("/tax-declaration/{year:int}", async (int year, DeclarationValues body, DeclarationService declarations, CancellationToken ct) => TypedResults.Ok(await declarations.SaveMineAsync(year, body, ct)))
            .WithName("SaveMyTaxDeclaration").RequirePermission(Permissions.Self.TaxDeclarationManage);
        me.MapPost("/tax-declaration/{year:int}/submit", async (int year, DeclarationService declarations, CancellationToken ct) => TypedResults.Ok(await declarations.SubmitMineAsync(year, ct)))
            .WithName("SubmitMyTaxDeclaration").RequirePermission(Permissions.Self.TaxDeclarationManage);
    }

    private static void MapSetup(RouteGroupBuilder group)
    {
        group.MapGet("/components", async (bool? includeInactive, PayrollSetupService setup, CancellationToken ct) => TypedResults.Ok(await setup.ComponentsAsync(includeInactive ?? false, ct)))
            .WithName("ListSalaryComponents")
            .RequirePermission(Permissions.Payroll.ComponentManage, Permissions.Payroll.StructureManage, Permissions.Payroll.InputManage, Permissions.Payroll.RunView);
        group.MapPost("/components", async (ComponentBody body, PayrollSetupService setup, CancellationToken ct) => TypedResults.Ok(await setup.SaveComponentAsync(null, body, ct)))
            .WithName("CreateSalaryComponent").RequirePermission(Permissions.Payroll.ComponentManage);
        group.MapPut("/components/{id:guid}", async (Guid id, ComponentBody body, PayrollSetupService setup, CancellationToken ct) => TypedResults.Ok(await setup.SaveComponentAsync(id, body, ct)))
            .WithName("UpdateSalaryComponent").RequirePermission(Permissions.Payroll.ComponentManage);
        group.MapPost("/components/{id:guid}/activate", async (Guid id, PayrollSetupService setup, CancellationToken ct) =>
            {
                await setup.SetComponentActiveAsync(id, true, ct);
                return TypedResults.NoContent();
            })
            .WithName("ActivateSalaryComponent").RequirePermission(Permissions.Payroll.ComponentManage);
        group.MapPost("/components/{id:guid}/deactivate", async (Guid id, PayrollSetupService setup, CancellationToken ct) =>
            {
                await setup.SetComponentActiveAsync(id, false, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeactivateSalaryComponent").RequirePermission(Permissions.Payroll.ComponentManage);

        group.MapGet("/structures", async (PayrollSetupService setup, CancellationToken ct) => TypedResults.Ok(await setup.StructuresAsync(ct)))
            .WithName("ListSalaryStructures")
            .RequirePermission(Permissions.Payroll.StructureManage, Permissions.Payroll.EmployeeSalaryView, Permissions.Compensation.RevisionManage);
        group.MapPost("/structures", async (StructureBody body, PayrollSetupService setup, CancellationToken ct) => TypedResults.Ok(await setup.SaveStructureAsync(null, body, ct)))
            .WithName("CreateSalaryStructure").RequirePermission(Permissions.Payroll.StructureManage);
        group.MapPut("/structures/{id:guid}", async (Guid id, StructureBody body, PayrollSetupService setup, CancellationToken ct) => TypedResults.Ok(await setup.SaveStructureAsync(id, body, ct)))
            .WithName("UpdateSalaryStructure").RequirePermission(Permissions.Payroll.StructureManage);
        group.MapPost("/structures/{id:guid}/activate", async (Guid id, PayrollSetupService setup, CancellationToken ct) =>
            {
                await setup.SetStructureActiveAsync(id, true, ct);
                return TypedResults.NoContent();
            })
            .WithName("ActivateSalaryStructure").RequirePermission(Permissions.Payroll.StructureManage);
        group.MapPost("/structures/{id:guid}/deactivate", async (Guid id, PayrollSetupService setup, CancellationToken ct) =>
            {
                await setup.SetStructureActiveAsync(id, false, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeactivateSalaryStructure").RequirePermission(Permissions.Payroll.StructureManage);
        group.MapPost("/structures/{id:guid}/preview", async (Guid id, PreviewBody body, PayrollSetupService setup, CancellationToken ct) => TypedResults.Ok(await setup.PreviewAsync(id, body, ct)))
            .WithName("PreviewSalaryStructure")
            .RequirePermission(Permissions.Payroll.StructureManage, Permissions.Payroll.EmployeeSalaryManage, Permissions.Compensation.RevisionManage);

        group.MapGet("/settings", async (PayrollSetupService setup, CancellationToken ct) => TypedResults.Ok(await setup.SettingsAsync(ct)))
            .WithName("GetPayrollSettings").RequirePermission(Permissions.Payroll.SettingsManage, Permissions.Payroll.RunView);
        group.MapPut("/settings", async (PayrollSettingsValues body, PayrollSetupService setup, CancellationToken ct) => TypedResults.Ok(await setup.SaveSettingsAsync(body, ct)))
            .WithName("SavePayrollSettings").RequirePermission(Permissions.Payroll.SettingsManage);
    }

    private static void MapSalaries(RouteGroupBuilder group)
    {
        group.MapGet("/salaries", async ([AsParameters] SalaryListQuery query, SalaryService salaries, CancellationToken ct) => TypedResults.Ok(await salaries.ListAsync(query, ct)))
            .WithName("ListEmployeeSalaries").RequirePermission(Permissions.Payroll.EmployeeSalaryView);
        group.MapGet("/employees/{employeeId:guid}/salary", async (Guid employeeId, SalaryService salaries, CancellationToken ct) => TypedResults.Ok(await salaries.GetAsync(employeeId, ct)))
            .WithName("GetEmployeeSalary").RequirePermission(Permissions.Payroll.EmployeeSalaryView);
        group.MapPost("/employees/{employeeId:guid}/salary", async (Guid employeeId, AssignSalaryBody body, SalaryService salaries, CancellationToken ct) =>
                TypedResults.Ok(await salaries.AssignAsync(employeeId, body, ct)))
            .WithName("AssignEmployeeSalary").RequirePermission(Permissions.Payroll.EmployeeSalaryManage);
        group.MapGet("/employees/{employeeId:guid}/payslips", async (Guid employeeId, int? year, PayslipService payslips, SalaryService salaries, CancellationToken ct) =>
            {
                await salaries.GetAsync(employeeId, ct);
                return TypedResults.Ok(await payslips.ForEmployeeAsync(employeeId, year, ct));
            })
            .WithName("EmployeePayslips").RequirePermission(Permissions.Payroll.EmployeeSalaryView);
        group.MapGet("/employees/{employeeId:guid}/tax-statement", async (Guid employeeId, int? year, PayslipService payslips, CancellationToken ct) =>
                TypedResults.Ok(await payslips.EmployeeTaxStatementAsync(employeeId, year, ct)))
            .WithName("EmployeeTaxStatement").RequirePermission(Permissions.Payroll.RunView);

        group.MapGet("/inputs", async (DateOnly period, Guid? employeeId, InputService inputs, CancellationToken ct) => TypedResults.Ok(await inputs.ListAsync(period, employeeId, ct)))
            .WithName("ListPayrollInputs").RequirePermission(Permissions.Payroll.InputManage);
        group.MapPost("/inputs", async (InputBody body, InputService inputs, CancellationToken ct) => TypedResults.Ok(await inputs.AddManualAsync(body, ct)))
            .WithName("AddPayrollInput").RequirePermission(Permissions.Payroll.InputManage);
        group.MapDelete("/inputs/{id:guid}", async (Guid id, InputService inputs, CancellationToken ct) =>
            {
                await inputs.CancelAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("CancelPayrollInput").RequirePermission(Permissions.Payroll.InputManage);

        group.MapGet("/tax-declarations", async (int? year, DeclarationStatus? status, DeclarationService declarations, CancellationToken ct) =>
                TypedResults.Ok(await declarations.ListAsync(year, status, ct)))
            .WithName("ListTaxDeclarations").RequirePermission(Permissions.Payroll.TaxDeclarationView);
        group.MapGet("/tax-declarations/{id:guid}", async (Guid id, DeclarationService declarations, CancellationToken ct) => TypedResults.Ok(await declarations.GetAsync(id, ct)))
            .WithName("GetTaxDeclaration").RequirePermission(Permissions.Payroll.TaxDeclarationView);
        group.MapPost("/tax-declarations/{id:guid}/verify", async (Guid id, VerifyBody body, DeclarationService declarations, CancellationToken ct) => TypedResults.Ok(await declarations.VerifyAsync(id, body, ct)))
            .WithName("VerifyTaxDeclaration").RequirePermission(Permissions.Payroll.TaxDeclarationVerify);
        group.MapPost("/tax-declarations/{id:guid}/reopen", async (Guid id, DeclarationService declarations, CancellationToken ct) => TypedResults.Ok(await declarations.ReopenAsync(id, ct)))
            .WithName("ReopenTaxDeclaration").RequirePermission(Permissions.Payroll.TaxDeclarationVerify);
    }

    private static void MapRuns(RouteGroupBuilder group)
    {
        group.MapGet("/runs", async (int? year, RunService runs, CancellationToken ct) => TypedResults.Ok(await runs.ListAsync(year, ct)))
            .WithName("ListPayrollRuns").RequirePermission(Permissions.Payroll.RunView);
        group.MapPost("/runs", async (CreateRunBody body, RunService runs, CancellationToken ct) => TypedResults.Ok(await runs.CreateAsync(body, ct)))
            .WithName("CreatePayrollRun").RequirePermission(Permissions.Payroll.RunProcess);
        group.MapGet("/runs/{id:guid}", async (Guid id, RunService runs, CancellationToken ct) => TypedResults.Ok(await runs.GetAsync(id, ct)))
            .WithName("GetPayrollRun").RequirePermission(Permissions.Payroll.RunView, Permissions.Workflow.TaskAct);
        group.MapPost("/runs/{id:guid}/calculate", async (Guid id, RunService runs, CancellationToken ct) => TypedResults.Ok(await runs.CalculateAsync(id, ct)))
            .WithName("CalculatePayrollRun").RequirePermission(Permissions.Payroll.RunProcess);
        group.MapGet("/runs/{id:guid}/items", async (Guid id, [AsParameters] RunItemQuery query, RunService runs, CancellationToken ct) => TypedResults.Ok(await runs.ItemsAsync(id, query, ct)))
            .WithName("ListPayrollRunItems").RequirePermission(Permissions.Payroll.RunView);
        group.MapGet("/runs/{id:guid}/items/{itemId:guid}", async (Guid id, Guid itemId, RunService runs, CancellationToken ct) => TypedResults.Ok(await runs.ItemAsync(id, itemId, ct)))
            .WithName("GetPayrollRunItem").RequirePermission(Permissions.Payroll.RunView);
        group.MapPost("/runs/{id:guid}/items/{itemId:guid}/hold", async (Guid id, Guid itemId, HoldBody body, RunService runs, CancellationToken ct) =>
                TypedResults.Ok(await runs.HoldAsync(id, itemId, body.Reason, ct)))
            .WithName("HoldPayrollRunItem").RequirePermission(Permissions.Payroll.RunProcess);
        group.MapPost("/runs/{id:guid}/items/{itemId:guid}/release", async (Guid id, Guid itemId, RunService runs, CancellationToken ct) =>
                TypedResults.Ok(await runs.HoldAsync(id, itemId, null, ct)))
            .WithName("ReleasePayrollRunItem").RequirePermission(Permissions.Payroll.RunProcess);
        group.MapPost("/runs/{id:guid}/submit", async (Guid id, RunService runs, CancellationToken ct) => TypedResults.Ok(await runs.SubmitAsync(id, ct)))
            .WithName("SubmitPayrollRun").RequirePermission(Permissions.Payroll.RunProcess);
        group.MapPost("/runs/{id:guid}/finalize", async (Guid id, RunService runs, CancellationToken ct) => TypedResults.Ok(await runs.FinalizeAsync(id, ct)))
            .WithName("FinalizePayrollRun").RequirePermission(Permissions.Payroll.RunFinalize);
        group.MapPost("/runs/{id:guid}/paid", async (Guid id, PaidBody body, RunService runs, CancellationToken ct) => TypedResults.Ok(await runs.MarkPaidAsync(id, body, ct)))
            .WithName("MarkPayrollRunPaid").RequirePermission(Permissions.Payroll.RunFinalize);
        group.MapPost("/runs/{id:guid}/cancel", async (Guid id, RunService runs, CancellationToken ct) => TypedResults.Ok(await runs.CancelAsync(id, ct)))
            .WithName("CancelPayrollRun").RequirePermission(Permissions.Payroll.RunProcess);

        group.MapGet("/payslips", async (DateOnly? period, Guid? employeeId, PayslipService payslips, CancellationToken ct) => TypedResults.Ok(await payslips.ListAsync(period, employeeId, ct)))
            .WithName("ListPayslips").RequirePermission(Permissions.Payroll.RunView);
        group.MapGet("/payslips/{id:guid}", async (Guid id, PayslipService payslips, CancellationToken ct) => TypedResults.Ok(await payslips.GetAsync(id, ct)))
            .WithName("GetPayslip").RequirePermission(Permissions.Payroll.RunView);
    }

    private static void MapReports(RouteGroupBuilder group)
    {
        group.MapGet("/dashboard", async (PayrollReports reports, CancellationToken ct) => TypedResults.Ok(await reports.DashboardAsync(ct)))
            .WithName("PayrollDashboard").RequirePermission(Permissions.Payroll.RunView);

        group.MapGet("/runs/{id:guid}/reports/register", async (Guid id, PayrollReports reports, CancellationToken ct) => File(await reports.RegisterAsync(id, ct)))
            .WithName("PayrollRegister").RequirePermission(Permissions.Payroll.ReportView);
        group.MapGet("/runs/{id:guid}/reports/bank-file", async (Guid id, PayrollReports reports, CancellationToken ct) => File(await reports.BankFileAsync(id, ct)))
            .WithName("PayrollBankFile").RequirePermission(Permissions.Payroll.ReportView);
        group.MapGet("/runs/{id:guid}/reports/ecr", async (Guid id, PayrollReports reports, CancellationToken ct) => File(await reports.ProvidentFundReturnAsync(id, ct)))
            .WithName("PayrollProvidentFundReturn").RequirePermission(Permissions.Payroll.ReportView);
        group.MapGet("/runs/{id:guid}/reports/esi", async (Guid id, PayrollReports reports, CancellationToken ct) => File(await reports.StateInsuranceReturnAsync(id, ct)))
            .WithName("PayrollStateInsuranceReturn").RequirePermission(Permissions.Payroll.ReportView);
        group.MapGet("/runs/{id:guid}/reports/professional-tax", async (Guid id, PayrollReports reports, CancellationToken ct) => File(await reports.ProfessionalTaxReturnAsync(id, ct)))
            .WithName("PayrollProfessionalTaxReturn").RequirePermission(Permissions.Payroll.ReportView);
        group.MapGet("/runs/{id:guid}/reports/journal", async (Guid id, PayrollReports reports, CancellationToken ct) => File(await reports.JournalAsync(id, ct)))
            .WithName("PayrollJournal").RequirePermission(Permissions.Payroll.ReportView);
    }

    private static IResult File(ReportFile file) => Results.File(file.Content, file.ContentType, file.FileName);
}
