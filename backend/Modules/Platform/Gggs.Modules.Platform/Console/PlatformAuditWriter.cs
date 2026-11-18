using System.Security.Claims;
using Gggs.BuildingBlocks.Application;
using Gggs.Data;
using Gggs.Modules.Platform.Contracts;
using Gggs.Modules.Platform.Domain.Console;
using Gggs.Modules.Platform.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Gggs.Modules.Platform.Console;

/// <summary>
/// Records what platform staff did. Adds the entry to the caller's unit of work, so it commits with
/// the change it describes - or not at all.
/// </summary>
public sealed class PlatformAuditWriter(AppDbContext dbContext, ICurrentUser currentUser, IHttpContextAccessor http, TimeProvider timeProvider)
    : IPlatformAuditLog
{
    /// <summary>The signed-in staff member's name, as the audit trail records it.</summary>
    public string StaffName =>
        http.HttpContext?.User.FindFirstValue(ClaimTypes.Name)
            ?? http.HttpContext?.User.FindFirstValue(ClaimTypes.Email)
            ?? "Platform staff";

    public void Record(string action, string summary, Guid? tenantId = null, string? tenantCode = null, string? reason = null)
    {
        dbContext.PlatformAuditEntries.Add(PlatformAuditEntry.Record(
            timeProvider.GetUtcNow().UtcDateTime, currentUser.UserId, StaffName, tenantId, tenantCode, action, summary, reason));
    }
}
