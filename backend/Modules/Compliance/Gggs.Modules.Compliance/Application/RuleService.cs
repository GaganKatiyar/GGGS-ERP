using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.Data;
using Gggs.Modules.Compliance.Contracts;
using Gggs.Modules.Compliance.Domain;
using Gggs.Modules.Compliance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Compliance.Application;

public sealed record RuleItem(
    Guid Id,
    string Code,
    string Name,
    StatutoryRuleType Type,
    string CountryCode,
    string? StateCode,
    int Version,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    RuleStatus Status,
    JsonElement Configuration,
    string? Eligibility,
    string? Notes,
    DateTime? PublishedDate,
    bool InForce);

/// <summary>One rule across its versions: the one in force today and any draft waiting to be published.</summary>
public sealed record RuleSummary(string Code, string Name, StatutoryRuleType Type, string CountryCode, string? StateCode, RuleItem? Current, RuleItem? Draft, RuleItem? Upcoming, int Versions);

public sealed record RuleBody(string Code, string Name, StatutoryRuleType Type, string? CountryCode, string? StateCode, DateOnly EffectiveFrom, JsonElement Configuration, string? Eligibility, string? Notes);

public sealed record DraftBody(string Name, DateOnly EffectiveFrom, JsonElement Configuration, string? Eligibility, string? Notes);

public sealed record NewVersionBody(DateOnly EffectiveFrom, JsonElement? Configuration, string? Notes);

public sealed record RetireBody(DateOnly LastDay);

/// <summary>
/// Statutory rules (spec §22): drafted, checked against their scheme, published as immutable
/// versions, and superseded by newer versions from a later date - never edited in place.
/// </summary>
internal sealed class RuleService(AppDbContext dbContext, ITenantClock clock, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<RuleSummary>> ListAsync(StatutoryRuleType? type, string? stateCode, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        IQueryable<StatutoryRule> rules = dbContext.StatutoryRules.AsNoTracking();
        if (type is { } wanted)
        {
            rules = rules.Where(rule => rule.Type == wanted);
        }

        if (!string.IsNullOrWhiteSpace(stateCode))
        {
            string state = stateCode.Trim().ToUpperInvariant();
            rules = rules.Where(rule => rule.StateCode == state);
        }

        List<StatutoryRule> all = await rules.ToListAsync(cancellationToken);
        return [.. all
            .GroupBy(rule => rule.Code)
            .Select(group =>
            {
                StatutoryRule? current = group.Where(rule => rule.InForceOn(today)).MaxBy(rule => rule.Version);
                StatutoryRule? draft = group.Where(rule => rule.Status == RuleStatus.Draft).MaxBy(rule => rule.Version);
                StatutoryRule? upcoming = group.Where(rule => rule.Status == RuleStatus.Active && rule.EffectiveFrom > today).MinBy(rule => rule.EffectiveFrom);
                StatutoryRule head = current ?? upcoming ?? draft ?? group.MaxBy(rule => rule.Version)!;
                return new RuleSummary(
                    group.Key, head.Name, head.Type, head.CountryCode, head.StateCode,
                    current is null ? null : Item(current, today), draft is null ? null : Item(draft, today), upcoming is null ? null : Item(upcoming, today), group.Count());
            })
            .OrderBy(summary => summary.Type)
            .ThenBy(summary => summary.StateCode)
            .ThenBy(summary => summary.Code)];
    }

    public async Task<IReadOnlyList<RuleItem>> VersionsAsync(string code, CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        string key = code.Trim().ToUpperInvariant();
        List<StatutoryRule> versions = await dbContext.StatutoryRules.AsNoTracking().Where(rule => rule.Code == key).OrderByDescending(rule => rule.Version).ToListAsync(cancellationToken);
        return versions.Count == 0 ? throw new NotFoundException(nameof(StatutoryRule), code) : [.. versions.Select(rule => Item(rule, today))];
    }

    public async Task<RuleItem> GetAsync(Guid id, CancellationToken cancellationToken)
        => Item(await FindAsync(id, tracked: false, cancellationToken), await clock.TodayAsync(cancellationToken));

    /// <summary>A brand-new rule, as a draft version 1.</summary>
    public async Task<RuleItem> CreateAsync(RuleBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        string code = body.Code.Trim().ToUpperInvariant();
        if (await dbContext.StatutoryRules.AnyAsync(rule => rule.Code == code, cancellationToken))
        {
            throw new DomainRuleException("RULE_CODE_TAKEN", $"A rule {code} exists; draft a new version of it instead.");
        }

        string configuration = StatutoryConfigValidator.Normalise(body.Type, body.Configuration.GetRawText());
        var rule = StatutoryRule.Draft(code, body.Name, body.Type, body.CountryCode ?? "IN", body.StateCode, 1, body.EffectiveFrom, configuration, body.Eligibility, body.Notes);
        dbContext.StatutoryRules.Add(rule);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Item(rule, await clock.TodayAsync(cancellationToken));
    }

    /// <summary>The next version of a rule as a draft, starting from a copy of <paramref name="id"/>.</summary>
    public async Task<RuleItem> DraftNextVersionAsync(Guid id, NewVersionBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        StatutoryRule source = await FindAsync(id, tracked: false, cancellationToken);
        if (await dbContext.StatutoryRules.AnyAsync(rule => rule.Code == source.Code && rule.Status == RuleStatus.Draft, cancellationToken))
        {
            throw new DomainRuleException("RULE_DRAFT_EXISTS", $"{source.Code} already has a draft; publish or discard it first.");
        }

        int version = await dbContext.StatutoryRules.Where(rule => rule.Code == source.Code).MaxAsync(rule => rule.Version, cancellationToken) + 1;
        string configuration = body.Configuration is { } config ? StatutoryConfigValidator.Normalise(source.Type, config.GetRawText()) : source.Configuration;
        var draft = StatutoryRule.Draft(source.Code, source.Name, source.Type, source.CountryCode, source.StateCode, version, body.EffectiveFrom, configuration, source.Eligibility, body.Notes);
        dbContext.StatutoryRules.Add(draft);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Item(draft, await clock.TodayAsync(cancellationToken));
    }

    public async Task<RuleItem> UpdateDraftAsync(Guid id, DraftBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        StatutoryRule rule = await FindAsync(id, tracked: true, cancellationToken);
        rule.Edit(body.Name, body.EffectiveFrom, StatutoryConfigValidator.Normalise(rule.Type, body.Configuration.GetRawText()), body.Eligibility, body.Notes);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Item(rule, await clock.TodayAsync(cancellationToken));
    }

    public async Task DiscardDraftAsync(Guid id, CancellationToken cancellationToken)
    {
        StatutoryRule rule = await FindAsync(id, tracked: true, cancellationToken);
        if (rule.Status != RuleStatus.Draft)
        {
            throw new DomainRuleException("RULE_PUBLISHED", "A published rule is history and cannot be deleted.");
        }

        dbContext.StatutoryRules.Remove(rule);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Publishes a draft. The version it follows ends the day before this one starts.</summary>
    public async Task<RuleItem> PublishAsync(Guid id, CancellationToken cancellationToken)
    {
        StatutoryRule rule = await FindAsync(id, tracked: true, cancellationToken);
        List<StatutoryRule> published = await dbContext.StatutoryRules
            .Where(other => other.Code == rule.Code && other.Id != rule.Id && other.Status != RuleStatus.Draft)
            .ToListAsync(cancellationToken);

        StatutoryRule? latest = published.MaxBy(other => other.EffectiveFrom);
        if (latest is not null)
        {
            if (rule.EffectiveFrom <= latest.EffectiveFrom)
            {
                throw new DomainRuleException(
                    "RULE_VERSION_DATE",
                    $"Version {latest.Version} starts {latest.EffectiveFrom:d MMM yyyy}; a new version must start after it so past payroll keeps its rule.");
            }

            if (latest.Status == RuleStatus.Active && (latest.EffectiveTo is null || latest.EffectiveTo >= rule.EffectiveFrom))
            {
                latest.SupersedeFrom(rule.EffectiveFrom);
            }
        }

        rule.Publish(timeProvider.GetUtcNow().UtcDateTime);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Item(rule, await clock.TodayAsync(cancellationToken));
    }

    public async Task<RuleItem> RetireAsync(Guid id, DateOnly lastDay, CancellationToken cancellationToken)
    {
        StatutoryRule rule = await FindAsync(id, tracked: true, cancellationToken);
        rule.Retire(lastDay);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Item(rule, await clock.TodayAsync(cancellationToken));
    }

    private async Task<StatutoryRule> FindAsync(Guid id, bool tracked, CancellationToken cancellationToken)
    {
        IQueryable<StatutoryRule> rules = tracked ? dbContext.StatutoryRules : dbContext.StatutoryRules.AsNoTracking();
        return await rules.FirstOrDefaultAsync(rule => rule.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(StatutoryRule), id);
    }

    internal static RuleItem Item(StatutoryRule rule, DateOnly today)
    {
        using JsonDocument document = JsonDocument.Parse(rule.Configuration);
        return new RuleItem(
            rule.Id, rule.Code, rule.Name, rule.Type, rule.CountryCode, rule.StateCode, rule.Version, rule.EffectiveFrom, rule.EffectiveTo, rule.Status,
            document.RootElement.Clone(), rule.Eligibility, rule.Notes, rule.PublishedDate, rule.InForceOn(today));
    }
}

/// <summary>The rule versions in force, for payroll.</summary>
internal sealed class StatutoryRuleBook(AppDbContext dbContext) : IStatutoryRuleBook
{
    public async Task<StatutoryRuleVersion?> ResolveAsync(StatutoryRuleType type, string? stateCode, DateOnly day, CancellationToken cancellationToken = default)
    {
        string? state = string.IsNullOrWhiteSpace(stateCode) ? null : stateCode.Trim().ToUpperInvariant();
        List<StatutoryRule> candidates = await dbContext.StatutoryRules.AsNoTracking()
            .Where(rule => rule.Type == type && rule.Status != RuleStatus.Draft && rule.EffectiveFrom <= day && (rule.EffectiveTo == null || rule.EffectiveTo >= day)
                && (rule.StateCode == null || rule.StateCode == state))
            .ToListAsync(cancellationToken);
        StatutoryRule? chosen = candidates.Where(rule => state is not null && rule.StateCode == state).MaxBy(rule => rule.Version)
            ?? candidates.Where(rule => rule.StateCode is null).MaxBy(rule => rule.Version);
        return chosen is null ? null : Version(chosen);
    }

    public async Task<IReadOnlyList<StatutoryRuleVersion>> InForceAsync(DateOnly day, CancellationToken cancellationToken = default)
        => [.. (await dbContext.StatutoryRules.AsNoTracking()
                .Where(rule => rule.Status != RuleStatus.Draft && rule.EffectiveFrom <= day && (rule.EffectiveTo == null || rule.EffectiveTo >= day))
                .ToListAsync(cancellationToken))
            .Select(Version)];

    private static StatutoryRuleVersion Version(StatutoryRule rule)
        => new(rule.Id, rule.Code, rule.Type, rule.CountryCode, rule.StateCode, rule.Version, rule.EffectiveFrom, rule.EffectiveTo, rule.Name, rule.Configuration);
}
