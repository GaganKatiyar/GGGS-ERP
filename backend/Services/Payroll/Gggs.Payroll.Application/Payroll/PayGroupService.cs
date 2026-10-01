using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Payroll.Domain.Payroll;

namespace Gggs.Payroll.Application.Payroll;

public sealed class PayGroupService(IPayGroupRepository payGroups, IUnitOfWork unitOfWork)
{
    public async Task<PayGroup> CreateAsync(
        string code, string name, PayFrequency frequency, string currency, CancellationToken cancellationToken = default)
    {
        Guard(
            !await payGroups.CodeExistsAsync(code, cancellationToken),
            "PAY_GROUP_CODE_ALREADY_EXISTS",
            $"A pay group with code '{code}' already exists.");

        PayGroup payGroup = PayGroup.Create(code, name, frequency, currency);
        payGroups.Add(payGroup);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return payGroup;
    }

    public async Task<PayGroup> DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        PayGroup payGroup = await GetOrThrowAsync(id, cancellationToken);
        payGroup.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return payGroup;
    }

    public async Task<PayGroup> ReactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        PayGroup payGroup = await GetOrThrowAsync(id, cancellationToken);
        payGroup.Reactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return payGroup;
    }

    private async Task<PayGroup> GetOrThrowAsync(Guid id, CancellationToken cancellationToken)
        => await payGroups.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(PayGroup), id);

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
