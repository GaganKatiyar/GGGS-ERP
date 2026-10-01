using Gggs.Payroll.Domain.Payroll;

namespace Gggs.Payroll.Api.Contracts;

public sealed record SalaryComponentResponse(
    Guid Id, string Code, string Name, SalaryComponentKind Kind, SalaryComponentCalcType CalcType, bool IsTaxable,
    int DisplayOrder, bool IsActive);

public sealed record CreateSalaryComponentBody(
    string Code, string Name, SalaryComponentKind Kind, SalaryComponentCalcType CalcType, bool IsTaxable, int DisplayOrder);

public sealed record SalaryComponentSlabResponse(Guid Id, Guid ComponentId, decimal ThresholdAmount, decimal TaxAmount);

public sealed record AddSalaryComponentSlabBody(decimal ThresholdAmount, decimal TaxAmount);

public sealed record SalaryStructureResponse(Guid Id, string Code, string Name, bool IsActive);

public sealed record CreateSalaryStructureBody(string Code, string Name);

public sealed record SalaryStructureComponentResponse(
    Guid Id, Guid StructureId, Guid ComponentId, decimal Value, decimal? WageCeiling, int DisplayOrder);

public sealed record AddSalaryStructureComponentBody(Guid ComponentId, decimal Value, int DisplayOrder, decimal? WageCeiling = null);

public sealed record EmployeeSalaryComponentResponse(Guid Id, Guid ComponentId, decimal MonthlyAmount, decimal AnnualAmount);

public sealed record EmployeeSalaryResponse(
    Guid Id, Guid EmployeeId, Guid StructureId, decimal AnnualCtc, string Currency, DateOnly EffectiveFrom,
    IReadOnlyList<EmployeeSalaryComponentResponse> Components);

public sealed record AssignEmployeeSalaryBody(Guid EmployeeId, Guid StructureId, decimal AnnualCtc, string Currency, DateOnly EffectiveFrom);

public sealed record PayGroupResponse(Guid Id, string Code, string Name, PayFrequency Frequency, string Currency, bool IsActive);

public sealed record CreatePayGroupBody(string Code, string Name, PayFrequency Frequency, string Currency);

public sealed record PayPeriodResponse(
    Guid Id, Guid PayGroupId, string PeriodCode, DateOnly StartDate, DateOnly EndDate, DateOnly PayDate, PayPeriodStatus Status);

public sealed record CreatePayPeriodBody(string PeriodCode, DateOnly StartDate, DateOnly EndDate, DateOnly PayDate);

public sealed record PayrollRunResponse(
    Guid Id, Guid PayGroupId, Guid PayPeriodId, string RunNo, PayrollRunStatus Status, int EmployeeCount,
    decimal GrossTotal, decimal NetTotal, DateTime? CalculatedDate, DateTime? FinalizedDate);

public sealed record CreatePayrollRunBody(Guid PayGroupId, Guid PayPeriodId, string RunNo);

public sealed record PayrollRunEmployeeLineResponse(Guid ComponentId, decimal Amount);

public sealed record PayrollRunEmployeeResponse(
    Guid Id, Guid EmployeeId, decimal Gross, decimal NetPay, decimal PaidDays, decimal LopDays, decimal TotalDays,
    IReadOnlyList<PayrollRunEmployeeLineResponse> Lines);

public sealed record PayslipLineResponse(Guid ComponentId, string ComponentName, SalaryComponentKind Kind, decimal Amount);

public sealed record PayslipResponse(
    Guid Id,
    Guid EmployeeId,
    Guid PayrollRunId,
    string RunNo,
    Guid PayPeriodId,
    string PeriodCode,
    DateOnly PayDate,
    decimal Gross,
    decimal NetPay,
    DateTime PublishedDate,
    IReadOnlyList<PayslipLineResponse> Lines);

public sealed record PayslipListItem(Guid Id, Guid PayrollRunId, string RunNo, DateOnly PayDate, decimal NetPay, DateTime PublishedDate);
