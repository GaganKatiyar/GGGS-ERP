using Gggs.Data;
using Gggs.Modules.Identity.Application.PlatformStaff;
using Gggs.Modules.Identity.Domain.PlatformStaff;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Identity.Infrastructure;

/// <summary>
/// Platform staff persistence.
/// <para>
/// No tenant parameter anywhere, unlike <c>UserRepository</c>: <c>PlatformStaffMember</c> carries no
/// <c>TenantId</c> and gets no tenant query filter, so a lookup by email here can only ever find a
/// platform account. Row-level security still applies underneath; this table simply has no tenant
/// column for a policy to filter on.
/// </para>
/// </summary>
public sealed class PlatformStaffRepository(AppDbContext dbContext) : IPlatformStaffRepository
{
    public async Task<PlatformStaffMember?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
        => await dbContext.PlatformStaff.FirstOrDefaultAsync(
            member => member.NormalizedEmail == normalizedEmail,
            cancellationToken);

    public async Task<PlatformStaffMember?> GetAsync(Guid platformStaffId, CancellationToken cancellationToken = default)
        => await dbContext.PlatformStaff.FirstOrDefaultAsync(member => member.Id == platformStaffId, cancellationToken);

    public async Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default)
        => await dbContext.PlatformStaff.AnyAsync(member => member.NormalizedEmail == normalizedEmail, cancellationToken);

    public void Add(PlatformStaffMember member) => dbContext.PlatformStaff.Add(member);
}
