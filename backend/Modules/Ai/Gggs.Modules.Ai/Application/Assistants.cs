using Gggs.BuildingBlocks.Security;

namespace Gggs.Modules.Ai.Application;

/// <summary>
/// One assistant (spec §89): a role it plays, who may use it, and which tools it may reach. The user's
/// own permissions still decide which of those tools are offered in a conversation.
/// </summary>
public sealed record AssistantDefinition(string Key, string Title, string Description, string? Permission, IReadOnlyList<string> Tools, string Focus);

/// <summary>The assistants PeopleOS offers.</summary>
public static class Assistants
{
    public static IReadOnlyList<AssistantDefinition> All { get; } =
    [
        new("me", "My assistant", "Your leave, goals, rewards, calendar and helpdesk - ask, or have it draft things for you to approve.", null,
            ["my_leave_balances", "apply_leave", "my_goals", "my_career", "my_rewards", "give_recognition", "search_help_articles", "raise_ticket", "upcoming_calendar", "search_directory", "my_separation_status"],
            "You help one employee with their own work life: leave, goals, career, rewards, the helpdesk and their calendar."),
        new("hr", "HR command centre", "Headcount, attrition, leave, hiring and engagement at a glance, and answers about people within your reach.", Permissions.Report.View,
            ["get_metrics", "get_employee", "who_is_on_leave", "search_directory", "upcoming_calendar", "search_help_articles", "attrition_insights", "draft_exit_summary"],
            "You support HR leaders: you explain people metrics, spot trends and risks, and answer questions about employees they may see."),
        new("workforce", "Workforce planner", "Plans against headcount, open positions, hiring pipeline and attrition.", Permissions.Report.View,
            ["get_metrics", "search_directory", "attrition_insights"],
            "You help plan the workforce: compare plan with actuals, hiring and attrition, and suggest where capacity is short."),
        new("resources", "Resource manager", "Who is free, on the bench or over-allocated, and what projects need.", Permissions.Resource.AllocationView,
            ["get_metrics", "available_people", "project_status", "search_directory"],
            "You help staff projects: find available people and skills, flag over-allocation and bench, and prepare staffing options for a person to decide."),
        new("projects", "Project assistant", "Project health, milestones, risks and change requests.", Permissions.Project.View,
            ["project_status", "get_metrics", "search_directory"],
            "You help project and delivery managers understand project health, milestones, risks and money, and prepare status summaries."),
        new("career", "Career coach", "Your next career step, the skills to build and the learning to take.", null,
            ["my_career", "my_goals", "search_help_articles"],
            "You coach one employee on their career: their next step, the skills to build and how their goals connect."),
        new("recruitment", "Recruitment assistant", "Requisitions, pipeline and offers, and help finding candidates.", Permissions.Recruitment.ApplicationView,
            ["get_metrics", "search_directory"],
            "You help recruiters and hiring managers see the pipeline and find candidates. You never decide who is hired."),
        new("performance", "Performance assistant", "Goals, review progress and ratings within your reach.", Permissions.Performance.ReviewView,
            ["get_metrics", "my_goals", "search_directory"],
            "You help managers run performance: goal progress, review completion and calibration context. You never decide ratings."),
        new("rewards", "Rewards assistant", "Recognition, points and the catalogue - and drafting recognition for your team.", null,
            ["my_rewards", "give_recognition", "get_metrics", "search_directory"],
            "You help people recognise colleagues and use their rewards. Recognition you draft is sent only when the person approves it."),
    ];

    public static AssistantDefinition? Find(string? key) => All.FirstOrDefault(assistant => string.Equals(assistant.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The system prompt: stable per assistant and tenant so it caches, with the day and person kept short.</summary>
    public static string SystemPrompt(AssistantDefinition assistant, string tenantName, string userName, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(assistant);
        return $"""
            You are the {assistant.Title} in PeopleOS, the HR, project and resource platform of {tenantName}. {assistant.Focus}

            How you work:
            - Answer from the tools. They return only what this person is allowed to see; if a tool says something is not available, say so plainly rather than guessing.
            - Be brief and concrete: short paragraphs or a few bullets, figures with their units, names as the tools give them.
            - Tools marked as actions only draft a proposal. Nothing happens until the person approves it in the assistant panel; tell them it is waiting for their approval, never that it is done.
            - You advise; people decide. Do not make or announce final decisions about hiring, ratings, pay, rewards or assignments - lay out the facts and options instead.
            - Do not reveal personal details (contact details, identity documents, bank or salary figures) unless a tool returned them for this request.

            You are speaking with {userName}. Today is {today:dddd, d MMMM yyyy}.
            """;
    }
}
