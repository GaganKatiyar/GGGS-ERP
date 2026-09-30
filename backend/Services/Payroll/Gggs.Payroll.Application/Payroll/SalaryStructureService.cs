using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Payroll.Domain.Payroll;

namespace Gggs.Payroll.Application.Payroll;

/// <summary>Structures and the components they are built from. A structure's percentage lines are
/// always a share of its own "BASIC" line - see <see cref="AddComponentAsync"/>'s own remarks on why
/// that rule is enforced here rather than on <see cref="SalaryStructureComponent"/> itself.</summary>
public sealed class SalaryStructureService(
    ISalaryStructureRepository structures,
    ISalaryStructureComponentRepository structureComponents,
    ISalaryComponentRepository components,
    IUnitOfWork unitOfWork)
{
    private const string BasicComponentCode = "BASIC";

    public async Task<SalaryStructure> CreateAsync(string code, string name, CancellationToken cancellationToken = default)
    {
        Guard(
            !await structures.CodeExistsAsync(code, cancellationToken),
            "SALARY_STRUCTURE_CODE_ALREADY_EXISTS",
            $"A structure with code '{code}' already exists.");

        SalaryStructure structure = SalaryStructure.Create(code, name);
        structures.Add(structure);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return structure;
    }

    /// <summary>
    /// Adds one component line to a structure. A Fixed component must be that structure's own
    /// "BASIC" component code the first time a Fixed line is added - <see cref="EmployeeSalaryService"/>'s
    /// breakdown calculation needs exactly one Basic amount to compute every PercentOfBasic line
    /// against, so a structure that could end up with zero or several is a configuration mistake
    /// worth catching here, at setup time, rather than at assignment time.
    /// </summary>
    public async Task<SalaryStructureComponent> AddComponentAsync(
        Guid structureId, Guid componentId, decimal value, int displayOrder, CancellationToken cancellationToken = default)
    {
        SalaryStructure structure = await structures.GetAsync(structureId, cancellationToken)
            ?? throw new NotFoundException(nameof(SalaryStructure), structureId);

        SalaryComponent component = await components.GetAsync(componentId, cancellationToken)
            ?? throw new NotFoundException(nameof(SalaryComponent), componentId);

        IReadOnlyList<SalaryStructureComponent> existingLines = await structureComponents.ListForStructureAsync(structureId, cancellationToken);

        if (component.CalcType == SalaryComponentCalcType.Fixed && component.Code == BasicComponentCode)
        {
            IReadOnlyList<SalaryComponent> existingComponents = await components.GetManyAsync(
                existingLines.Select(x => x.ComponentId), cancellationToken);

            Guard(
                existingComponents.All(x => x.Code != BasicComponentCode),
                "SALARY_STRUCTURE_BASIC_ALREADY_SET",
                $"Structure '{structure.Name}' already has a Basic component.");
        }
        else if (component.CalcType == SalaryComponentCalcType.PercentOfBasic)
        {
            Guard(value is >= 0 and <= 100, "SALARY_STRUCTURE_PERCENTAGE_OUT_OF_RANGE", "The percentage must be between 0 and 100.");
        }

        Guard(
            existingLines.All(x => x.ComponentId != componentId),
            "SALARY_STRUCTURE_COMPONENT_ALREADY_ADDED",
            $"'{component.Name}' has already been added to this structure.");

        SalaryStructureComponent line = SalaryStructureComponent.Create(structureId, componentId, value, displayOrder);
        structureComponents.Add(line);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return line;
    }

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
