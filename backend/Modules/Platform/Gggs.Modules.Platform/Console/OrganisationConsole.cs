using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;
using Gggs.BuildingBlocks.Jobs;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Platform.Application.Provisioning;
using Gggs.Modules.Platform.Contracts;
using Gggs.Modules.Platform.Domain.Console;
using Gggs.Modules.Platform.Domain.Subscriptions;
using Gggs.Modules.Platform.Domain.Tenants;
using Gggs.Modules.Platform.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Platform.Console;

/// <summary>
/// The platform console's organisations: the list, one organisation, creating one, its lifecycle, its
/// subscription and modules - every change recorded in the platform audit trail. Reads only platform-owned
/// tables (tenants, plans, subscriptions, measured usage), never an organisation's own data.
/// </summary>
public sealed class OrganisationConsole(
    AppDbContext dbContext,
    TenantProvisioningService provisioning,
    IIntegrationEventPublisher publisher,
    ITenantRegistry registry,
    PlatformAuditWriter audit,
    IJobMonitor jobs,
    TimeProvider timeProvider)
{
    private static readonly SubscriptionStatus[] Live = [SubscriptionStatus.Trial, SubscriptionStatus.Active, SubscriptionStatus.PastDue];

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    private DateOnly Today => DateOnly.FromDateTime(Now);

    // ----- Reading -------------------------------------------------------------------------------

    public async Task<IReadOnlyList<OrganisationRow>> ListAsync(TenantStatus? status, string? search, CancellationToken cancellationToken)
    {
        IQueryable<Tenant> tenants = dbContext.Tenants.AsNoTracking();
        if (status is not null)
        {
            tenants = tenants.Where(tenant => tenant.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            tenants = tenants.Where(tenant => tenant.Code.Contains(term) || tenant.Name.Contains(term));
        }

        List<Tenant> rows = await tenants.OrderBy(tenant => tenant.Name).ToListAsync(cancellationToken);
        List<Guid> ids = [.. rows.Select(tenant => tenant.Id)];
        Dictionary<Guid, Subscription> subscriptions = (await dbContext.Subscriptions.AsNoTracking().Where(row => ids.Contains(row.TenantId) && Live.Contains(row.Status)).ToListAsync(cancellationToken))
            .ToDictionary(row => row.TenantId);
        Dictionary<Guid, Plan> plans = await dbContext.Plans.AsNoTracking().ToDictionaryAsync(plan => plan.Id, cancellationToken);
        Dictionary<Guid, TenantUsage> usage = await dbContext.TenantUsages.AsNoTracking().Where(row => ids.Contains(row.TenantId)).ToDictionaryAsync(row => row.TenantId, cancellationToken);

        return [.. rows.Select(tenant =>
        {
            Subscription? subscription = subscriptions.GetValueOrDefault(tenant.Id);
            Plan? plan = subscription is null ? null : plans.GetValueOrDefault(subscription.PlanId);
            return new OrganisationRow(
                tenant.Id, tenant.Code, tenant.Name, tenant.Status, plan?.Code, plan?.Name, subscription?.Status.ToString(), subscription?.SeatLimit,
                subscription?.TrialEndsOn, subscription?.RenewsOn, tenant.PurgeEligibleDate, tenant.SuspensionReason, tenant.DataRegion,
                Usage(usage.GetValueOrDefault(tenant.Id)), tenant.CreatedDate);
        })];
    }

    public async Task<OrganisationDetail> DetailAsync(Guid id, CancellationToken cancellationToken)
    {
        Tenant tenant = await dbContext.Tenants.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Tenant), id);
        Subscription? subscription = await LiveSubscriptionAsync(id, tracked: false, cancellationToken);
        Plan? plan = subscription is null ? null : await dbContext.Plans.AsNoTracking().FirstOrDefaultAsync(row => row.Id == subscription.PlanId, cancellationToken);
        TenantUsage? usage = await dbContext.TenantUsages.AsNoTracking().FirstOrDefaultAsync(row => row.TenantId == id, cancellationToken);

        return new OrganisationDetail(
            tenant.Id, tenant.Code, tenant.Name, tenant.LegalName, tenant.Status, tenant.IsolationTier, tenant.CountryCode, tenant.DataRegion,
            tenant.DefaultTimeZone, tenant.DefaultCurrency, tenant.DefaultLocale, tenant.ActivatedDate, tenant.SuspendedDate, tenant.SuspensionReason,
            tenant.PurgeEligibleDate,
            subscription is null || plan is null ? null : new SubscriptionView(
                subscription.Id, plan.Code, plan.Name, plan.MaxEmployees, subscription.Status.ToString(), subscription.StartDate,
                subscription.TrialEndsOn, subscription.RenewsOn, subscription.SeatLimit, subscription.BillingReference),
            subscription is null || plan is null ? [] : Modules(subscription, plan, Today),
            Usage(usage),
            tenant.CreatedDate);
    }

    public async Task<IReadOnlyList<AuditItem>> ActivityAsync(Guid? tenantId, int take, CancellationToken cancellationToken)
    {
        IQueryable<PlatformAuditEntry> rows = dbContext.PlatformAuditEntries.AsNoTracking();
        if (tenantId is { } id)
        {
            rows = rows.Where(row => row.TenantId == id);
        }

        return [.. (await rows.OrderByDescending(row => row.OccurredDate).Take(Math.Clamp(take, 1, 500)).ToListAsync(cancellationToken)).Select(Item)];
    }

    public async Task<PlansView> PlansAsync(CancellationToken cancellationToken)
    {
        List<Plan> plans = await dbContext.Plans.AsNoTracking().OrderBy(plan => plan.Edition).ToListAsync(cancellationToken);
        List<Subscription> live = await LiveForActiveOrganisationsAsync(cancellationToken);
        Dictionary<Guid, Plan> byId = plans.ToDictionary(plan => plan.Id);

        var featureCounts = Features.All.ToDictionary(code => code, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (Subscription subscription in live)
        {
            if (byId.GetValueOrDefault(subscription.PlanId) is { } plan)
            {
                foreach (string feature in subscription.ResolveFeatures(plan, Today))
                {
                    featureCounts[feature] = featureCounts.GetValueOrDefault(feature) + 1;
                }
            }
        }

        return new PlansView(
            [.. plans.Select(plan => new PlanView(plan.Code, plan.Name, plan.Edition.ToString(), plan.MaxEmployees, plan.IsActive, [.. plan.FeatureCodes], live.Count(row => row.PlanId == plan.Id)))],
            [.. Features.All.Select(code => new ModuleUsage(code, featureCounts.GetValueOrDefault(code)))]);
    }

    // ----- Creating and the lifecycle ------------------------------------------------------------

    /// <summary>
    /// Provisions the organisation, adds any agreed add-on modules, and opens it. The modules seed their
    /// defaults (roles, templates, the first administrator and their invitation email) from the
    /// provisioning event within moments.
    /// </summary>
    public async Task<OrganisationDetail> CreateAsync(CreateOrganisationBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Plan plan = await dbContext.Plans.AsNoTracking().FirstOrDefaultAsync(row => row.Code == body.PlanCode, cancellationToken)
            ?? throw new NotFoundException(nameof(Plan), body.PlanCode);
        if (body.SeatLimit is { } seats && (seats < 1 || (plan.MaxEmployees is { } max && seats > max)))
        {
            throw new DomainRuleException("SEAT_LIMIT_INVALID", plan.MaxEmployees is { } limit ? $"The employee limit must be between 1 and {limit:N0} on this plan." : "The employee limit must be at least 1.");
        }

        ProvisionTenantResult result = await provisioning.ProvisionAsync(
            new ProvisionTenantRequest
            {
                Code = Guard.NotNullOrWhiteSpace(body.Code).Trim().ToLowerInvariant(),
                Name = body.Name,
                LegalName = string.IsNullOrWhiteSpace(body.LegalName) ? null : body.LegalName.Trim(),
                CountryCode = body.CountryCode,
                DataRegion = body.DataRegion,
                DefaultTimeZone = body.DefaultTimeZone,
                DefaultCurrency = body.DefaultCurrency,
                DefaultLocale = body.DefaultLocale,
                PlanCode = plan.Code,
                IsolationTier = body.IsolationTier,
                TrialDays = body.TrialDays,
                SeatLimit = body.SeatLimit,
                BillingReference = body.BillingReference,
                AdministratorEmail = body.AdministratorEmail.Trim(),
            },
            cancellationToken);

        string[] addOns = [.. body.AddOns.Where(code => Features.All.Contains(code, StringComparer.OrdinalIgnoreCase) && !plan.FeatureCodes.Contains(code, StringComparer.OrdinalIgnoreCase))];
        if (addOns.Length > 0 && await LiveSubscriptionAsync(result.TenantId, tracked: true, cancellationToken) is { } subscription)
        {
            foreach (string code in addOns)
            {
                subscription.SetFeatureOverride(code, true, null, "Add-on agreed when the organisation was created.");
            }
        }

        audit.Record(
            "organisation.created",
            $"Created on {plan.Name}{(body.TrialDays is { } days ? $", {days}-day trial" : ", paid")}{(addOns.Length > 0 ? $", add-ons: {string.Join(", ", addOns)}" : string.Empty)}; first administrator {body.AdministratorEmail.Trim()}",
            result.TenantId,
            result.Code);
        await dbContext.SaveChangesAsync(cancellationToken);

        await provisioning.ActivateAsync(result.TenantId, cancellationToken);
        return await DetailAsync(result.TenantId, cancellationToken);
    }

    public Task ActivateAsync(Guid id, CancellationToken cancellationToken)
        => ActAsync(id, "organisation.activated", "Activated", null, () => provisioning.ActivateAsync(id, cancellationToken), cancellationToken);

    public Task SuspendAsync(Guid id, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainRuleException("SUSPEND_REASON_REQUIRED", "Say why the organisation is being suspended.");
        }

        return ActAsync(id, "organisation.suspended", "Suspended: nobody can sign in", reason.Trim(), () => provisioning.SuspendAsync(id, reason.Trim(), cancellationToken), cancellationToken);
    }

    public Task ReactivateAsync(Guid id, CancellationToken cancellationToken)
        => ActAsync(id, "organisation.reactivated", "Reactivated", null, () => provisioning.ReactivateAsync(id, cancellationToken), cancellationToken);

    public Task OffboardAsync(Guid id, int graceDays, CancellationToken cancellationToken)
        => ActAsync(id, "organisation.offboarding", $"Offboarding started, {graceDays}-day grace before data may be deleted", null, () => provisioning.BeginOffboardingAsync(id, TimeSpan.FromDays(graceDays), cancellationToken), cancellationToken);

    // ----- Subscription and modules ----------------------------------------------------------------

    public async Task<OrganisationDetail> UpdateSubscriptionAsync(Guid id, UpdateSubscriptionBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Tenant tenant = await TenantAsync(id, cancellationToken);
        Subscription subscription = await LiveSubscriptionAsync(id, tracked: true, cancellationToken)
            ?? throw new DomainRuleException("SUBSCRIPTION_NONE", "This organisation has no live subscription to change.");
        Plan current = await dbContext.Plans.AsNoTracking().FirstAsync(row => row.Id == subscription.PlanId, cancellationToken);
        Plan plan = await dbContext.Plans.AsNoTracking().FirstOrDefaultAsync(row => row.Code == body.PlanCode, cancellationToken)
            ?? throw new NotFoundException(nameof(Plan), body.PlanCode);

        int employees = (await dbContext.TenantUsages.AsNoTracking().FirstOrDefaultAsync(row => row.TenantId == id, cancellationToken))?.Employees ?? 0;
        int? seats = body.SeatLimit ?? plan.MaxEmployees;
        if (plan.MaxEmployees is { } max && (seats is null || seats > max))
        {
            throw new DomainRuleException("SEAT_LIMIT_INVALID", $"{plan.Name} allows up to {max:N0} employees.");
        }

        if (seats is { } limit && limit < employees)
        {
            throw new DomainRuleException("SEAT_LIMIT_BELOW_HEADCOUNT", $"They have {employees:N0} employees; the limit cannot be lower than that.");
        }

        var changes = new List<string>();
        if (plan.Id != current.Id || seats != subscription.SeatLimit)
        {
            subscription.ChangePlan(plan.Id, seats);
            changes.Add(plan.Id != current.Id ? $"plan {current.Name} → {plan.Name}" : $"employee limit {Seats(seats)}");
        }

        if (subscription.Status == SubscriptionStatus.Trial)
        {
            if (body.Paid)
            {
                subscription.ConvertTrialToPaid(body.BillingReference);
                changes.Add("trial converted to paid");
            }
            else if (body.TrialEndsOn is { } trialEnd && trialEnd != subscription.TrialEndsOn)
            {
                subscription.ExtendTrial(trialEnd, Today);
                changes.Add($"trial now ends {trialEnd.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}");
            }
        }

        if (body.RenewsOn != subscription.RenewsOn || (body.BillingReference ?? string.Empty) != (subscription.BillingReference ?? string.Empty))
        {
            subscription.SetTerms(body.RenewsOn, body.BillingReference);
            changes.Add("renewal date and billing reference");
        }

        if (changes.Count == 0)
        {
            return await DetailAsync(id, cancellationToken);
        }

        audit.Record("subscription.changed", $"Subscription: {string.Join("; ", changes)}", tenant.Id, tenant.Code);
        await SaveEntitlementsAsync(tenant.Id, subscription, plan, cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>Makes exactly the given modules available: a module outside the plan becomes an add-on, a plan module left out is switched off.</summary>
    public async Task<OrganisationDetail> UpdateModulesAsync(Guid id, UpdateModulesBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Tenant tenant = await TenantAsync(id, cancellationToken);
        Subscription subscription = await LiveSubscriptionAsync(id, tracked: true, cancellationToken)
            ?? throw new DomainRuleException("SUBSCRIPTION_NONE", "This organisation has no live subscription to change.");
        Plan plan = await dbContext.Plans.AsNoTracking().FirstAsync(row => row.Id == subscription.PlanId, cancellationToken);

        var wanted = new HashSet<string>(body.Enabled.Where(code => Features.All.Contains(code, StringComparer.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
        IReadOnlySet<string> before = subscription.ResolveFeatures(plan, Today);
        foreach (string code in Features.All)
        {
            bool inPlan = plan.FeatureCodes.Contains(code, StringComparer.OrdinalIgnoreCase);
            bool on = wanted.Contains(code);
            if (on == inPlan)
            {
                subscription.ClearFeatureOverride(code);
            }
            else
            {
                subscription.SetFeatureOverride(code, on, null, on ? "Add-on set in the platform console." : "Switched off in the platform console.");
            }
        }

        IReadOnlySet<string> after = subscription.ResolveFeatures(plan, Today);
        string[] added = [.. after.Except(before, StringComparer.OrdinalIgnoreCase).Order()];
        string[] removed = [.. before.Except(after, StringComparer.OrdinalIgnoreCase).Order()];
        if (added.Length == 0 && removed.Length == 0)
        {
            return await DetailAsync(id, cancellationToken);
        }

        var parts = new List<string>();
        if (added.Length > 0)
        {
            parts.Add($"switched on {string.Join(", ", added)}");
        }

        if (removed.Length > 0)
        {
            parts.Add($"switched off {string.Join(", ", removed)}");
        }

        audit.Record("modules.changed", $"Modules: {string.Join("; ", parts)}", tenant.Id, tenant.Code);
        await SaveEntitlementsAsync(tenant.Id, subscription, plan, cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    // ----- Dashboard -----------------------------------------------------------------------------

    public async Task<DashboardView> DashboardAsync(CancellationToken cancellationToken)
    {
        List<Tenant> tenants = await dbContext.Tenants.AsNoTracking().ToListAsync(cancellationToken);
        Dictionary<Guid, Tenant> byId = tenants.ToDictionary(tenant => tenant.Id);
        List<Subscription> live = await dbContext.Subscriptions.AsNoTracking().Where(row => Live.Contains(row.Status)).ToListAsync(cancellationToken);
        Dictionary<Guid, Plan> plans = await dbContext.Plans.AsNoTracking().ToDictionaryAsync(plan => plan.Id, cancellationToken);
        List<TenantUsage> usage = await dbContext.TenantUsages.AsNoTracking().ToListAsync(cancellationToken);
        Dictionary<Guid, TenantUsage> usageById = usage.ToDictionary(row => row.TenantId);

        bool Counted(Guid tenantId) => byId.GetValueOrDefault(tenantId)?.Status is TenantStatus.Active or TenantStatus.Suspended or TenantStatus.Provisioning or TenantStatus.Offboarding;

        DateOnly today = Today;
        DateOnly horizon = today.AddDays(60);
        var upcoming = new List<UpcomingItem>();
        foreach (Subscription subscription in live.Where(row => Counted(row.TenantId)))
        {
            Tenant tenant = byId[subscription.TenantId];
            string? planName = plans.GetValueOrDefault(subscription.PlanId)?.Name;
            if (subscription.TrialEndsOn is { } trialEnd && trialEnd >= today && trialEnd <= horizon)
            {
                upcoming.Add(new UpcomingItem(tenant.Id, tenant.Name, "Trial ends", trialEnd, planName));
            }

            if (subscription.RenewsOn is { } renews && renews >= today && renews <= horizon)
            {
                upcoming.Add(new UpcomingItem(tenant.Id, tenant.Name, "Renews", renews, planName));
            }
        }

        long failedJobs = SafeFailedJobs();
        var attention = new List<AttentionItem>();
        foreach (UpcomingItem item in upcoming.Where(row => row.What == "Trial ends" && row.When <= today.AddDays(14)))
        {
            attention.Add(new AttentionItem("Trial", item.TenantId, $"{item.Name}'s trial ends {Days(item.When, today)} ({item.When.ToString("d MMM", CultureInfo.InvariantCulture)})."));
        }

        foreach (Tenant tenant in tenants.Where(row => row.Status == TenantStatus.Suspended))
        {
            attention.Add(new AttentionItem("Suspended", tenant.Id, $"{tenant.Name} is suspended{(tenant.SuspensionReason is { } why ? $": {why}" : string.Empty)}."));
        }

        foreach (Tenant tenant in tenants.Where(row => row.Status == TenantStatus.Offboarding && row.PurgeEligibleDate is not null))
        {
            attention.Add(new AttentionItem("Offboarding", tenant.Id, $"{tenant.Name}'s data may be deleted from {tenant.PurgeEligibleDate!.Value.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}."));
        }

        foreach (Tenant tenant in tenants.Where(row => row.Status == TenantStatus.Provisioning))
        {
            attention.Add(new AttentionItem("Setting up", tenant.Id, $"{tenant.Name} is still being set up and cannot sign in yet."));
        }

        foreach (TenantUsage row in usage.Where(row => row.EmailsFailedToday > 0 && byId.ContainsKey(row.TenantId)))
        {
            attention.Add(new AttentionItem("Email", row.TenantId, $"{byId[row.TenantId].Name}: {row.EmailsFailedToday} email{(row.EmailsFailedToday == 1 ? string.Empty : "s")} failed today."));
        }

        if (failedJobs > 0)
        {
            attention.Add(new AttentionItem("System", null, $"{failedJobs} background job{(failedJobs == 1 ? string.Empty : "s")} failed and need a look."));
        }

        DateTime monthStart = new(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        IReadOnlyList<MonthCount> perMonth = [.. Enumerable.Range(0, 6).Reverse().Select(back =>
        {
            DateTime from = monthStart.AddMonths(-back);
            DateTime to = from.AddMonths(1);
            return new MonthCount(from.ToString("MMM yyyy", CultureInfo.InvariantCulture), tenants.Count(tenant => tenant.CreatedDate >= from && tenant.CreatedDate < to));
        })];

        List<TenantUsage> counted = [.. usage.Where(row => Counted(row.TenantId))];
        return new DashboardView(
            [.. tenants.GroupBy(tenant => tenant.Status).OrderBy(group => group.Key).Select(group => new CountByKey(group.Key.ToString(), group.Count()))],
            [.. live.Where(row => Counted(row.TenantId)).GroupBy(row => plans.GetValueOrDefault(row.PlanId)?.Name ?? "Unknown").Select(group => new CountByKey(group.Key, group.Count()))],
            counted.Sum(row => row.Employees),
            counted.Sum(row => row.SignedInToday),
            counted.Sum(row => row.EmailsSentToday),
            counted.Sum(row => row.EmailsFailedToday),
            perMonth,
            [.. upcoming.OrderBy(item => item.When)],
            attention,
            await ActivityAsync(null, 8, cancellationToken),
            (int)failedJobs);
    }

    // ----- Health --------------------------------------------------------------------------------

    public async Task<HealthView> HealthAsync(CancellationToken cancellationToken)
    {
        bool database = await dbContext.Database.CanConnectAsync(cancellationToken);
        IReadOnlyList<JobServerInfo> servers = jobs.Servers();
        JobCounts counts = jobs.Counts();
        DateTime now = Now;
        JobServerInfo? freshest = servers.OrderByDescending(server => server.LastHeartbeat).FirstOrDefault();
        bool workerAlive = freshest?.LastHeartbeat is { } beat && now - beat < TimeSpan.FromMinutes(2);

        Dictionary<Guid, Tenant> tenants = await dbContext.Tenants.AsNoTracking().ToDictionaryAsync(tenant => tenant.Id, cancellationToken);
        List<TenantUsage> usage = await dbContext.TenantUsages.AsNoTracking().ToListAsync(cancellationToken);
        int failedEmails = usage.Sum(row => row.EmailsFailedToday);

        IReadOnlyList<ServiceStatus> services =
        [
            new("Database", database ? "Running" : "Not reachable", database, database ? "The console can read and write." : "The console cannot reach the database."),
            new("Background worker", workerAlive ? (counts.Failed > 0 ? $"{counts.Failed} failed job{(counts.Failed == 1 ? string.Empty : "s")}" : "Running") : "Not running",
                workerAlive && counts.Failed == 0,
                freshest?.LastHeartbeat is { } heartbeat ? $"Last heartbeat {Ago(now - heartbeat)} ago" : "No worker has reported in"),
            new("Job queue", $"{counts.Enqueued} waiting", counts.Enqueued < 100, $"{counts.Processing} running, {counts.Scheduled} scheduled"),
            new("Email delivery", failedEmails > 0 ? $"{failedEmails} failed today" : "No failures today", failedEmails == 0, "Across every organisation, from the last measurement"),
        ];

        return new HealthView(
            services,
            [.. jobs.FailedJobs().Select(job =>
            {
                Guid? tenantId = Guid.TryParse(job.TenantId, out Guid parsed) ? parsed : null;
                return new FailedJobView(job.Id, job.Job, job.FailedAt, job.Reason, tenantId, tenantId is { } key ? tenants.GetValueOrDefault(key)?.Name : null);
            })],
            [.. jobs.RecurringJobs().Select(job => new RecurringJobView(job.Id, job.Cron, job.LastExecution, job.NextExecution, job.LastState, job.Error))],
            [.. usage.Where(row => tenants.ContainsKey(row.TenantId)).OrderBy(row => tenants[row.TenantId].Name)
                .Select(row => new EmailHealthRow(row.TenantId, tenants[row.TenantId].Name, row.EmailChannel, row.EmailsSentToday, row.EmailsFailedToday, row.EmailsLoggedToday))]);
    }

    public void RetryJob(string jobId)
    {
        if (!jobs.Retry(jobId))
        {
            throw new DomainRuleException("JOB_NOT_FAILED", "That job is no longer waiting to be tried again.");
        }

        audit.Record("job.retried", $"Background job {jobId} queued again");
    }

    public Task SaveAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    // ----- Helpers -------------------------------------------------------------------------------

    private async Task ActAsync(Guid id, string action, string summary, string? reason, Func<Task> change, CancellationToken cancellationToken)
    {
        Tenant tenant = await TenantAsync(id, cancellationToken);
        await change();
        audit.Record(action, summary, tenant.Id, tenant.Code, reason);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SaveEntitlementsAsync(Guid tenantId, Subscription subscription, Plan plan, CancellationToken cancellationToken)
    {
        publisher.PublishForTenant(tenantId, new TenantEntitlementsChangedIntegrationEvent(tenantId, [.. subscription.ResolveFeatures(plan, Today)]));
        await dbContext.SaveChangesAsync(cancellationToken);

        // The registry caches each tenant's features; without this their menus change only when it expires.
        await registry.InvalidateAsync(tenantId, cancellationToken);
    }

    private async Task<Tenant> TenantAsync(Guid id, CancellationToken cancellationToken)
        => await dbContext.Tenants.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Tenant), id);

    private async Task<Subscription?> LiveSubscriptionAsync(Guid tenantId, bool tracked, CancellationToken cancellationToken)
    {
        IQueryable<Subscription> rows = tracked ? dbContext.Subscriptions : dbContext.Subscriptions.AsNoTracking();
        return await rows.FirstOrDefaultAsync(row => row.TenantId == tenantId && Live.Contains(row.Status), cancellationToken);
    }

    private async Task<List<Subscription>> LiveForActiveOrganisationsAsync(CancellationToken cancellationToken)
    {
        List<Guid> open = await dbContext.Tenants.AsNoTracking().Where(tenant => tenant.Status == TenantStatus.Active || tenant.Status == TenantStatus.Suspended).Select(tenant => tenant.Id).ToListAsync(cancellationToken);
        return await dbContext.Subscriptions.AsNoTracking().Where(row => Live.Contains(row.Status) && open.Contains(row.TenantId)).ToListAsync(cancellationToken);
    }

    private long SafeFailedJobs()
    {
        try
        {
            return jobs.Counts().Failed;
        }
        catch (InvalidOperationException)
        {
            // Hangfire's schema may not exist yet on a fresh install: the dashboard still loads.
            return 0;
        }
    }

    private static IReadOnlyList<ModuleState> Modules(Subscription subscription, Plan plan, DateOnly today)
    {
        IReadOnlySet<string> enabled = subscription.ResolveFeatures(plan, today);
        return [.. Features.All.Select(code =>
        {
            bool inPlan = plan.FeatureCodes.Contains(code, StringComparer.OrdinalIgnoreCase);
            bool on = enabled.Contains(code);
            return new ModuleState(code, inPlan, on, on != inPlan);
        })];
    }

    private static UsageView? Usage(TenantUsage? row) => row is null ? null : new UsageView(
        row.Employees, row.Users, row.SignedInToday, row.LastSignIn, row.EmailsSentToday, row.EmailsFailedToday, row.EmailsLoggedToday, row.EmailChannel, row.SignIn, row.MeasuredDate);

    private static AuditItem Item(PlatformAuditEntry row) => new(row.Id, row.OccurredDate, row.StaffName, row.TenantId, row.TenantCode, row.Action, row.Summary, row.Reason);

    private static string Seats(int? seats) => seats is { } value ? value.ToString("N0", CultureInfo.InvariantCulture) : "no limit";

    private static string Days(DateOnly when, DateOnly today) => (when.DayNumber - today.DayNumber) switch
    {
        0 => "today",
        1 => "tomorrow",
        int days => $"in {days} days",
    };

    private static string Ago(TimeSpan span) => span.TotalSeconds < 90 ? $"{(int)span.TotalSeconds} seconds" : span.TotalMinutes < 90 ? $"{(int)span.TotalMinutes} minutes" : $"{(int)span.TotalHours} hours";
}
