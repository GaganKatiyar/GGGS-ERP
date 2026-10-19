using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Persistence;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Organization.Contracts;
using Gggs.Modules.Payroll.Contracts;
using Gggs.Modules.Rewards.Domain;
using Gggs.Modules.Rewards.Infrastructure;
using Gggs.Modules.Workflow.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Rewards.Application;

public sealed record SettingsView(decimal PointValue, string Currency, int ExpiryMonths, int MonthlyPeerAllowance);

public sealed record TypeItem(Guid Id, string Code, string Name, string? Description, RecognitionKind Kind, int Points, bool RequiresApproval, decimal? CashAmount, bool IsActive);

public sealed record TypeBody(string Code, string Name, string? Description, RecognitionKind Kind, int Points, bool RequiresApproval, decimal? CashAmount, bool IsActive = true);

public sealed record RuleItem(Guid Id, string Name, RuleTrigger Trigger, int Points, int Threshold, bool PerYear, string Message, bool IsActive);

public sealed record RuleBody(string Name, RuleTrigger Trigger, int Points, int Threshold, bool PerYear, string Message, bool IsActive = true);

public sealed record RecognitionItem(
    Guid Id,
    Guid? GiverEmployeeId,
    string? GiverName,
    Guid RecipientEmployeeId,
    string? RecipientName,
    string? TypeName,
    string? RuleName,
    RecognitionKind? Kind,
    string Message,
    int Points,
    decimal? CashAmount,
    bool IsPublic,
    RecognitionStatus Status,
    DateOnly GivenOn,
    int Cheers,
    bool CheeredByMe);

public sealed record GiveBody(Guid RecipientEmployeeId, Guid TypeId, string Message, bool IsPublic = true);

public sealed record LedgerItem(Guid Id, PointEntryKind Kind, int Points, int RemainingPoints, DateOnly OccurredOn, DateOnly? ExpiresOn, string Note);

public sealed record WalletView(int Balance, int ExpiringSoon, DateOnly? NextExpiry, int EarnedThisYear, int RedeemedThisYear, int PeerAllowance, int PeerAllowanceLeft, decimal PointValue, string Currency);

public sealed record CatalogItemView(Guid Id, string Name, string? Description, string Category, int PointsCost, int? Stock, bool IsActive);

public sealed record CatalogBody(string Name, string? Description, string Category, int PointsCost, int? Stock, bool IsActive = true);

public sealed record RedemptionItem(Guid Id, Guid EmployeeId, string? EmployeeName, Guid ItemId, string ItemName, int Points, RedemptionStatus Status, string? Note, DateOnly RequestedOn, DateOnly? FulfilledOn);

public sealed record RedeemBody(Guid ItemId);

public sealed record NoteBody(string? Note);

public sealed record BudgetItem(Guid Id, int Year, Guid? DepartmentId, string Department, int Points, int Used, double UsedPercent);

public sealed record BudgetBody(int Year, Guid? DepartmentId, int Points);

public sealed record AdjustBody(Guid EmployeeId, int Points, string Note);

public sealed record LeaderEntry(Guid EmployeeId, string? Name, int Count, int Points);

public sealed record RewardDashboard(
    int RecognitionsThisMonth,
    int PointsAwardedThisYear,
    decimal ValueAwardedThisYear,
    int PendingApprovals,
    int PendingRedemptions,
    int PointsOutstanding,
    IReadOnlyList<LeaderEntry> TopRecipients,
    IReadOnlyList<LeaderEntry> TopGivers,
    IReadOnlyList<BudgetItem> Budgets);

/// <summary>
/// Rewards and recognition (spec: rewards): recognition types and rules, a public feed with cheers,
/// a points wallet whose credits lapse and are spent soonest-expiring first, a catalogue to redeem
/// against, budgets for manager awards, and cash awards through payroll.
/// </summary>
internal sealed class RewardService(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    IOrgStructure organization,
    IWorkflowStarter workflow,
    IPayrollInputs payrollInputs,
    INotifier notifier,
    IPermissionProvider permissions,
    ICurrentUser currentUser,
    ITenantClock clock)
{
    public const string SubjectType = "rewards.recognition";
    public const string Source = "rewards.recognition";

    public static WorkflowSubject Subject { get; } = new(
        SubjectType,
        "Approve a recognition award",
        "rewards",
        [
            new WorkflowSubjectField("points", "Points", WorkflowFieldType.Number),
            new WorkflowSubjectField("cash", "Cash amount", WorkflowFieldType.Number),
            new WorkflowSubjectField("kind", "Kind (Peer, Manager, Award)", WorkflowFieldType.Text),
        ]);

    // ----- Settings, types, rules, budgets ------------------------------------------------------

    public async Task<SettingsView> SettingsAsync(CancellationToken cancellationToken)
    {
        RewardSettings settings = await SettingsRowAsync(cancellationToken);
        return new SettingsView(settings.PointValue, settings.Currency, settings.ExpiryMonths, settings.MonthlyPeerAllowance);
    }

    public async Task<SettingsView> SaveSettingsAsync(SettingsView body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        RewardSettings settings = await SettingsRowAsync(cancellationToken);
        settings.Update(body.PointValue, body.Currency, body.ExpiryMonths, body.MonthlyPeerAllowance);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await SettingsAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TypeItem>> TypesAsync(CancellationToken cancellationToken)
        => [.. (await dbContext.RecognitionTypes.AsNoTracking().OrderBy(row => row.Kind).ThenBy(row => row.Points).ToListAsync(cancellationToken))
            .Select(row => new TypeItem(row.Id, row.Code, row.Name, row.Description, row.Kind, row.Points, row.RequiresApproval, row.CashAmount, row.IsActive))];

    public async Task<IReadOnlyList<TypeItem>> SaveTypeAsync(Guid? id, TypeBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        string code = (body.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (await dbContext.RecognitionTypes.AnyAsync(row => row.Code == code && row.Id != id, cancellationToken))
        {
            throw new DomainRuleException("RECOGNITION_TYPE_CODE", $"A recognition type already uses the code {code}.");
        }

        if (id is { } existing)
        {
            RecognitionType type = await dbContext.RecognitionTypes.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(RecognitionType), existing);
            type.Update(code, body.Name, body.Description, body.Kind, body.Points, body.RequiresApproval, body.CashAmount, body.IsActive);
        }
        else
        {
            dbContext.RecognitionTypes.Add(RecognitionType.Create(code, body.Name, body.Description, body.Kind, body.Points, body.RequiresApproval, body.CashAmount));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await TypesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RuleItem>> RulesAsync(CancellationToken cancellationToken)
        => [.. (await dbContext.RewardRules.AsNoTracking().OrderBy(row => row.Trigger).ToListAsync(cancellationToken))
            .Select(row => new RuleItem(row.Id, row.Name, row.Trigger, row.Points, row.Threshold, row.PerYear, row.Message, row.IsActive))];

    public async Task<IReadOnlyList<RuleItem>> SaveRuleAsync(Guid? id, RuleBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (id is { } existing)
        {
            RewardRule rule = await dbContext.RewardRules.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(RewardRule), existing);
            rule.Update(body.Name, body.Trigger, body.Points, body.Threshold, body.PerYear, body.Message, body.IsActive);
        }
        else
        {
            dbContext.RewardRules.Add(RewardRule.Create(body.Name, body.Trigger, body.Points, body.Threshold, body.PerYear, body.Message));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await RulesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BudgetItem>> BudgetsAsync(int? year, CancellationToken cancellationToken)
    {
        int wanted = year ?? (await clock.TodayAsync(cancellationToken)).Year;
        List<RewardBudget> budgets = await dbContext.RewardBudgets.AsNoTracking().Where(row => row.Year == wanted).ToListAsync(cancellationToken);
        List<Guid> ids = [.. budgets.Select(row => row.Id)];
        Dictionary<Guid, int> used = await dbContext.Recognitions.AsNoTracking()
            .Where(row => row.BudgetId != null && ids.Contains(row.BudgetId.Value) && row.Status != RecognitionStatus.Rejected)
            .GroupBy(row => row.BudgetId!.Value).Select(group => new { group.Key, Points = group.Sum(row => row.Points) })
            .ToDictionaryAsync(row => row.Key, row => row.Points, cancellationToken);
        IReadOnlyDictionary<Guid, string> departments = await DepartmentNamesAsync(budgets.Where(row => row.DepartmentId != null).Select(row => row.DepartmentId!.Value), cancellationToken);
        return [.. budgets
            .Select(row => new BudgetItem(row.Id, row.Year, row.DepartmentId, row.DepartmentId is { } department ? departments.GetValueOrDefault(department) ?? "Unknown" : "Company-wide",
                row.Points, used.GetValueOrDefault(row.Id), row.Points == 0 ? 0 : Math.Round(100.0 * used.GetValueOrDefault(row.Id) / row.Points, 1)))
            .OrderBy(row => row.DepartmentId != null).ThenBy(row => row.Department, StringComparer.Ordinal)];
    }

    public async Task<IReadOnlyList<BudgetItem>> SaveBudgetAsync(BudgetBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        RewardBudget? budget = await dbContext.RewardBudgets.FirstOrDefaultAsync(row => row.Year == body.Year && row.DepartmentId == body.DepartmentId, cancellationToken);
        if (budget is null)
        {
            dbContext.RewardBudgets.Add(RewardBudget.Create(body.Year, body.DepartmentId, body.Points));
        }
        else
        {
            budget.Update(body.Points);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await BudgetsAsync(body.Year, cancellationToken);
    }

    // ----- Recognition --------------------------------------------------------------------------

    public async Task<IReadOnlyList<RecognitionItem>> FeedAsync(CancellationToken cancellationToken)
    {
        Guid? me = currentUser.EmployeeId;
        List<Recognition> rows = await dbContext.Recognitions.AsNoTracking()
            .Where(row => row.Status == RecognitionStatus.Approved && (row.IsPublic || row.RecipientEmployeeId == me || row.GiverEmployeeId == me))
            .OrderByDescending(row => row.GivenOn).ThenByDescending(row => row.CreatedDate).Take(100).ToListAsync(cancellationToken);
        return await ItemsAsync(rows, cancellationToken);
    }

    public async Task<IReadOnlyList<RecognitionItem>> RecognitionsAsync(RecognitionStatus? status, Guid? employeeId, CancellationToken cancellationToken)
    {
        IQueryable<Recognition> rows = dbContext.Recognitions.AsNoTracking();
        if (status is { } wanted)
        {
            rows = rows.Where(row => row.Status == wanted);
        }

        if (employeeId is { } employee)
        {
            rows = rows.Where(row => row.RecipientEmployeeId == employee || row.GiverEmployeeId == employee);
        }

        return await ItemsAsync(await rows.OrderByDescending(row => row.GivenOn).Take(500).ToListAsync(cancellationToken), cancellationToken);
    }

    /// <summary>Recognitions an employee received - for their profile.</summary>
    public async Task<IReadOnlyList<RecognitionItem>> ReceivedAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        bool admin = (await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Reward.RecognitionView);
        Guid? me = currentUser.EmployeeId;
        List<Recognition> rows = await dbContext.Recognitions.AsNoTracking()
            .Where(row => row.RecipientEmployeeId == employeeId && row.Status == RecognitionStatus.Approved && (admin || row.IsPublic || row.RecipientEmployeeId == me))
            .OrderByDescending(row => row.GivenOn).Take(100).ToListAsync(cancellationToken);
        return await ItemsAsync(rows, cancellationToken);
    }

    /// <summary>
    /// Gives a recognition. Peer recognition comes out of the giver's monthly allowance; manager
    /// recognition is for the giver's own reports and draws on the department (or company) budget;
    /// awards are for the programme team. Types that need approval go through the workflow.
    /// </summary>
    public async Task<RecognitionItem> GiveAsync(GiveBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Guid me = Me();
        DateOnly today = await clock.TodayAsync(cancellationToken);
        RecognitionType type = await dbContext.RecognitionTypes.AsNoTracking().FirstOrDefaultAsync(row => row.Id == body.TypeId && row.IsActive, cancellationToken)
            ?? throw new DomainRuleException("RECOGNITION_TYPE", "Choose an active recognition type.");
        EmployeeSummary recipient = await employees.GetAsync(body.RecipientEmployeeId, cancellationToken) is { } person && person.ExitDate is null
            ? person
            : throw new DomainRuleException("RECOGNITION_RECIPIENT", "Recognise an active colleague.");
        bool programme = (await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Reward.ProgramManage);
        Guid? budgetId = null;
        switch (type.Kind)
        {
            case RecognitionKind.Peer:
                RewardSettings settings = await SettingsRowAsync(cancellationToken);
                int given = await PeerGivenAsync(me, today, cancellationToken);
                if (given + type.Points > settings.MonthlyPeerAllowance)
                {
                    throw new DomainRuleException("RECOGNITION_ALLOWANCE", $"This would exceed your monthly allowance of {settings.MonthlyPeerAllowance} points ({settings.MonthlyPeerAllowance - given} left).");
                }

                break;
            case RecognitionKind.Manager:
                if (recipient.ManagerEmployeeId != me && !programme)
                {
                    throw new ForbiddenException("RECOGNITION_MANAGER", "Manager recognition is for your own team.");
                }

                budgetId = await BudgetForAsync(recipient, type.Points, today, cancellationToken);
                break;
            default:
                if (!programme)
                {
                    throw new ForbiddenException("RECOGNITION_AWARD", "Awards are given by the rewards programme team.");
                }

                budgetId = await BudgetForAsync(recipient, type.Points, today, cancellationToken);
                break;
        }

        var recognition = Recognition.Give(me, recipient.Id, type, body.Message, body.IsPublic, budgetId, today);
        dbContext.Recognitions.Add(recognition);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (type.RequiresApproval)
        {
            WorkflowStartResult started = await workflow.StartAsync(
                new WorkflowStartRequest
                {
                    SubjectType = SubjectType,
                    SubjectId = recognition.Id,
                    Title = $"{type.Name} for {recipient.DisplayName} ({type.Points} points)",
                    RequesterEmployeeId = me,
                    Link = "/rewards/recognitions",
                    Fields = new Dictionary<string, string>
                    {
                        ["points"] = type.Points.ToString(CultureInfo.InvariantCulture),
                        ["cash"] = (type.CashAmount ?? 0).ToString(CultureInfo.InvariantCulture),
                        ["kind"] = type.Kind.ToString(),
                    },
                },
                cancellationToken);
            if (started.Outcome == WorkflowStartOutcome.Pending)
            {
                recognition.AwaitApproval(started.InstanceId!.Value);
                await dbContext.SaveChangesAsync(cancellationToken);
                return (await ItemsAsync([recognition], cancellationToken))[0];
            }
        }

        await ApproveAsync(recognition, type.Name, cancellationToken);
        return (await ItemsAsync([recognition], cancellationToken))[0];
    }

    public async Task DecideAsync(Guid id, bool approved, CancellationToken cancellationToken)
    {
        Recognition? recognition = await dbContext.Recognitions.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (recognition?.Status != RecognitionStatus.Pending)
        {
            return;
        }

        if (approved)
        {
            string? typeName = await dbContext.RecognitionTypes.AsNoTracking().Where(row => row.Id == recognition.TypeId).Select(row => row.Name).FirstOrDefaultAsync(cancellationToken);
            await ApproveAsync(recognition, typeName ?? "Recognition", cancellationToken);
        }
        else
        {
            recognition.Decide(approved: false);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<RecognitionItem> CheerAsync(Guid id, CancellationToken cancellationToken)
    {
        Guid me = Me();
        Recognition recognition = await dbContext.Recognitions.FirstOrDefaultAsync(row => row.Id == id && row.Status == RecognitionStatus.Approved && (row.IsPublic || row.RecipientEmployeeId == me || row.GiverEmployeeId == me), cancellationToken)
            ?? throw new NotFoundException(nameof(Recognition), id);
        if (!await dbContext.RecognitionCheers.AnyAsync(row => row.RecognitionId == id && row.EmployeeId == me, cancellationToken))
        {
            dbContext.RecognitionCheers.Add(RecognitionCheer.Add(id, me));
            recognition.Cheered();
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return (await ItemsAsync([recognition], cancellationToken))[0];
    }

    // ----- Rules --------------------------------------------------------------------------------

    public async Task<int> OnReviewReleasedAsync(Guid employeeId, Guid reviewId, int rating, CancellationToken cancellationToken)
    {
        List<RewardRule> rules = await dbContext.RewardRules.AsNoTracking().Where(row => row.Trigger == RuleTrigger.ReviewRating && row.IsActive).ToListAsync(cancellationToken);
        int fired = 0;
        foreach (RewardRule rule in rules.Where(rule => rule.Applies(rating)))
        {
            fired += await FireAsync(rule, employeeId, rule.Points, rule.Message.Replace("{rating}", rating.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal), $"review:{reviewId:N}", cancellationToken);
        }

        return fired;
    }

    public async Task<int> OnCourseCompletedAsync(Guid employeeId, Guid enrollmentId, string courseTitle, CancellationToken cancellationToken)
    {
        List<RewardRule> rules = await dbContext.RewardRules.AsNoTracking().Where(row => row.Trigger == RuleTrigger.CourseCompleted && row.IsActive).ToListAsync(cancellationToken);
        int fired = 0;
        foreach (RewardRule rule in rules)
        {
            fired += await FireAsync(rule, employeeId, rule.Points, rule.Message.Replace("{course}", courseTitle, StringComparison.Ordinal), $"course:{enrollmentId:N}", cancellationToken);
        }

        return fired;
    }

    /// <summary>The daily sweep: work-anniversary points, expiry of lapsed points, and a 30-day warning before points lapse.</summary>
    public async Task<(int Anniversaries, int Expired, int Warned)> SweepAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        int anniversaries = 0;
        List<RewardRule> rules = await dbContext.RewardRules.AsNoTracking().Where(row => row.Trigger == RuleTrigger.WorkAnniversary && row.IsActive).ToListAsync(cancellationToken);
        if (rules.Count > 0)
        {
            IReadOnlyList<Guid> active = await employees.ListActiveIdsAsync(null, null, cancellationToken);
            foreach (EmployeeSummary person in await employees.GetManyAsync(active, cancellationToken))
            {
                if (person.JoiningDate is not { } joined || !IsAnniversary(joined, today))
                {
                    continue;
                }

                int years = today.Year - joined.Year;
                foreach (RewardRule rule in rules.Where(rule => years >= 1 && rule.Applies(years)))
                {
                    anniversaries += await FireAsync(rule, person.Id, rule.PointsFor(years), rule.Message.Replace("{years}", years.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal), $"anniversary:{today.Year}", cancellationToken);
                }
            }
        }

        List<PointEntry> lapsed = await dbContext.PointEntries.Where(row => row.RemainingPoints > 0 && row.ExpiresOn != null && row.ExpiresOn < today).ToListAsync(cancellationToken);
        foreach (PointEntry credit in lapsed)
        {
            int points = credit.Consume(credit.RemainingPoints);
            dbContext.PointEntries.Add(PointEntry.Debit(credit.EmployeeId, PointEntryKind.Expired, points, today, "rewards.points", credit.Id, $"Points earned {credit.OccurredOn:d MMM yyyy} lapsed"));
        }

        DateOnly horizon = today.AddDays(30);
        List<PointEntry> expiring = await dbContext.PointEntries.Where(row => row.RemainingPoints > 0 && !row.ExpiryWarned && row.ExpiresOn != null && row.ExpiresOn >= today && row.ExpiresOn <= horizon).ToListAsync(cancellationToken);
        foreach (PointEntry credit in expiring)
        {
            credit.Warned();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (IGrouping<Guid, PointEntry> group in expiring.GroupBy(row => row.EmployeeId))
        {
            await notifier.NotifyAsync(
                new NotificationRequest(
                    NotificationTemplates.PointsExpiring,
                    [new NotificationRecipient(group.Key)],
                    new Dictionary<string, string>
                    {
                        ["points"] = group.Sum(row => row.RemainingPoints).ToString(CultureInfo.InvariantCulture),
                        ["expiresOn"] = group.Min(row => row.ExpiresOn!.Value).ToString("d MMM yyyy", CultureInfo.InvariantCulture),
                    },
                    "/me/rewards"),
                cancellationToken);
        }

        return (anniversaries, lapsed.Count, expiring.Select(row => row.EmployeeId).Distinct().Count());
    }

    // ----- Wallet, catalogue and redemption ------------------------------------------------------

    public async Task<WalletView> WalletAsync(CancellationToken cancellationToken)
    {
        Guid me = Me();
        DateOnly today = await clock.TodayAsync(cancellationToken);
        var yearStart = new DateOnly(today.Year, 1, 1);
        DateOnly horizon = today.AddDays(30);
        List<PointEntry> entries = await dbContext.PointEntries.AsNoTracking().Where(row => row.EmployeeId == me).ToListAsync(cancellationToken);
        RewardSettings settings = await SettingsRowAsync(cancellationToken);
        int given = await PeerGivenAsync(me, today, cancellationToken);
        List<PointEntry> open = [.. entries.Where(row => row.RemainingPoints > 0 && row.ExpiresOn != null)];
        return new WalletView(
            entries.Sum(row => row.Points),
            open.Where(row => row.ExpiresOn <= horizon).Sum(row => row.RemainingPoints),
            open.Count == 0 ? null : open.Min(row => row.ExpiresOn),
            entries.Where(row => row.Kind == PointEntryKind.Earned && row.OccurredOn >= yearStart).Sum(row => row.Points),
            -entries.Where(row => row.Kind == PointEntryKind.Redeemed && row.OccurredOn >= yearStart).Sum(row => row.Points),
            settings.MonthlyPeerAllowance,
            Math.Max(0, settings.MonthlyPeerAllowance - given),
            settings.PointValue,
            settings.Currency);
    }

    public async Task<IReadOnlyList<LedgerItem>> LedgerAsync(CancellationToken cancellationToken)
    {
        Guid me = Me();
        return [.. (await dbContext.PointEntries.AsNoTracking().Where(row => row.EmployeeId == me).OrderByDescending(row => row.OccurredOn).ThenByDescending(row => row.CreatedDate).Take(300).ToListAsync(cancellationToken))
            .Select(row => new LedgerItem(row.Id, row.Kind, row.Points, row.RemainingPoints, row.OccurredOn, row.ExpiresOn, row.Note))];
    }

    public async Task<IReadOnlyList<CatalogItemView>> CatalogAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        IQueryable<CatalogItem> rows = dbContext.CatalogItems.AsNoTracking();
        if (!includeInactive)
        {
            rows = rows.Where(row => row.IsActive);
        }

        return [.. (await rows.OrderBy(row => row.Category).ThenBy(row => row.PointsCost).ToListAsync(cancellationToken))
            .Select(row => new CatalogItemView(row.Id, row.Name, row.Description, row.Category, row.PointsCost, row.Stock, row.IsActive))];
    }

    public async Task<IReadOnlyList<CatalogItemView>> SaveItemAsync(Guid? id, CatalogBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (id is { } existing)
        {
            CatalogItem item = await dbContext.CatalogItems.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(CatalogItem), existing);
            item.Update(body.Name, body.Description, body.Category, body.PointsCost, body.Stock, body.IsActive);
        }
        else
        {
            dbContext.CatalogItems.Add(CatalogItem.Create(body.Name, body.Description, body.Category, body.PointsCost, body.Stock));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await CatalogAsync(includeInactive: true, cancellationToken);
    }

    public async Task<RedemptionItem> RedeemAsync(Guid itemId, CancellationToken cancellationToken)
    {
        Guid me = Me();
        DateOnly today = await clock.TodayAsync(cancellationToken);
        CatalogItem item = await dbContext.CatalogItems.FirstOrDefaultAsync(row => row.Id == itemId, cancellationToken) ?? throw new NotFoundException(nameof(CatalogItem), itemId);
        item.Take();
        List<PointEntry> credits = await dbContext.PointEntries.Where(row => row.EmployeeId == me && row.RemainingPoints > 0).ToListAsync(cancellationToken);
        PointEntry.SpendFifo(credits, item.PointsCost);
        var redemption = Redemption.Request(me, item, today);
        dbContext.Redemptions.Add(redemption);
        dbContext.PointEntries.Add(PointEntry.Debit(me, PointEntryKind.Redeemed, item.PointsCost, today, "rewards.redemption", redemption.Id, $"Redeemed {item.Name}"));
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await RedemptionItemsAsync([redemption], cancellationToken))[0];
    }

    public async Task<IReadOnlyList<RedemptionItem>> MyRedemptionsAsync(CancellationToken cancellationToken)
    {
        Guid me = Me();
        return await RedemptionItemsAsync(await dbContext.Redemptions.AsNoTracking().Where(row => row.EmployeeId == me).OrderByDescending(row => row.RequestedOn).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<RedemptionItem>> RedemptionsAsync(RedemptionStatus? status, CancellationToken cancellationToken)
    {
        IQueryable<Redemption> rows = dbContext.Redemptions.AsNoTracking();
        if (status is { } wanted)
        {
            rows = rows.Where(row => row.Status == wanted);
        }

        return await RedemptionItemsAsync(await rows.OrderByDescending(row => row.RequestedOn).Take(500).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<RedemptionItem> FulfilAsync(Guid id, string? note, CancellationToken cancellationToken)
    {
        Redemption redemption = await dbContext.Redemptions.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Redemption), id);
        redemption.Fulfil(note, await clock.TodayAsync(cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyRedemptionAsync(redemption, "fulfilled", cancellationToken);
        return (await RedemptionItemsAsync([redemption], cancellationToken))[0];
    }

    /// <summary>Cancels a requested redemption - by the programme team, or by the employee themselves - refunding the points and the stock.</summary>
    public async Task<RedemptionItem> CancelRedemptionAsync(Guid id, string? note, bool own, CancellationToken cancellationToken)
    {
        Redemption redemption = await dbContext.Redemptions.FirstOrDefaultAsync(row => row.Id == id && (!own || row.EmployeeId == currentUser.EmployeeId), cancellationToken) ?? throw new NotFoundException(nameof(Redemption), id);
        redemption.Cancel(note);
        CatalogItem? item = await dbContext.CatalogItems.FirstOrDefaultAsync(row => row.Id == redemption.ItemId, cancellationToken);
        item?.PutBack();
        DateOnly today = await clock.TodayAsync(cancellationToken);
        RewardSettings settings = await SettingsRowAsync(cancellationToken);
        dbContext.PointEntries.Add(PointEntry.Credit(redemption.EmployeeId, PointEntryKind.Refunded, redemption.Points, today, settings.ExpiryFrom(today), "rewards.redemption", redemption.Id, $"Refund for {redemption.ItemName}"));
        await dbContext.SaveChangesAsync(cancellationToken);
        if (!own)
        {
            await NotifyRedemptionAsync(redemption, "cancelled and your points refunded", cancellationToken);
        }

        return (await RedemptionItemsAsync([redemption], cancellationToken))[0];
    }

    /// <summary>A manual correction by the programme team: positive credits, negative debits.</summary>
    public async Task<IReadOnlyList<LedgerItem>> AdjustAsync(AdjustBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.Points == 0 || !await employees.IsActiveAsync(body.EmployeeId, cancellationToken))
        {
            throw new DomainRuleException("POINTS_ADJUST", "Adjust an active employee's points by a non-zero amount.");
        }

        string note = $"Adjustment: {Guard(body.Note)}";
        DateOnly today = await clock.TodayAsync(cancellationToken);
        if (body.Points > 0)
        {
            RewardSettings settings = await SettingsRowAsync(cancellationToken);
            dbContext.PointEntries.Add(PointEntry.Credit(body.EmployeeId, PointEntryKind.Adjusted, body.Points, today, settings.ExpiryFrom(today), "rewards.adjustment", null, note));
        }
        else
        {
            List<PointEntry> credits = await dbContext.PointEntries.Where(row => row.EmployeeId == body.EmployeeId && row.RemainingPoints > 0).ToListAsync(cancellationToken);
            PointEntry.SpendFifo(credits, -body.Points);
            dbContext.PointEntries.Add(PointEntry.Debit(body.EmployeeId, PointEntryKind.Adjusted, -body.Points, today, "rewards.adjustment", null, note));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return [.. (await dbContext.PointEntries.AsNoTracking().Where(row => row.EmployeeId == body.EmployeeId).OrderByDescending(row => row.OccurredOn).Take(50).ToListAsync(cancellationToken))
            .Select(row => new LedgerItem(row.Id, row.Kind, row.Points, row.RemainingPoints, row.OccurredOn, row.ExpiresOn, row.Note))];
    }

    public async Task<RewardDashboard> DashboardAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var yearStart = new DateOnly(today.Year, 1, 1);
        DateOnly quarterStart = new(today.Year, ((today.Month - 1) / 3 * 3) + 1, 1);
        List<Recognition> year = await dbContext.Recognitions.AsNoTracking().Where(row => row.GivenOn >= yearStart).ToListAsync(cancellationToken);
        List<Recognition> approved = [.. year.Where(row => row.Status == RecognitionStatus.Approved)];
        RewardSettings settings = await SettingsRowAsync(cancellationToken);
        int pointsAwarded = approved.Sum(row => row.Points);
        List<Recognition> quarter = [.. approved.Where(row => row.GivenOn >= quarterStart)];
        Dictionary<Guid, string> names = await NamesAsync(quarter.Select(row => row.RecipientEmployeeId).Concat(quarter.Where(row => row.GiverEmployeeId != null).Select(row => row.GiverEmployeeId!.Value)), cancellationToken);
        return new RewardDashboard(
            approved.Count(row => row.GivenOn >= monthStart),
            pointsAwarded,
            Math.Round(pointsAwarded * settings.PointValue, 2),
            year.Count(row => row.Status == RecognitionStatus.Pending),
            await dbContext.Redemptions.CountAsync(row => row.Status == RedemptionStatus.Requested, cancellationToken),
            await dbContext.PointEntries.SumAsync(row => row.RemainingPoints, cancellationToken),
            [.. quarter.GroupBy(row => row.RecipientEmployeeId).Select(group => new LeaderEntry(group.Key, names.GetValueOrDefault(group.Key), group.Count(), group.Sum(row => row.Points)))
                .OrderByDescending(row => row.Points).Take(5)],
            [.. quarter.Where(row => row.GiverEmployeeId != null).GroupBy(row => row.GiverEmployeeId!.Value).Select(group => new LeaderEntry(group.Key, names.GetValueOrDefault(group.Key), group.Count(), group.Sum(row => row.Points)))
                .OrderByDescending(row => row.Count).Take(5)],
            await BudgetsAsync(today.Year, cancellationToken));
    }

    // ----- Internals ----------------------------------------------------------------------------

    private static string Guard(string? note) => string.IsNullOrWhiteSpace(note) ? throw new DomainRuleException("POINTS_ADJUST_NOTE", "Say why the points are adjusted.") : note.Trim();

    private static bool IsAnniversary(DateOnly joined, DateOnly today)
        => joined.Month == today.Month && (joined.Day == today.Day || (joined is { Month: 2, Day: 29 } && today.Day == 28 && !DateTime.IsLeapYear(today.Year)));

    private Guid Me() => currentUser.EmployeeId ?? throw new DomainRuleException("REWARD_NO_EMPLOYEE", "Your account is not linked to an employee record.");

    private async Task<RewardSettings> SettingsRowAsync(CancellationToken cancellationToken)
    {
        RewardSettings? settings = await dbContext.RewardSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = Domain.RewardSettings.Default();
            dbContext.RewardSettings.Add(settings);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return settings;
    }

    private async Task<int> PeerGivenAsync(Guid giver, DateOnly today, CancellationToken cancellationToken)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        return await dbContext.Recognitions.AsNoTracking()
            .Where(row => row.GiverEmployeeId == giver && row.Kind == RecognitionKind.Peer && row.GivenOn >= monthStart && row.Status != RecognitionStatus.Rejected)
            .SumAsync(row => row.Points, cancellationToken);
    }

    /// <summary>The recipient's department budget for the year, else the company budget; none means unlimited.</summary>
    private async Task<Guid?> BudgetForAsync(EmployeeSummary recipient, int points, DateOnly today, CancellationToken cancellationToken)
    {
        List<RewardBudget> budgets = await dbContext.RewardBudgets.AsNoTracking().Where(row => row.Year == today.Year && (row.DepartmentId == null || row.DepartmentId == recipient.DepartmentId)).ToListAsync(cancellationToken);
        RewardBudget? budget = budgets.FirstOrDefault(row => row.DepartmentId != null) ?? budgets.FirstOrDefault();
        if (budget is null)
        {
            return null;
        }

        int used = await dbContext.Recognitions.AsNoTracking().Where(row => row.BudgetId == budget.Id && row.Status != RecognitionStatus.Rejected).SumAsync(row => row.Points, cancellationToken);
        if (used + points > budget.Points)
        {
            throw new DomainRuleException("REWARD_BUDGET_EXCEEDED", $"The {today.Year} rewards budget has {budget.Points - used} points left.");
        }

        return budget.Id;
    }

    /// <summary>Approves a recognition: credits the points, sends any cash to payroll, and tells the recipient.</summary>
    private async Task ApproveAsync(Recognition recognition, string label, CancellationToken cancellationToken)
    {
        if (recognition.Status == RecognitionStatus.Pending)
        {
            recognition.Decide(approved: true);
        }

        DateOnly today = await clock.TodayAsync(cancellationToken);
        if (recognition.Points > 0)
        {
            RewardSettings settings = await SettingsRowAsync(cancellationToken);
            dbContext.PointEntries.Add(PointEntry.Credit(recognition.RecipientEmployeeId, PointEntryKind.Earned, recognition.Points, today, settings.ExpiryFrom(today), "rewards.recognition", recognition.Id, label));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (recognition.CashAmount is > 0)
        {
            await payrollInputs.AddAsync(
                new PayrollInputRequest(recognition.RecipientEmployeeId, new DateOnly(today.Year, today.Month, 1), PayrollInputKind.Bonus, recognition.CashAmount.Value, $"Reward: {label}", Source, recognition.Id),
                cancellationToken);
        }

        string giver = recognition.GiverEmployeeId is { } giverId ? (await employees.GetAsync(giverId, cancellationToken))?.DisplayName ?? "A colleague" : "PeopleOS";
        await notifier.NotifyAsync(
            new NotificationRequest(
                NotificationTemplates.Recognized,
                [new NotificationRecipient(recognition.RecipientEmployeeId)],
                new Dictionary<string, string>
                {
                    ["giverName"] = giver,
                    ["typeName"] = label,
                    ["points"] = recognition.Points.ToString(CultureInfo.InvariantCulture),
                    ["message"] = recognition.Message,
                },
                "/me/rewards"),
            cancellationToken);
    }

    private async Task<int> FireAsync(RewardRule rule, Guid employeeId, int points, string message, string sourceKey, CancellationToken cancellationToken)
    {
        if (await dbContext.Recognitions.AnyAsync(row => row.RuleId == rule.Id && row.RecipientEmployeeId == employeeId && row.SourceKey == sourceKey, cancellationToken))
        {
            return 0;
        }

        var recognition = Recognition.FromRule(rule, employeeId, points, message, sourceKey, await clock.TodayAsync(cancellationToken));
        dbContext.Recognitions.Add(recognition);
        await ApproveAsync(recognition, rule.Name, cancellationToken);
        return 1;
    }

    private Task NotifyRedemptionAsync(Redemption redemption, string status, CancellationToken cancellationToken)
        => notifier.NotifyAsync(
            new NotificationRequest(
                NotificationTemplates.RedemptionUpdate,
                [new NotificationRecipient(redemption.EmployeeId)],
                new Dictionary<string, string> { ["itemName"] = redemption.ItemName, ["status"] = status, ["note"] = redemption.Note ?? string.Empty },
                "/me/rewards"),
            cancellationToken);

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
        => (await employees.GetManyAsync(ids.Distinct(), cancellationToken)).ToDictionary(person => person.Id, person => person.DisplayName);

    private async Task<IReadOnlyDictionary<Guid, string>> DepartmentNamesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        List<Guid> list = [.. ids.Distinct()];
        return list.Count == 0 ? new Dictionary<Guid, string>() : await organization.GetNamesAsync(OrgUnitKind.Department, list, cancellationToken);
    }

    private async Task<List<RecognitionItem>> ItemsAsync(IReadOnlyList<Recognition> rows, CancellationToken cancellationToken)
    {
        Dictionary<Guid, string> names = await NamesAsync(rows.Select(row => row.RecipientEmployeeId).Concat(rows.Where(row => row.GiverEmployeeId != null).Select(row => row.GiverEmployeeId!.Value)), cancellationToken);
        Dictionary<Guid, string> types = await dbContext.RecognitionTypes.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);
        Dictionary<Guid, string> rules = await dbContext.RewardRules.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);
        List<Guid> ids = [.. rows.Select(row => row.Id)];
        Guid? me = currentUser.EmployeeId;
        HashSet<Guid> cheered = [.. await dbContext.RecognitionCheers.AsNoTracking().Where(row => row.EmployeeId == me && ids.Contains(row.RecognitionId)).Select(row => row.RecognitionId).ToListAsync(cancellationToken)];
        return [.. rows.Select(row => new RecognitionItem(
            row.Id, row.GiverEmployeeId, row.GiverEmployeeId is { } giver ? names.GetValueOrDefault(giver) : null, row.RecipientEmployeeId, names.GetValueOrDefault(row.RecipientEmployeeId),
            row.TypeId is { } type ? types.GetValueOrDefault(type) : null, row.RuleId is { } rule ? rules.GetValueOrDefault(rule) : null, row.Kind, row.Message, row.Points, row.CashAmount,
            row.IsPublic, row.Status, row.GivenOn, row.Cheers, cheered.Contains(row.Id)))];
    }

    private async Task<List<RedemptionItem>> RedemptionItemsAsync(IReadOnlyList<Redemption> rows, CancellationToken cancellationToken)
    {
        Dictionary<Guid, string> names = await NamesAsync(rows.Select(row => row.EmployeeId), cancellationToken);
        return [.. rows.Select(row => new RedemptionItem(row.Id, row.EmployeeId, names.GetValueOrDefault(row.EmployeeId), row.ItemId, row.ItemName, row.Points, row.Status, row.Note, row.RequestedOn, row.FulfilledOn))];
    }
}

/// <summary>Seeds a tenant's reward settings, starter recognition types, rules and catalogue.</summary>
public sealed class RewardSeeder(AppDbContext dbContext)
{
    public async Task<int> SeedCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        int added = 0;
        if (!await dbContext.RewardSettings.IgnoreQueryFilters([QueryFilters.SoftDelete]).AnyAsync(cancellationToken))
        {
            dbContext.RewardSettings.Add(RewardSettings.Default());
            added++;
        }

        HashSet<string> codes = [.. await dbContext.RecognitionTypes.IgnoreQueryFilters([QueryFilters.SoftDelete]).Select(row => row.Code).ToListAsync(cancellationToken)];
        foreach ((string code, string name, string description, RecognitionKind kind, int points, bool approval, decimal? cash) in RewardDefaults.Types.Where(row => !codes.Contains(row.Code)))
        {
            dbContext.RecognitionTypes.Add(RecognitionType.Create(code, name, description, kind, points, approval, cash));
            added++;
        }

        if (!await dbContext.RewardRules.IgnoreQueryFilters([QueryFilters.SoftDelete]).AnyAsync(cancellationToken))
        {
            foreach ((string name, RuleTrigger trigger, int points, int threshold, bool perYear, string message) in RewardDefaults.Rules)
            {
                dbContext.RewardRules.Add(RewardRule.Create(name, trigger, points, threshold, perYear, message));
                added++;
            }
        }

        if (!await dbContext.CatalogItems.IgnoreQueryFilters([QueryFilters.SoftDelete]).AnyAsync(cancellationToken))
        {
            foreach ((string name, string description, string category, int cost, int? stock) in RewardDefaults.Catalog)
            {
                dbContext.CatalogItems.Add(CatalogItem.Create(name, description, category, cost, stock));
                added++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return added;
    }
}
