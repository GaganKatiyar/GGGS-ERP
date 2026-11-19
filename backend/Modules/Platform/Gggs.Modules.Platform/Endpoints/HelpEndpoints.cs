using System.Text.Json;
using System.Text.RegularExpressions;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.BuildingBlocks.Web;
using Gggs.Data;
using Gggs.Modules.Platform.Domain.Tenants;
using Gggs.Modules.Platform.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gggs.Modules.Platform.Endpoints;

/// <summary>
/// The Help &amp; Wiki's server side. The guides themselves ship with the web app; what lives here is
/// what an organisation adds or tells us:
/// <list type="bullet">
/// <item>"In our company" notes an administrator adds to a help page (stored as tenant settings,
/// module <c>help</c>, one key per page), shown to everyone in the organisation;</item>
/// <item>feedback from the help assistant and the help pages - questions it could not answer and
/// answers marked helpful or not - written to the log so we know which guides to write next.</item>
/// </list>
/// </summary>
public static partial class HelpEndpoints
{
    /// <summary>The tenant-setting module the notes are kept under.</summary>
    public const string NotesModule = "help";

    /// <summary>Longest note an administrator can add to one page.</summary>
    public const int MaxNoteLength = 1000;

    /// <summary>Longest question kept from the assistant.</summary>
    public const int MaxQuestionLength = 300;

    public static IEndpointRouteBuilder MapHelpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints.MapGroup("/api/v1/help").WithTags("Help");

        // Everyone in the organisation reads the notes: they explain the organisation's own rules
        // next to the guide. Only people who manage settings write them.
        group.MapGet("/notes", ListNotesAsync)
            .WithName("ListHelpNotes")
            .RequireAuthorization();

        group.MapPut("/notes/{topicId}", SaveNoteAsync)
            .WithName("SaveHelpNote")
            .RequirePermission(Permissions.Admin.SettingManage);

        // Anyone signed in can tell us a guide did or did not help.
        group.MapPost("/feedback", RecordFeedbackAsync)
            .WithName("RecordHelpFeedback")
            .RequireAuthorization();

        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<HelpNoteItem>>> ListNotesAsync(
        AppDbContext database,
        CancellationToken cancellationToken)
    {
        List<TenantSetting> settings = await database.TenantSettings
            .AsNoTracking()
            .Where(setting => setting.Module == NotesModule)
            .OrderBy(setting => setting.Key)
            .ToListAsync(cancellationToken);

        List<HelpNoteItem> notes = [.. settings
            .Select(setting => new HelpNoteItem(setting.Key, ReadText(setting.ValueJson)))
            .Where(note => note.Text.Length > 0)];

        return TypedResults.Ok<IReadOnlyList<HelpNoteItem>>(notes);
    }

    private static async Task<Ok<HelpNoteItem>> SaveNoteAsync(
        string topicId,
        SaveHelpNoteBody body,
        AppDbContext database,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        string key = topicId.Trim();
        if (!TopicIdPattern().IsMatch(key))
        {
            throw new DomainRuleException("HELP_TOPIC_INVALID", "That is not a help page.");
        }

        string text = (body.Text ?? string.Empty).Trim();
        if (text.Length > MaxNoteLength)
        {
            throw new DomainRuleException("HELP_NOTE_TOO_LONG", $"A note can be at most {MaxNoteLength} characters.");
        }

        string valueJson = JsonSerializer.Serialize(text);
        TenantSetting? setting = await database.TenantSettings
            .FirstOrDefaultAsync(candidate => candidate.Module == NotesModule && candidate.Key == key, cancellationToken);

        // An emptied note is kept as an empty value rather than deleted: the list leaves it out, and
        // the audit trail keeps who removed it.
        if (setting is null)
        {
            database.TenantSettings.Add(TenantSetting.Create(NotesModule, key, valueJson));
        }
        else
        {
            setting.SetValue(valueJson);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new HelpNoteItem(key, text));
    }

    private static NoContent RecordFeedbackAsync(
        HelpFeedbackBody body,
        ICurrentUser currentUser,
        ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(body);

        string? question = body.Question?.Trim();
        if (question is { Length: > MaxQuestionLength })
        {
            question = question[..MaxQuestionLength];
        }

        string? topic = body.TopicId is { } id && TopicIdPattern().IsMatch(id.Trim()) ? id.Trim() : null;

        // Structured, so the log can be filtered for unanswered questions and unhelpful guides.
        LogFeedback(
            loggers.CreateLogger("Gggs.Help"),
            body.Kind.ToString(),
            topic ?? "-",
            body.Helpful,
            currentUser.UserId,
            question ?? string.Empty);

        return TypedResults.NoContent();
    }

    private static string ReadText(string valueJson)
    {
        try
        {
            return JsonSerializer.Deserialize<string>(valueJson)?.Trim() ?? string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>Help page ids: lower-case words, dots and hyphens (<c>me.leave</c>, <c>getting-started</c>).</summary>
    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{0,99}$")]
    private static partial Regex TopicIdPattern();

    [LoggerMessage(EventId = 7301, Level = LogLevel.Information,
        Message = "Help feedback {Kind} on {TopicId}: helpful={Helpful} user={UserId} question={Question}")]
    private static partial void LogFeedback(ILogger logger, string kind, string topicId, bool? helpful, Guid? userId, string question);
}

/// <summary>An organisation's own note on one help page.</summary>
public sealed record HelpNoteItem(string TopicId, string Text);

public sealed record SaveHelpNoteBody(string? Text);

/// <summary>What the feedback is about.</summary>
public enum HelpFeedbackKind
{
    /// <summary>The assistant found no answer to a question.</summary>
    Unanswered = 1,

    /// <summary>Someone rated an assistant answer.</summary>
    AnswerRated = 2,

    /// <summary>Someone rated a help page.</summary>
    PageRated = 3,
}

public sealed record HelpFeedbackBody(HelpFeedbackKind Kind, string? TopicId, string? Question, bool? Helpful);
