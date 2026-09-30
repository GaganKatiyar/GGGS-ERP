using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Payroll.Domain.Payroll;

namespace Gggs.Payroll.Application.Payroll;

public sealed class SalaryComponentService(ISalaryComponentRepository components, IUnitOfWork unitOfWork)
{
    public async Task<SalaryComponent> CreateAsync(
        string code, string name, SalaryComponentKind kind, SalaryComponentCalcType calcType, bool isTaxable, int displayOrder,
        CancellationToken cancellationToken = default)
    {
        Guard(
            !await components.CodeExistsAsync(code, cancellationToken),
            "SALARY_COMPONENT_CODE_ALREADY_EXISTS",
            $"A component with code '{code}' already exists.");

        SalaryComponent component = SalaryComponent.Create(code, name, kind, calcType, isTaxable, displayOrder);
        components.Add(component);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return component;
    }

    public async Task<SalaryComponent> DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        SalaryComponent component = await GetOrThrowAsync(id, cancellationToken);
        component.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return component;
    }

    public async Task<SalaryComponent> ReactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        SalaryComponent component = await GetOrThrowAsync(id, cancellationToken);
        component.Reactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return component;
    }

    private async Task<SalaryComponent> GetOrThrowAsync(Guid id, CancellationToken cancellationToken)
        => await components.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(SalaryComponent), id);

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
