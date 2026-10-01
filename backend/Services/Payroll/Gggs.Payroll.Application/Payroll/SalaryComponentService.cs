using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Payroll.Domain.Payroll;

namespace Gggs.Payroll.Application.Payroll;

public sealed class SalaryComponentService(
    ISalaryComponentRepository components, ISalaryComponentSlabRepository slabs, IUnitOfWork unitOfWork)
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

    /// <summary>
    /// Adds one slab to a FlatSlab component - "at or above Rs 15,000 of Basic, Rs 200/month." Only
    /// meaningful for a <see cref="SalaryComponentCalcType.FlatSlab"/> component, and rejected for
    /// every other calc type since nothing would ever read the slab.
    /// </summary>
    public async Task<SalaryComponentSlab> AddSlabAsync(
        Guid componentId, decimal thresholdAmount, decimal taxAmount, CancellationToken cancellationToken = default)
    {
        SalaryComponent component = await GetOrThrowAsync(componentId, cancellationToken);
        Guard(
            component.CalcType == SalaryComponentCalcType.FlatSlab,
            "SALARY_COMPONENT_NOT_FLAT_SLAB",
            $"'{component.Name}' is not a FlatSlab component, so it cannot have slabs.");

        IReadOnlyList<SalaryComponentSlab> existing = await slabs.ListForComponentAsync(componentId, cancellationToken);
        Guard(
            existing.All(x => x.ThresholdAmount != thresholdAmount),
            "SALARY_COMPONENT_SLAB_THRESHOLD_ALREADY_USED",
            $"'{component.Name}' already has a slab at threshold {thresholdAmount}.");

        SalaryComponentSlab slab = SalaryComponentSlab.Create(componentId, thresholdAmount, taxAmount);
        slabs.Add(slab);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return slab;
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
