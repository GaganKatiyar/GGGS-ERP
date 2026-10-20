using System.Text.Json;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Ai.Contracts;
using Gggs.Modules.Helpdesk.Domain;
using Gggs.Modules.Helpdesk.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Helpdesk.Application;

/// <summary>Searches published help articles and returns the best matches with their text.</summary>
internal sealed class SearchHelpArticlesTool(HelpdeskService helpdesk) : IAiTool
{
    public string Name => "search_help_articles";

    public string Description => "Searches the company knowledge base (IT, HR, payroll, facilities how-tos) and returns the best matching articles with their text. Try this before suggesting a ticket.";

    public JsonElement InputSchema { get; } = AiJson.Schema(("query", "string", "Keywords, e.g. 'reset password' or 'payslip'", true));

    public AiToolKind Kind => AiToolKind.Read;

    public string? Permission => Permissions.Self.TicketCreate;

    public async Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ArticleItem> matches = await helpdesk.ArticlesAsync(AiJson.Text(input, "query"), null, cancellationToken);
        List<object> results = [];
        foreach (ArticleItem article in matches.Take(3))
        {
            ArticleView view = await helpdesk.ArticleAsync(article.Id, cancellationToken);
            results.Add(new { article.Title, article.CategoryName, Text = view.Body.Length > 3000 ? view.Body[..3000] : view.Body });
        }

        return results.Count == 0 ? AiToolOutput.Problem("No help article matches that.") : AiToolOutput.Data(results);
    }
}

/// <summary>Drafts a helpdesk ticket; it is raised only when the person approves it.</summary>
internal sealed class RaiseTicketTool(AppDbContext dbContext, HelpdeskService helpdesk) : IAiActionTool
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Name => "raise_ticket";

    public string Description => "Drafts a helpdesk ticket for this person: category code (IT, HR, PAYROLL, ADMIN or GRIEVANCE), subject, description and priority (Low, Medium, High, Urgent).";

    public JsonElement InputSchema { get; } = AiJson.Schema(
        ("categoryCode", "string", "IT, HR, PAYROLL, ADMIN or GRIEVANCE", true),
        ("subject", "string", "One-line subject", true),
        ("description", "string", "What happened and what is needed", true),
        ("priority", "string", "Low, Medium, High or Urgent (default Medium)", false));

    public AiToolKind Kind => AiToolKind.Action;

    public string? Permission => Permissions.Self.TicketCreate;

    public Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("raise_ticket is an action; it is proposed, not run.");

    public async Task<AiProposal> ProposeAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        string code = (AiJson.Text(input, "categoryCode") ?? string.Empty).Trim().ToUpperInvariant();
        HelpdeskCategory category = await dbContext.HelpdeskCategories.AsNoTracking().FirstOrDefaultAsync(row => row.Code == code && row.IsActive, cancellationToken)
            ?? throw new DomainRuleException("AI_TICKET_CATEGORY", $"There is no helpdesk category '{code}'.");
        TicketPriority priority = Enum.TryParse(AiJson.Text(input, "priority"), ignoreCase: true, out TicketPriority parsed) && Enum.IsDefined(parsed) ? parsed : TicketPriority.Medium;
        string subject = (AiJson.Text(input, "subject") ?? string.Empty).Trim();
        string description = (AiJson.Text(input, "description") ?? string.Empty).Trim();
        if (subject.Length is 0 or > 200 || description.Length is 0 or > 4000)
        {
            throw new DomainRuleException("AI_TICKET_TEXT", "Give a subject of up to 200 characters and a description.");
        }

        return new AiProposal($"Raise a {priority.ToString().ToLowerInvariant()}-priority {category.Name} ticket: \"{subject}\"", JsonSerializer.Serialize(new RaiseBody(category.Id, subject, description, priority), Json));
    }

    public async Task<string> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        RaiseBody body = JsonSerializer.Deserialize<RaiseBody>(payloadJson, Json) ?? throw new DomainRuleException("AI_PAYLOAD", "The proposal could not be read.");
        TicketView view = await helpdesk.RaiseAsync(body, cancellationToken);
        return $"Ticket {view.Ticket.Number} raised; respond-by {view.Ticket.ResponseDueAt:d MMM HH:mm} UTC.";
    }
}
