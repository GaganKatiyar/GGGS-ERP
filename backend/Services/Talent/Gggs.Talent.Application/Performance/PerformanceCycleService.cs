using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.Talent.Domain.Performance;

namespace Gggs.Talent.Application.Performance;

public sealed class PerformanceCycleService(IPerformanceCycleRepository cycles, IUnitOfWork unitOfWork)
{
    public async Task<PerformanceCycle> CreateAsync(
        string name, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default)
    {
        PerformanceCycle cycle = PerformanceCycle.Create(name, startDate, endDate);
        cycles.Add(cycle);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return cycle;
    }

    public async Task<PerformanceCycle> OpenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        PerformanceCycle cycle = await GetOrThrowAsync(id, cancellationToken);
        cycle.Open();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return cycle;
    }

    public async Task<PerformanceCycle> CloseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        PerformanceCycle cycle = await GetOrThrowAsync(id, cancellationToken);
        cycle.Close();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return cycle;
    }

    private async Task<PerformanceCycle> GetOrThrowAsync(Guid id, CancellationToken cancellationToken)
        => await cycles.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(PerformanceCycle), id);
}
