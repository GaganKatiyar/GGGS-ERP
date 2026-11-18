using Gggs.Data;
using Gggs.Modules.Employee.Domain;
using Gggs.Modules.Platform.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Employee.Infrastructure;

/// <summary>The organisation's headcount for the platform console: everyone not yet exited.</summary>
internal sealed class EmployeeUsageProbe(AppDbContext dbContext) : ITenantUsageProbe
{
    public async Task ReadAsync(TenantUsageReading reading, DateTime todayStartUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reading);
        reading.Employees = await dbContext.Employees.AsNoTracking()
            .CountAsync(employee => employee.Status != EmployeeStatus.Exited, cancellationToken);
    }
}
