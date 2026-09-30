using Gggs.Payroll.Domain.Payroll;

namespace Gggs.Payroll.Api.Contracts;

public sealed record SalaryComponentResponse(
    Guid Id, string Code, string Name, SalaryComponentKind Kind, SalaryComponentCalcType CalcType, bool IsTaxable,
    int DisplayOrder, bool IsActive);

public sealed record CreateSalaryComponentBody(
    string Code, string Name, SalaryComponentKind Kind, SalaryComponentCalcType CalcType, bool IsTaxable, int DisplayOrder);

public sealed record SalaryStructureResponse(Guid Id, string Code, string Name, bool IsActive);

public sealed record CreateSalaryStructureBody(string Code, string Name);

public sealed record SalaryStructureComponentResponse(Guid Id, Guid StructureId, Guid ComponentId, decimal Value, int DisplayOrder);

public sealed record AddSalaryStructureComponentBody(Guid ComponentId, decimal Value, int DisplayOrder);

public sealed record EmployeeSalaryComponentResponse(Guid Id, Guid ComponentId, decimal MonthlyAmount, decimal AnnualAmount);

public sealed record EmployeeSalaryResponse(
    Guid Id, Guid EmployeeId, Guid StructureId, decimal AnnualCtc, string Currency, DateOnly EffectiveFrom,
    IReadOnlyList<EmployeeSalaryComponentResponse> Components);

public sealed record AssignEmployeeSalaryBody(Guid EmployeeId, Guid StructureId, decimal AnnualCtc, string Currency, DateOnly EffectiveFrom);
