using Gggs.Talent.Domain.Recruitment;

namespace Gggs.Talent.Application.Recruitment;

public interface IOfferRepository
{
    Task<Offer?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ExistsForApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default);

    void Add(Offer offer);
}
