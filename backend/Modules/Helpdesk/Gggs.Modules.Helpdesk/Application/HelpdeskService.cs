using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Persistence;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Helpdesk.Domain;
using Gggs.Modules.Helpdesk.Infrastructure;
using Gggs.Modules.Notification.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Helpdesk.Application;

public sealed record CategoryItem(Guid Id, string Code, string Name, string? Description, Guid? OwnerEmployeeId, string? OwnerName, int ResponseHours, int ResolutionHours, bool IsConfidential, bool IsActive);

public sealed record CategoryBody(string Code, string Name, string? Description, Guid? OwnerEmployeeId, int ResponseHours, int ResolutionHours, bool IsConfidential, bool IsActive = true);

public sealed record TicketItem(
    Guid Id,
    string Number,
    Guid RequesterEmployeeId,
    string? RequesterName,
    Guid CategoryId,
    string CategoryName,
    string Subject,
    TicketPriority Priority,
    TicketStatus Status,
    Guid? AssigneeEmployeeId,
    string? AssigneeName,
    bool IsConfidential,
    DateTime CreatedDate,
    DateTime ResponseDueAt,
    DateTime ResolutionDueAt,
    DateTime? FirstRespondedAt,
    DateTime? ResolvedAt,
    bool ResponseBreached,
    bool ResolutionBreached,
    int? SatisfactionRating);

public sealed record CommentItem(Guid Id, Guid? AuthorEmployeeId, string? AuthorName, string Body, bool IsInternal, DateTime CreatedDate);

public sealed record TicketView(
    TicketItem Ticket,
    string Description,
    string? Resolution,
    string? SatisfactionComment,
    int ReopenCount,
    IReadOnlyList<CommentItem> Comments,
    bool IsAgent,
    bool IsRequester);

public sealed record TicketQuery
{
    public TicketStatus? Status { get; init; }

    public Guid? CategoryId { get; init; }

    public Guid? RequesterId { get; init; }

    /// <summary>Only tickets assigned to the caller.</summary>
    public bool? Mine { get; init; }

    public bool? Breached { get; init; }

    public bool? OpenOnly { get; init; }
}

public sealed record RaiseBody(Guid CategoryId, string Subject, string Description, TicketPriority Priority = TicketPriority.Medium);

public sealed record CommentBody(string Body, bool IsInternal = false);

public sealed record AssignBody(Guid? AssigneeEmployeeId);

public sealed record StatusBody(TicketStatus Status, string? Resolution);

public sealed record RecategoriseBody(Guid CategoryId, TicketPriority Priority);

public sealed record ReopenBody(string Reason);

public sealed record RateBody(int Rating, string? Comment);

public sealed record ArticleItem(Guid Id, Guid? CategoryId, string? CategoryName, string Title, string? Tags, bool IsPublished, int Views, int HelpfulVotes, int UnhelpfulVotes, DateTime? ModifiedDate);

public sealed record ArticleView(ArticleItem Article, string Body);

public sealed record ArticleBody(Guid? CategoryId, string Title, string Body, string? Tags, bool Publish);

public sealed record VoteBody(bool Helpful);

public sealed record HelpdeskDashboard(
    int Open,
    int Unassigned,
    int ResponseBreached,
    int ResolutionBreached,
    double? AverageResolutionHours,
    double? AverageSatisfaction,
    double ResponseSlaMetPercent,
    IReadOnlyDictionary<string, int> OpenByCategory,
    IReadOnlyDictionary<string, int> OpenByPriority);

/// <summary>
/// The employee helpdesk (spec: helpdesk): categories with service targets and owners, tickets
/// routed, worked and resolved against SLA, conversation with internal notes, satisfaction, and a
/// knowledge base suggested before a ticket is raised.
/// </summary>
internal sealed class HelpdeskService(
    AppDbContext dbContext,
    IEmployeeDirectory employees,
    INotifier notifier,
    IPermissionProvider permissions,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // ----- Categories ---------------------------------------------------------------------------

    public async Task<IReadOnlyList<CategoryItem>> CategoriesAsync(CancellationToken cancellationToken)
    {
        List<HelpdeskCategory> rows = await dbContext.HelpdeskCategories.AsNoTracking().OrderBy(row => row.Name).ToListAsync(cancellationToken);
        Dictionary<Guid, string> names = await NamesAsync(rows.Where(row => row.OwnerEmployeeId != null).Select(row => row.OwnerEmployeeId!.Value), cancellationToken);
        return [.. rows.Select(row => new CategoryItem(row.Id, row.Code, row.Name, row.Description, row.OwnerEmployeeId,
            row.OwnerEmployeeId is { } owner ? names.GetValueOrDefault(owner) : null, row.ResponseHours, row.ResolutionHours, row.IsConfidential, row.IsActive))];
    }

    public async Task<IReadOnlyList<CategoryItem>> SaveCategoryAsync(Guid? id, CategoryBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        string code = (body.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (await dbContext.HelpdeskCategories.AnyAsync(row => row.Code == code && row.Id != id, cancellationToken))
        {
            throw new DomainRuleException("HELPDESK_CATEGORY_CODE", $"A category already uses the code {code}.");
        }

        if (body.OwnerEmployeeId is { } owner && !await employees.IsActiveAsync(owner, cancellationToken))
        {
            throw new DomainRuleException("HELPDESK_OWNER", "The owner must be an active employee.");
        }

        if (id is { } existing)
        {
            HelpdeskCategory category = await dbContext.HelpdeskCategories.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(HelpdeskCategory), existing);
            category.Update(code, body.Name, body.Description, body.OwnerEmployeeId, body.ResponseHours, body.ResolutionHours, body.IsConfidential, body.IsActive);
        }
        else
        {
            dbContext.HelpdeskCategories.Add(HelpdeskCategory.Create(code, body.Name, body.Description, body.OwnerEmployeeId, body.ResponseHours, body.ResolutionHours, body.IsConfidential));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await CategoriesAsync(cancellationToken);
    }

    // ----- Tickets ------------------------------------------------------------------------------

    public async Task<IReadOnlyList<TicketItem>> TicketsAsync(TicketQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IQueryable<Ticket> rows = await AgentTicketsAsync(cancellationToken);
        if (query.Status is { } status)
        {
            rows = rows.Where(row => row.Status == status);
        }

        if (query.OpenOnly == true)
        {
            rows = rows.Where(row => row.Status == TicketStatus.Open || row.Status == TicketStatus.InProgress || row.Status == TicketStatus.WaitingOnRequester);
        }

        if (query.CategoryId is { } category)
        {
            rows = rows.Where(row => row.CategoryId == category);
        }

        if (query.RequesterId is { } requester)
        {
            rows = rows.Where(row => row.RequesterEmployeeId == requester);
        }

        if (query.Mine == true)
        {
            Guid? me = currentUser.EmployeeId;
            rows = rows.Where(row => row.AssigneeEmployeeId == me);
        }

        if (query.Breached == true)
        {
            rows = rows.Where(row => row.ResponseBreached || row.ResolutionBreached);
        }

        return await ItemsAsync(await rows.OrderByDescending(row => row.CreatedDate).Take(500).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<TicketItem>> MyTicketsAsync(CancellationToken cancellationToken)
    {
        Guid me = Me();
        return await ItemsAsync(await dbContext.Tickets.AsNoTracking().Where(row => row.RequesterEmployeeId == me).OrderByDescending(row => row.CreatedDate).Take(300).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<TicketView> TicketAsync(Guid id, CancellationToken cancellationToken)
        => await ViewAsync(await VisibleAsync(id, tracked: false, cancellationToken), cancellationToken);

    public async Task<TicketView> RaiseAsync(RaiseBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Guid me = Me();
        HelpdeskCategory category = await dbContext.HelpdeskCategories.AsNoTracking().FirstOrDefaultAsync(row => row.Id == body.CategoryId && row.IsActive, cancellationToken)
            ?? throw new DomainRuleException("TICKET_CATEGORY", "Choose an active category.");
        string number = await NextNumberAsync(cancellationToken);
        var ticket = Ticket.Raise(number, me, category, body.Subject, body.Description, body.Priority, Now);
        dbContext.Tickets.Add(ticket);
        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyAssignedAsync(ticket, cancellationToken);
        return await ViewAsync(ticket, cancellationToken);
    }

    public async Task<TicketView> CommentAsync(Guid id, CommentBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Ticket ticket = await VisibleAsync(id, tracked: true, cancellationToken);
        bool agent = await IsAgentAsync(ticket, cancellationToken);
        bool requester = ticket.RequesterEmployeeId == currentUser.EmployeeId;
        if (body.IsInternal && !agent)
        {
            throw new ForbiddenException("TICKET_INTERNAL", "Only the helpdesk can add internal notes.");
        }

        if (ticket.Status is TicketStatus.Closed or TicketStatus.Cancelled)
        {
            throw new DomainRuleException("TICKET_NOT_OPEN", "The ticket is closed; raise a new one.");
        }

        dbContext.TicketComments.Add(TicketComment.Write(ticket.Id, currentUser.EmployeeId, body.Body, body.IsInternal));
        if (agent && !requester && !body.IsInternal)
        {
            ticket.Responded(Now);
            await NotifyRequesterAsync(ticket, "updated with a reply", body.Body, cancellationToken);
        }
        else if (requester)
        {
            ticket.RequesterReplied();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(ticket, cancellationToken);
    }

    public async Task<TicketView> AssignAsync(Guid id, Guid? assigneeEmployeeId, CancellationToken cancellationToken)
    {
        Ticket ticket = await AgentTicketAsync(id, cancellationToken);
        if (assigneeEmployeeId is { } assignee && !await employees.IsActiveAsync(assignee, cancellationToken))
        {
            throw new DomainRuleException("TICKET_ASSIGNEE", "Assign the ticket to an active employee.");
        }

        ticket.Assign(assigneeEmployeeId);
        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyAssignedAsync(ticket, cancellationToken);
        return await ViewAsync(ticket, cancellationToken);
    }

    public async Task<TicketView> ChangeStatusAsync(Guid id, StatusBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Ticket ticket = await AgentTicketAsync(id, cancellationToken);
        ticket.MoveTo(body.Status, body.Resolution, Now);
        await dbContext.SaveChangesAsync(cancellationToken);
        string label = body.Status switch
        {
            TicketStatus.Resolved => "resolved",
            TicketStatus.WaitingOnRequester => "waiting on you",
            _ => "in progress",
        };
        await NotifyRequesterAsync(ticket, label, body.Status == TicketStatus.Resolved ? $"Resolution: {ticket.Resolution} You can reopen it within {Ticket.AutoCloseDays} days." : string.Empty, cancellationToken);
        return await ViewAsync(ticket, cancellationToken);
    }

    public async Task<TicketView> RecategoriseAsync(Guid id, RecategoriseBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Ticket ticket = await AgentTicketAsync(id, cancellationToken);
        HelpdeskCategory category = await dbContext.HelpdeskCategories.AsNoTracking().FirstOrDefaultAsync(row => row.Id == body.CategoryId, cancellationToken) ?? throw new NotFoundException(nameof(HelpdeskCategory), body.CategoryId);
        Guid? before = ticket.AssigneeEmployeeId;
        ticket.Recategorise(category, body.Priority, Now);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (ticket.AssigneeEmployeeId != before)
        {
            await NotifyAssignedAsync(ticket, cancellationToken);
        }

        return await ViewAsync(ticket, cancellationToken);
    }

    /// <summary>The requester's own actions on a ticket: reopen, close, cancel or rate.</summary>
    public async Task<TicketView> ReopenAsync(Guid id, string reason, CancellationToken cancellationToken)
    {
        Ticket ticket = await OwnAsync(id, cancellationToken);
        ticket.Reopen(reason, Now);
        dbContext.TicketComments.Add(TicketComment.Write(ticket.Id, ticket.RequesterEmployeeId, $"Reopened: {reason}", isInternal: false));
        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyAssignedAsync(ticket, cancellationToken);
        return await ViewAsync(ticket, cancellationToken);
    }

    public async Task<TicketView> CloseAsync(Guid id, CancellationToken cancellationToken)
    {
        Ticket ticket = await OwnAsync(id, cancellationToken);
        ticket.Close(Now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(ticket, cancellationToken);
    }

    public async Task<TicketView> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        Ticket ticket = await OwnAsync(id, cancellationToken);
        ticket.Cancel(Now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(ticket, cancellationToken);
    }

    public async Task<TicketView> RateAsync(Guid id, RateBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Ticket ticket = await OwnAsync(id, cancellationToken);
        ticket.Rate(body.Rating, body.Comment);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ViewAsync(ticket, cancellationToken);
    }

    /// <summary>Marks missed targets and tells the assignee (or category owner); closes tickets resolved over a week ago.</summary>
    public async Task<(int Breached, int Closed)> SweepAsync(CancellationToken cancellationToken)
    {
        DateTime now = Now;
        List<Ticket> open = await dbContext.Tickets
            .Where(row => (row.Status == TicketStatus.Open || row.Status == TicketStatus.InProgress || row.Status == TicketStatus.WaitingOnRequester)
                && ((!row.ResponseBreached && row.FirstRespondedAt == null && row.ResponseDueAt < now) || (!row.ResolutionBreached && row.ResolutionDueAt < now)))
            .ToListAsync(cancellationToken);
        Dictionary<Guid, Guid?> owners = await dbContext.HelpdeskCategories.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.OwnerEmployeeId, cancellationToken);
        List<(Ticket Ticket, string Kind)> breaches = [];
        foreach (Ticket ticket in open)
        {
            (bool response, bool resolution) = ticket.CheckSla(now);
            if (response)
            {
                breaches.Add((ticket, "response"));
            }

            if (resolution)
            {
                breaches.Add((ticket, "resolution"));
            }
        }

        DateTime cutoff = now.AddDays(-Ticket.AutoCloseDays);
        List<Ticket> stale = await dbContext.Tickets.Where(row => row.Status == TicketStatus.Resolved && row.ResolvedAt < cutoff).ToListAsync(cancellationToken);
        foreach (Ticket ticket in stale)
        {
            ticket.Close(now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        foreach ((Ticket ticket, string kind) in breaches)
        {
            Guid? to = ticket.AssigneeEmployeeId ?? owners.GetValueOrDefault(ticket.CategoryId);
            if (to is { } recipient)
            {
                await notifier.NotifyAsync(
                    new NotificationRequest(
                        NotificationTemplates.TicketSlaBreached,
                        [new NotificationRecipient(recipient)],
                        new Dictionary<string, string>
                        {
                            ["number"] = ticket.Number,
                            ["subject"] = ticket.Subject,
                            ["kind"] = kind,
                            ["dueAt"] = (kind == "response" ? ticket.ResponseDueAt : ticket.ResolutionDueAt).ToString("d MMM yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture),
                        },
                        $"/helpdesk/tickets/{ticket.Id}"),
                    cancellationToken);
            }
        }

        return (breaches.Count, stale.Count);
    }

    public async Task<HelpdeskDashboard> DashboardAsync(CancellationToken cancellationToken)
    {
        DateTime since = Now.AddDays(-90);
        List<Ticket> rows = await (await AgentTicketsAsync(cancellationToken)).Where(row => row.CreatedDate >= since || row.Status == TicketStatus.Open || row.Status == TicketStatus.InProgress || row.Status == TicketStatus.WaitingOnRequester).ToListAsync(cancellationToken);
        Dictionary<Guid, string> categories = await dbContext.HelpdeskCategories.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);
        List<Ticket> open = [.. rows.Where(row => row.IsOpen)];
        List<Ticket> resolved = [.. rows.Where(row => row.ResolvedAt != null)];
        List<Ticket> responded = [.. rows.Where(row => row.FirstRespondedAt != null || row.ResponseBreached)];
        List<int> ratings = [.. rows.Where(row => row.SatisfactionRating != null).Select(row => row.SatisfactionRating!.Value)];
        return new HelpdeskDashboard(
            open.Count,
            open.Count(row => row.AssigneeEmployeeId == null),
            open.Count(row => row.ResponseBreached),
            open.Count(row => row.ResolutionBreached),
            resolved.Count == 0 ? null : Math.Round(resolved.Average(row => (row.ResolvedAt!.Value - row.CreatedDate).TotalHours), 1),
            ratings.Count == 0 ? null : Math.Round(ratings.Average(), 2),
            responded.Count == 0 ? 100 : Math.Round(100.0 * responded.Count(row => !row.ResponseBreached) / responded.Count, 1),
            open.GroupBy(row => categories.GetValueOrDefault(row.CategoryId) ?? "Other").ToDictionary(group => group.Key, group => group.Count()),
            open.GroupBy(row => row.Priority.ToString()).ToDictionary(group => group.Key, group => group.Count()));
    }

    // ----- Knowledge base -----------------------------------------------------------------------

    public async Task<IReadOnlyList<ArticleItem>> ArticlesAsync(string? search, Guid? categoryId, CancellationToken cancellationToken)
    {
        bool manager = (await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Helpdesk.KnowledgeManage);
        IQueryable<KnowledgeArticle> rows = dbContext.KnowledgeArticles.AsNoTracking();
        if (!manager)
        {
            rows = rows.Where(row => row.IsPublished);
        }

        if (categoryId is { } category)
        {
            rows = rows.Where(row => row.CategoryId == category);
        }

        List<KnowledgeArticle> articles = await rows.ToListAsync(cancellationToken);
        string[] terms = (search ?? string.Empty).ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        IEnumerable<KnowledgeArticle> ranked = terms.Length == 0
            ? articles.OrderByDescending(row => row.Views)
            : articles
                .Select(row => (Article: row, Score: terms.Sum(term => Score(row, term))))
                .Where(row => row.Score > 0)
                .OrderByDescending(row => row.Score).ThenByDescending(row => row.Article.HelpfulVotes)
                .Select(row => row.Article);
        Dictionary<Guid, string> categories = await dbContext.HelpdeskCategories.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);
        return [.. ranked.Take(50).Select(row => Item(row, categories))];
    }

    public async Task<ArticleView> ArticleAsync(Guid id, CancellationToken cancellationToken)
    {
        bool manager = (await permissions.GetCurrentAsync(cancellationToken)).Has(Permissions.Helpdesk.KnowledgeManage);
        KnowledgeArticle article = await dbContext.KnowledgeArticles.FirstOrDefaultAsync(row => row.Id == id && (manager || row.IsPublished), cancellationToken) ?? throw new NotFoundException(nameof(KnowledgeArticle), id);
        article.Viewed();
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ArticleViewAsync(article, cancellationToken);
    }

    public async Task<ArticleView> SaveArticleAsync(Guid? id, ArticleBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.CategoryId is { } category && !await dbContext.HelpdeskCategories.AnyAsync(row => row.Id == category, cancellationToken))
        {
            throw new NotFoundException(nameof(HelpdeskCategory), category);
        }

        KnowledgeArticle article;
        if (id is { } existing)
        {
            article = await dbContext.KnowledgeArticles.FirstOrDefaultAsync(row => row.Id == existing, cancellationToken) ?? throw new NotFoundException(nameof(KnowledgeArticle), existing);
            article.Update(body.CategoryId, body.Title, body.Body, body.Tags, body.Publish);
        }
        else
        {
            article = KnowledgeArticle.Write(body.CategoryId, body.Title, body.Body, body.Tags, body.Publish);
            dbContext.KnowledgeArticles.Add(article);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await ArticleViewAsync(article, cancellationToken);
    }

    public async Task<ArticleView> VoteAsync(Guid id, bool helpful, CancellationToken cancellationToken)
    {
        KnowledgeArticle article = await dbContext.KnowledgeArticles.FirstOrDefaultAsync(row => row.Id == id && row.IsPublished, cancellationToken) ?? throw new NotFoundException(nameof(KnowledgeArticle), id);
        article.Vote(helpful);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ArticleViewAsync(article, cancellationToken);
    }

    // ----- Internals ----------------------------------------------------------------------------

    private static int Score(KnowledgeArticle article, string term)
        => (article.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ? 5 : 0)
            + (article.Tags?.Contains(term, StringComparison.OrdinalIgnoreCase) == true ? 3 : 0)
            + (article.Body.Contains(term, StringComparison.OrdinalIgnoreCase) ? 1 : 0);

    private static ArticleItem Item(KnowledgeArticle row, Dictionary<Guid, string> categories)
        => new(row.Id, row.CategoryId, row.CategoryId is { } category ? categories.GetValueOrDefault(category) : null, row.Title, row.Tags, row.IsPublished, row.Views, row.HelpfulVotes, row.UnhelpfulVotes, row.ModifiedDate ?? row.CreatedDate);

    private async Task<ArticleView> ArticleViewAsync(KnowledgeArticle article, CancellationToken cancellationToken)
    {
        Dictionary<Guid, string> categories = await dbContext.HelpdeskCategories.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);
        return new ArticleView(Item(article, categories), article.Body);
    }

    private Guid Me() => currentUser.EmployeeId ?? throw new DomainRuleException("HELPDESK_NO_EMPLOYEE", "Your account is not linked to an employee record.");

    /// <summary>
    /// Tickets the helpdesk sees: every ticket for helpdesk viewers, except confidential ones,
    /// which only helpdesk managers and the ticket's assignee see.
    /// </summary>
    private async Task<IQueryable<Ticket>> AgentTicketsAsync(CancellationToken cancellationToken)
    {
        PermissionSet granted = await permissions.GetCurrentAsync(cancellationToken);
        Guid? me = currentUser.EmployeeId;
        IQueryable<Ticket> rows = dbContext.Tickets.AsNoTracking();
        if (granted.Has(Permissions.Helpdesk.TicketManage))
        {
            return rows;
        }

        return granted.Has(Permissions.Helpdesk.TicketView)
            ? rows.Where(row => !row.IsConfidential || row.AssigneeEmployeeId == me)
            : rows.Where(row => row.AssigneeEmployeeId == me);
    }

    private async Task<bool> IsAgentAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        PermissionSet granted = await permissions.GetCurrentAsync(cancellationToken);
        return granted.Has(Permissions.Helpdesk.TicketManage)
            || ticket.AssigneeEmployeeId == currentUser.EmployeeId
            || (granted.Has(Permissions.Helpdesk.TicketView) && !ticket.IsConfidential);
    }

    private async Task<Ticket> VisibleAsync(Guid id, bool tracked, CancellationToken cancellationToken)
    {
        IQueryable<Ticket> source = tracked ? dbContext.Tickets : dbContext.Tickets.AsNoTracking();
        Ticket ticket = await source.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Ticket), id);
        return ticket.RequesterEmployeeId == currentUser.EmployeeId || await IsAgentAsync(ticket, cancellationToken) ? ticket : throw new NotFoundException(nameof(Ticket), id);
    }

    /// <summary>A ticket the caller works on: a helpdesk manager, a viewer on a non-confidential ticket, or its assignee.</summary>
    private async Task<Ticket> AgentTicketAsync(Guid id, CancellationToken cancellationToken)
    {
        Ticket ticket = await dbContext.Tickets.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Ticket), id);
        return await IsAgentAsync(ticket, cancellationToken) ? ticket : throw new NotFoundException(nameof(Ticket), id);
    }

    private async Task<Ticket> OwnAsync(Guid id, CancellationToken cancellationToken)
        => await dbContext.Tickets.FirstOrDefaultAsync(row => row.Id == id && row.RequesterEmployeeId == currentUser.EmployeeId, cancellationToken) ?? throw new NotFoundException(nameof(Ticket), id);

    private async Task<string> NextNumberAsync(CancellationToken cancellationToken)
    {
        const string prefix = "TKT-";
        List<string> taken = await dbContext.Tickets.IgnoreQueryFilters([QueryFilters.SoftDelete]).Select(row => row.Number).ToListAsync(cancellationToken);
        int next = taken.Select(number => int.TryParse(number.AsSpan(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0).DefaultIfEmpty(0).Max() + 1;
        return $"{prefix}{next:000000}";
    }

    private async Task NotifyAssignedAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        if (ticket.AssigneeEmployeeId is not { } assignee || assignee == currentUser.EmployeeId)
        {
            return;
        }

        await notifier.NotifyAsync(
            new NotificationRequest(
                NotificationTemplates.TicketAssigned,
                [new NotificationRecipient(assignee)],
                new Dictionary<string, string>
                {
                    ["number"] = ticket.Number,
                    ["subject"] = ticket.Subject,
                    ["priority"] = ticket.Priority.ToString(),
                    ["dueAt"] = ticket.ResponseDueAt.ToString("d MMM yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture),
                },
                $"/helpdesk/tickets/{ticket.Id}"),
            cancellationToken);
    }

    private Task NotifyRequesterAsync(Ticket ticket, string status, string note, CancellationToken cancellationToken)
        => ticket.RequesterEmployeeId == currentUser.EmployeeId
            ? Task.CompletedTask
            : notifier.NotifyAsync(
                new NotificationRequest(
                    NotificationTemplates.TicketUpdate,
                    [new NotificationRecipient(ticket.RequesterEmployeeId)],
                    new Dictionary<string, string> { ["number"] = ticket.Number, ["subject"] = ticket.Subject, ["status"] = status, ["note"] = note.Length > 300 ? note[..300] + "..." : note },
                    $"/me/tickets/{ticket.Id}"),
                cancellationToken);

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
        => (await employees.GetManyAsync(ids.Distinct(), cancellationToken)).ToDictionary(person => person.Id, person => person.DisplayName);

    private async Task<TicketView> ViewAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        bool agent = await IsAgentAsync(ticket, cancellationToken);
        List<TicketComment> comments = await dbContext.TicketComments.AsNoTracking()
            .Where(row => row.TicketId == ticket.Id && (agent || !row.IsInternal))
            .OrderBy(row => row.CreatedDate).ToListAsync(cancellationToken);
        Dictionary<Guid, string> names = await NamesAsync(comments.Where(row => row.AuthorEmployeeId != null).Select(row => row.AuthorEmployeeId!.Value), cancellationToken);
        return new TicketView(
            (await ItemsAsync([ticket], cancellationToken))[0],
            ticket.Description,
            ticket.Resolution,
            ticket.SatisfactionComment,
            ticket.ReopenCount,
            [.. comments.Select(row => new CommentItem(row.Id, row.AuthorEmployeeId, row.AuthorEmployeeId is { } author ? names.GetValueOrDefault(author) : null, row.Body, row.IsInternal, row.CreatedDate))],
            agent,
            ticket.RequesterEmployeeId == currentUser.EmployeeId);
    }

    private async Task<List<TicketItem>> ItemsAsync(IReadOnlyList<Ticket> tickets, CancellationToken cancellationToken)
    {
        Dictionary<Guid, string> categories = await dbContext.HelpdeskCategories.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);
        Dictionary<Guid, string> names = await NamesAsync(
            tickets.Select(row => row.RequesterEmployeeId).Concat(tickets.Where(row => row.AssigneeEmployeeId != null).Select(row => row.AssigneeEmployeeId!.Value)),
            cancellationToken);
        return [.. tickets.Select(row => new TicketItem(
            row.Id, row.Number, row.RequesterEmployeeId, names.GetValueOrDefault(row.RequesterEmployeeId), row.CategoryId, categories.GetValueOrDefault(row.CategoryId) ?? string.Empty,
            row.Subject, row.Priority, row.Status, row.AssigneeEmployeeId, row.AssigneeEmployeeId is { } assignee ? names.GetValueOrDefault(assignee) : null, row.IsConfidential,
            row.CreatedDate, row.ResponseDueAt, row.ResolutionDueAt, row.FirstRespondedAt, row.ResolvedAt, row.ResponseBreached, row.ResolutionBreached, row.SatisfactionRating))];
    }
}

/// <summary>Seeds a tenant's default helpdesk categories and starter knowledge articles.</summary>
public sealed class HelpdeskSeeder(AppDbContext dbContext)
{
    public async Task<int> SeedCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<string, Guid> codes = await dbContext.HelpdeskCategories.IgnoreQueryFilters([QueryFilters.SoftDelete]).ToDictionaryAsync(row => row.Code, row => row.Id, cancellationToken);
        int added = 0;
        foreach ((string code, string name, string description, int response, int resolution, bool confidential) in HelpdeskDefaults.Categories.Where(row => !codes.ContainsKey(row.Code)))
        {
            var category = HelpdeskCategory.Create(code, name, description, null, response, resolution, confidential);
            dbContext.HelpdeskCategories.Add(category);
            codes[code] = category.Id;
            added++;
        }

        if (!await dbContext.KnowledgeArticles.IgnoreQueryFilters([QueryFilters.SoftDelete]).AnyAsync(cancellationToken))
        {
            foreach ((string category, string title, string body, string tags) in HelpdeskDefaults.Articles)
            {
                dbContext.KnowledgeArticles.Add(KnowledgeArticle.Write(codes.GetValueOrDefault(category), title, body, tags, publish: true));
                added++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return added;
    }
}
