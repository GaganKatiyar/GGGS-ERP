using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Payroll.Domain.Payroll;

namespace Gggs.Payroll.Application.Payroll;

public sealed class PayPeriodService(IPayPeriodRepository periods, IPayGroupRepository payGroups, IUnitOfWork unitOfWork)
{
    public async Task<PayPeriod> CreateAsync(
        Guid payGroupId, string periodCode, DateOnly startDate, DateOnly endDate, DateOnly payDate,
        CancellationToken cancellationToken = default)
    {
        _ = await payGroups.GetAsync(payGroupId, cancellationToken) ?? throw new NotFoundException(nameof(PayGroup), payGroupId);

        Guard(
            !await periods.PeriodCodeExistsAsync(payGroupId, periodCode, cancellationToken),
            "PAY_PERIOD_CODE_ALREADY_EXISTS",
            $"A period with code '{periodCode}' already exists for this pay group.");

        PayPeriod period = PayPeriod.Create(payGroupId, periodCode, startDate, endDate, payDate);
        periods.Add(period);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return period;
    }

    private static void Guard(bool condition, string errorCode, string message)
    {
        if (!condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
