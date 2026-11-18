using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Engagement.Domain;
using Gggs.Modules.Engagement.Infrastructure;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Engagement.Application;

/// <summary>A birthday or work anniversary on the greetings screen, and whether HR has wished them.</summary>
public sealed record CelebrationRow(
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string? DepartmentName,
    string? ManagerName,
    DateOnly Date,
    int Years,
    bool CanSend,
    DateTime? WishedAt,
    string? WishedBy,
    string? Note);

/// <summary>The screen's list: today's date where the company is, and the celebrations in the range.</summary>
public sealed record CelebrationList(DateOnly Today, IReadOnlyList<CelebrationRow> Rows);

/// <summary>One person to wish, for the occasion on that date.</summary>
public sealed record WishTarget(Guid EmployeeId, DateOnly Date);

public sealed record SendWishesBody(CelebrationKind Kind, IReadOnlyList<WishTarget>? Items, string? Note);

/// <summary>How many were wished, and those that were not, with why.</summary>
public sealed record SendWishesResult(int Sent, IReadOnlyList<SkippedWish> Skipped);

public sealed record SkippedWish(Guid EmployeeId, DateOnly Date, string Reason);

/// <summary>
/// Birthday and work-anniversary greetings, sent by hand by HR: the employee gets the wish, with an
/// optional personal note, and their manager a heads-up. A wish goes out on the day or up to
/// <see cref="LateDays"/> days late, never early, and once per person and occasion.
/// </summary>
internal sealed class CelebrationService(
    AppDbContext dbContext,
    IEmployeeCelebrations celebrations,
    IEmployeeDirectory employees,
    IOrgStructure organization,
    INotifier notifier,
    ICurrentUser currentUser,
    ITenantClock clock,
    ITenantContext tenantContext,
    ITenantRegistry tenants,
    TimeProvider timeProvider)
{
    /// <summary>How many days after the occasion a belated wish may still be sent.</summary>
    public const int LateDays = 7;

    /// <summary>The longest range the screen asks for at once.</summary>
    public const int MaxRangeDays = 62;

    /// <summary>The most people one send may wish.</summary>
    public const int MaxPerSend = 200;

    public async Task<CelebrationList> ListAsync(CelebrationKind kind, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        DateOnly first = from ?? today;
        DateOnly last = to ?? first;
        if (last < first)
        {
            throw new DomainRuleException("CELEBRATION_RANGE", "The end date must be on or after the start date.");
        }

        if (last.DayNumber - first.DayNumber >= MaxRangeDays)
        {
            throw new DomainRuleException("CELEBRATION_RANGE", $"Choose a range of at most {MaxRangeDays} days.");
        }

        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Engagement.SurveyManage, cancellationToken);
        IReadOnlyList<Celebration> found = await FindAsync(kind, first, last, scope, cancellationToken);

        List<Guid> ids = [.. found.Select(row => row.EmployeeId).Distinct()];
        List<CelebrationWish> wishes = ids.Count == 0
            ? []
            : await dbContext.CelebrationWishes.AsNoTracking()
                .Where(row => row.Kind == kind && row.OccurredOn >= first && row.OccurredOn <= last && ids.Contains(row.EmployeeId))
                .ToListAsync(cancellationToken);
        Dictionary<(Guid, DateOnly), CelebrationWish> wished = wishes.ToDictionary(row => (row.EmployeeId, row.OccurredOn));

        List<Guid> departmentIds = [.. found.Where(row => row.DepartmentId != null).Select(row => row.DepartmentId!.Value).Distinct()];
        IReadOnlyDictionary<Guid, string> departments = departmentIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await organization.GetNamesAsync(OrgUnitKind.Department, departmentIds, cancellationToken);
        IReadOnlyDictionary<Guid, string> managers = await ManagerNamesAsync(found, cancellationToken);

        return new CelebrationList(today, [.. found.Select(row =>
        {
            CelebrationWish? wish = wished.GetValueOrDefault((row.EmployeeId, row.Date));
            return new CelebrationRow(
                row.EmployeeId,
                row.EmployeeCode,
                row.DisplayName,
                row.DepartmentId is { } department ? departments.GetValueOrDefault(department) : null,
                row.ManagerEmployeeId is { } manager ? managers.GetValueOrDefault(manager) : null,
                row.Date,
                row.Years,
                wish is null && IsSendable(row.Date, today),
                wish?.SentAt,
                wish?.SentByName,
                wish?.Note);
        })]);
    }

    public async Task<SendWishesResult> SendAsync(SendWishesBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!Enum.IsDefined(body.Kind))
        {
            throw new DomainRuleException("CELEBRATION_KIND", "Choose birthdays or work anniversaries.");
        }

        List<WishTarget> targets = [.. (body.Items ?? []).Distinct()];
        if (targets.Count == 0)
        {
            throw new DomainRuleException("CELEBRATION_NOBODY", "Choose who to wish.");
        }

        if (targets.Count > MaxPerSend)
        {
            throw new DomainRuleException("CELEBRATION_TOO_MANY", $"Wish at most {MaxPerSend} people at once.");
        }

        string? note = string.IsNullOrWhiteSpace(body.Note) ? null : body.Note.Trim();
        if (note is { Length: > CelebrationWish.NoteMaxLength })
        {
            throw new DomainRuleException("CELEBRATION_NOTE", $"Keep the personal note to {CelebrationWish.NoteMaxLength} characters.");
        }

        DateOnly today = await clock.TodayAsync(cancellationToken);
        DateOnly first = targets.Min(row => row.Date);
        DateOnly last = targets.Max(row => row.Date);
        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Engagement.SurveyManage, cancellationToken);
        Dictionary<(Guid, DateOnly), Celebration> real = (await FindAsync(body.Kind, first, last, scope, cancellationToken))
            .ToDictionary(row => (row.EmployeeId, row.Date));

        List<Guid> ids = [.. targets.Select(row => row.EmployeeId).Distinct()];
        HashSet<(Guid, DateOnly)> alreadyWished = [.. (await dbContext.CelebrationWishes.AsNoTracking()
                .Where(row => row.Kind == body.Kind && row.OccurredOn >= first && row.OccurredOn <= last && ids.Contains(row.EmployeeId))
                .Select(row => new { row.EmployeeId, row.OccurredOn })
                .ToListAsync(cancellationToken))
            .Select(row => (row.EmployeeId, row.OccurredOn))];

        string companyName = (await tenants.GetAsync(tenantContext.TenantId, cancellationToken))?.Name ?? "the company";
        string senderName = await SenderNameAsync(cancellationToken);
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        bool birthday = body.Kind == CelebrationKind.Birthday;

        List<SkippedWish> skipped = [];
        int sent = 0;
        foreach (WishTarget target in targets)
        {
            if (!real.TryGetValue((target.EmployeeId, target.Date), out Celebration? celebration))
            {
                skipped.Add(new SkippedWish(target.EmployeeId, target.Date, birthday ? "It is not their birthday on that day." : "It is not their work anniversary on that day."));
                continue;
            }

            if (alreadyWished.Contains((target.EmployeeId, target.Date)))
            {
                skipped.Add(new SkippedWish(target.EmployeeId, target.Date, $"{celebration.DisplayName} was already wished."));
                continue;
            }

            if (!IsSendable(target.Date, today))
            {
                skipped.Add(new SkippedWish(target.EmployeeId, target.Date, target.Date > today
                    ? $"{celebration.DisplayName}'s day has not come yet."
                    : $"{celebration.DisplayName}'s day was more than {LateDays} days ago."));
                continue;
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["employeeName"] = celebration.DisplayName,
                ["companyName"] = companyName,
                ["note"] = note ?? string.Empty,
                ["date"] = target.Date.ToString("d MMMM", CultureInfo.InvariantCulture),
                ["when"] = When(target.Date, today),
                ["years"] = celebration.Years.ToString(CultureInfo.InvariantCulture),
                ["yearsWord"] = celebration.Years == 1 ? "year" : "years",
            };

            await notifier.NotifyAsync(
                new NotificationRequest(birthday ? NotificationTemplates.BirthdayWish : NotificationTemplates.AnniversaryWish, [new NotificationRecipient(celebration.EmployeeId)], values, "/me/profile"),
                cancellationToken);

            if (celebration.ManagerEmployeeId is { } manager && manager != celebration.EmployeeId)
            {
                await notifier.NotifyAsync(
                    new NotificationRequest(birthday ? NotificationTemplates.BirthdayManagerNotice : NotificationTemplates.AnniversaryManagerNotice, [new NotificationRecipient(manager)], values, "/team"),
                    cancellationToken);
            }

            dbContext.CelebrationWishes.Add(CelebrationWish.Record(celebration.EmployeeId, body.Kind, target.Date, celebration.Years, note, currentUser.UserId, senderName, now));
            alreadyWished.Add((target.EmployeeId, target.Date));
            sent++;
        }

        // The wishes and the queued notifications commit together.
        await dbContext.SaveChangesAsync(cancellationToken);
        return new SendWishesResult(sent, skipped);
    }

    /// <summary>On the day, or up to <see cref="LateDays"/> days late.</summary>
    public static bool IsSendable(DateOnly occasion, DateOnly today)
        => occasion <= today && today.DayNumber - occasion.DayNumber <= LateDays;

    private static string When(DateOnly occasion, DateOnly today) => (today.DayNumber - occasion.DayNumber) switch
    {
        0 => "today",
        1 => "yesterday",
        int days => $"{days} days ago",
    };

    private Task<IReadOnlyList<Celebration>> FindAsync(CelebrationKind kind, DateOnly first, DateOnly last, IReadOnlyCollection<Guid>? scope, CancellationToken cancellationToken)
        => kind == CelebrationKind.Birthday
            ? celebrations.BirthdaysAsync(first, last, scope, cancellationToken)
            : celebrations.AnniversariesAsync(first, last, scope, cancellationToken);

    private async Task<IReadOnlyDictionary<Guid, string>> ManagerNamesAsync(IReadOnlyList<Celebration> found, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. found.Where(row => row.ManagerEmployeeId != null).Select(row => row.ManagerEmployeeId!.Value).Distinct()];
        return ids.Count == 0
            ? new Dictionary<Guid, string>()
            : (await employees.GetManyAsync(ids, cancellationToken)).ToDictionary(row => row.Id, row => row.DisplayName);
    }

    private async Task<string> SenderNameAsync(CancellationToken cancellationToken)
        => currentUser.EmployeeId is { } employeeId && await employees.GetAsync(employeeId, cancellationToken) is { } me ? me.DisplayName : "HR";
}
