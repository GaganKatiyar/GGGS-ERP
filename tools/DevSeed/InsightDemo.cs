using Gggs.Data;
using Gggs.Modules.Identity.Application.Authentication;
using Gggs.Modules.Identity.Domain.Users;
using Gggs.Modules.Insight.Application;
using Gggs.Modules.Insight.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gggs.DevSeed;

/// <summary>
/// Development-only insight data (M11): a bank-transfer connection and a Tally journal connection
/// ready to run against a finalized payroll, and a weekly executive digest for the administrator.
/// Dashboards, search and the calendar need no data of their own - they read every other module.
/// </summary>
internal static class InsightDemo
{
    public static async Task SeedAsync(IServiceProvider scope, string adminEmail)
    {
        await ExperienceDemo.TopUpRolesAsync(scope);
        AppDbContext db = scope.GetRequiredService<AppDbContext>();
        if (await db.Set<IntegrationConnection>().AnyAsync())
        {
            Console.WriteLine("Integrations already present.");
            return;
        }

        IntegrationService integrations = scope.GetRequiredService<IntegrationService>();
        await integrations.SaveAsync(null, new ConnectionBody("bank-transfer", "HDFC salary account", new Dictionary<string, string>
        {
            ["bankName"] = "HDFC Bank",
            ["debitAccount"] = "50200012345678",
            ["ifsc"] = "HDFC0000123",
        }), CancellationToken.None);
        await integrations.SaveAsync(null, new ConnectionBody("accounting-journal", "Tally Prime books", new Dictionary<string, string>
        {
            ["format"] = "tally",
            ["company"] = "Acme Technologies Pvt Ltd",
        }), CancellationToken.None);

        User? admin = await scope.GetRequiredService<IUserRepository>().FindByEmailAsync(User.NormalizeEmailForLookup(adminEmail));
        if (admin is not null)
        {
            db.Add(ReportSubscription.Create(admin.Id, admin.EmployeeId, AnalyticsService.Executive, DigestFrequency.Weekly));
            await db.SaveChangesAsync();
        }

        Console.WriteLine("Insight: bank-transfer and Tally journal connections, and a weekly executive digest for the administrator.");
    }
}
