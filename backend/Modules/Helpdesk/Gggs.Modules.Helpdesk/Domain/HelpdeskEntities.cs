using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Helpdesk.Domain;

/// <summary>
/// A kind of request (IT, HR, payroll, facilities, grievance) with its service targets and the
/// person who picks up new tickets. Confidential categories are seen only by helpdesk managers.
/// </summary>
public sealed class HelpdeskCategory : TenantAggregateRoot
{
    private HelpdeskCategory()
    {
        Code = null!;
        Name = null!;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Who new tickets go to; unassigned when empty.</summary>
    public Guid? OwnerEmployeeId { get; private set; }

    /// <summary>Hours to the first response at normal priority.</summary>
    public int ResponseHours { get; private set; }

    /// <summary>Hours to resolution at normal priority.</summary>
    public int ResolutionHours { get; private set; }

    public bool IsConfidential { get; private set; }

    public static HelpdeskCategory Create(string code, string name, string? description, Guid? ownerEmployeeId, int responseHours, int resolutionHours, bool confidential)
    {
        var category = new HelpdeskCategory();
        category.Update(code, name, description, ownerEmployeeId, responseHours, resolutionHours, confidential, active: true);
        return category;
    }

    public void Update(string code, string name, string? description, Guid? ownerEmployeeId, int responseHours, int resolutionHours, bool confidential, bool active)
    {
        if (responseHours is < 1 or > 720 || resolutionHours < responseHours || resolutionHours > 2160)
        {
            throw new DomainRuleException("HELPDESK_SLA", "Response runs 1 to 720 hours and resolution from the response target up to 2160 hours.");
        }

        Code = Guard.MaxLength(Guard.NotNullOrWhiteSpace(code).Trim().ToUpperInvariant(), 30);
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), 100);
        Description = string.IsNullOrWhiteSpace(description) ? null : Guard.MaxLength(description.Trim(), 500);
        OwnerEmployeeId = ownerEmployeeId;
        ResponseHours = responseHours;
        ResolutionHours = resolutionHours;
        IsConfidential = confidential;
        IsActive = active;
    }
}

public enum TicketPriority
{
    Low = 1,
    Medium = 2,
    High = 3,
    Urgent = 4,
}

public enum TicketStatus
{
    Open = 1,
    InProgress = 2,
    WaitingOnRequester = 3,
    Resolved = 4,
    Closed = 5,
    Cancelled = 6,
}

/// <summary>
/// A request to the helpdesk (spec: helpdesk): raised by an employee, routed to the category owner,
/// worked against response and resolution targets scaled by priority, resolved, rated and closed.
/// </summary>
public sealed class Ticket : TenantAggregateRoot
{
    /// <summary>Days a resolved ticket stays open for the requester to reopen before it closes itself.</summary>
    public const int AutoCloseDays = 7;

    private Ticket()
    {
        Number = null!;
        Subject = null!;
        Description = null!;
    }

    public string Number { get; private set; }

    public Guid RequesterEmployeeId { get; private set; }

    public Guid CategoryId { get; private set; }

    public string Subject { get; private set; }

    public string Description { get; private set; }

    public TicketPriority Priority { get; private set; }

    public TicketStatus Status { get; private set; }

    public Guid? AssigneeEmployeeId { get; private set; }

    public bool IsConfidential { get; private set; }

    public DateTime ResponseDueAt { get; private set; }

    public DateTime ResolutionDueAt { get; private set; }

    public DateTime? FirstRespondedAt { get; private set; }

    public DateTime? ResolvedAt { get; private set; }

    public DateTime? ClosedAt { get; private set; }

    public bool ResponseBreached { get; private set; }

    public bool ResolutionBreached { get; private set; }

    public string? Resolution { get; private set; }

    public int? SatisfactionRating { get; private set; }

    public string? SatisfactionComment { get; private set; }

    public int ReopenCount { get; private set; }

    public bool IsOpen => Status is TicketStatus.Open or TicketStatus.InProgress or TicketStatus.WaitingOnRequester;

    /// <summary>How priority scales the category's targets: urgent a quarter, low double.</summary>
    public static double SlaFactor(TicketPriority priority) => priority switch
    {
        TicketPriority.Urgent => 0.25,
        TicketPriority.High => 0.5,
        TicketPriority.Low => 2,
        _ => 1,
    };

    public static Ticket Raise(string number, Guid requesterEmployeeId, HelpdeskCategory category, string subject, string description, TicketPriority priority, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(category);
        if (!Enum.IsDefined(priority))
        {
            throw new DomainRuleException("TICKET_PRIORITY", "Choose a priority.");
        }

        var ticket = new Ticket
        {
            Number = number,
            RequesterEmployeeId = Guard.NotEmpty(requesterEmployeeId),
            CategoryId = category.Id,
            Subject = Guard.MaxLength(Guard.NotNullOrWhiteSpace(subject).Trim(), 200),
            Description = Guard.MaxLength(Guard.NotNullOrWhiteSpace(description).Trim(), 4000),
            Priority = priority,
            Status = TicketStatus.Open,
            AssigneeEmployeeId = category.OwnerEmployeeId,
            IsConfidential = category.IsConfidential,
        };
        ticket.SetTargets(category, now);
        return ticket;
    }

    /// <summary>Moves the ticket to another category, re-targeting it and re-routing it to the new owner.</summary>
    public void Recategorise(HelpdeskCategory category, TicketPriority priority, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(category);
        EnsureOpen();
        Priority = priority;
        if (category.Id != CategoryId)
        {
            CategoryId = category.Id;
            IsConfidential = category.IsConfidential;
            AssigneeEmployeeId ??= category.OwnerEmployeeId;
        }

        SetTargets(category, CreatedDate == default ? now : CreatedDate);
    }

    public void Assign(Guid? assigneeEmployeeId)
    {
        EnsureOpen();
        AssigneeEmployeeId = assigneeEmployeeId;
    }

    /// <summary>A reply from the helpdesk; the first one meets the response target.</summary>
    public void Responded(DateTime now)
    {
        FirstRespondedAt ??= now;
        if (Status == TicketStatus.Open)
        {
            Status = TicketStatus.InProgress;
        }
    }

    /// <summary>A reply from the requester puts a waiting ticket back in the queue.</summary>
    public void RequesterReplied()
    {
        if (Status == TicketStatus.WaitingOnRequester)
        {
            Status = TicketStatus.InProgress;
        }
    }

    public void MoveTo(TicketStatus status, string? resolution, DateTime now)
    {
        EnsureOpen();
        switch (status)
        {
            case TicketStatus.InProgress:
            case TicketStatus.WaitingOnRequester:
                FirstRespondedAt ??= now;
                Status = status;
                break;
            case TicketStatus.Resolved:
                if (string.IsNullOrWhiteSpace(resolution))
                {
                    throw new DomainRuleException("TICKET_RESOLUTION", "Say how the ticket was resolved.");
                }

                FirstRespondedAt ??= now;
                Resolution = Guard.MaxLength(resolution.Trim(), 2000);
                ResolvedAt = now;
                ResolutionBreached |= now > ResolutionDueAt;
                Status = TicketStatus.Resolved;
                break;
            default:
                throw new DomainRuleException("TICKET_STATUS", "A ticket moves to in progress, waiting on the requester or resolved.");
        }
    }

    /// <summary>The requester is not satisfied: the ticket reopens within the auto-close window.</summary>
    public void Reopen(string reason, DateTime now)
    {
        if (Status != TicketStatus.Resolved)
        {
            throw new DomainRuleException("TICKET_REOPEN", "Only a resolved ticket can be reopened.");
        }

        Guard.NotNullOrWhiteSpace(reason);
        Status = TicketStatus.InProgress;
        ResolvedAt = null;
        Resolution = null;
        ReopenCount++;
        if (ResolutionDueAt < now)
        {
            ResolutionDueAt = now.AddHours(24);
        }
    }

    /// <summary>Closes a resolved ticket, by the requester or automatically after the window.</summary>
    public void Close(DateTime now)
    {
        if (Status != TicketStatus.Resolved)
        {
            throw new DomainRuleException("TICKET_CLOSE", "Only a resolved ticket can be closed.");
        }

        Status = TicketStatus.Closed;
        ClosedAt = now;
    }

    public void Cancel(DateTime now)
    {
        EnsureOpen();
        Status = TicketStatus.Cancelled;
        ClosedAt = now;
    }

    public void Rate(int rating, string? comment)
    {
        if (Status is not (TicketStatus.Resolved or TicketStatus.Closed))
        {
            throw new DomainRuleException("TICKET_RATE", "Rate a ticket once it is resolved.");
        }

        if (rating is < 1 or > 5)
        {
            throw new DomainRuleException("TICKET_RATING", "Rate from 1 to 5.");
        }

        SatisfactionRating = rating;
        SatisfactionComment = string.IsNullOrWhiteSpace(comment) ? null : Guard.MaxLength(comment.Trim(), 500);
    }

    /// <summary>Marks targets missed as of now; returns which kinds were newly breached.</summary>
    public (bool Response, bool Resolution) CheckSla(DateTime now)
    {
        if (!IsOpen)
        {
            return (false, false);
        }

        bool response = !ResponseBreached && FirstRespondedAt is null && now > ResponseDueAt;
        bool resolution = !ResolutionBreached && Status != TicketStatus.WaitingOnRequester && now > ResolutionDueAt;
        ResponseBreached |= response;
        ResolutionBreached |= resolution;
        return (response, resolution);
    }

    private void SetTargets(HelpdeskCategory category, DateTime from)
    {
        double factor = SlaFactor(Priority);
        ResponseDueAt = from.AddHours(Math.Max(1, category.ResponseHours * factor));
        ResolutionDueAt = from.AddHours(Math.Max(1, category.ResolutionHours * factor));
    }

    private void EnsureOpen()
    {
        if (!IsOpen)
        {
            throw new DomainRuleException("TICKET_NOT_OPEN", "The ticket is no longer open.");
        }
    }
}

/// <summary>A message on a ticket; internal notes are seen only by the helpdesk.</summary>
public sealed class TicketComment : TenantAggregateRoot
{
    private TicketComment()
    {
        Body = null!;
    }

    public Guid TicketId { get; private set; }

    public Guid? AuthorEmployeeId { get; private set; }

    public string Body { get; private set; }

    public bool IsInternal { get; private set; }

    public static TicketComment Write(Guid ticketId, Guid? authorEmployeeId, string body, bool isInternal) => new()
    {
        TicketId = ticketId,
        AuthorEmployeeId = authorEmployeeId,
        Body = Guard.MaxLength(Guard.NotNullOrWhiteSpace(body).Trim(), 4000),
        IsInternal = isInternal,
    };
}

/// <summary>A self-help article; published articles are suggested before a ticket is raised.</summary>
public sealed class KnowledgeArticle : TenantAggregateRoot
{
    private KnowledgeArticle()
    {
        Title = null!;
        Body = null!;
    }

    public Guid? CategoryId { get; private set; }

    public string Title { get; private set; }

    public string Body { get; private set; }

    /// <summary>Comma-separated search keywords.</summary>
    public string? Tags { get; private set; }

    public bool IsPublished { get; private set; }

    public int Views { get; private set; }

    public int HelpfulVotes { get; private set; }

    public int UnhelpfulVotes { get; private set; }

    public static KnowledgeArticle Write(Guid? categoryId, string title, string body, string? tags, bool publish)
    {
        var article = new KnowledgeArticle();
        article.Update(categoryId, title, body, tags, publish);
        return article;
    }

    public void Update(Guid? categoryId, string title, string body, string? tags, bool publish)
    {
        CategoryId = categoryId;
        Title = Guard.MaxLength(Guard.NotNullOrWhiteSpace(title).Trim(), 200);
        Body = Guard.MaxLength(Guard.NotNullOrWhiteSpace(body).Trim(), 20000);
        Tags = string.IsNullOrWhiteSpace(tags) ? null : Guard.MaxLength(string.Join(", ", tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToLowerInvariant(), 300);
        IsPublished = publish;
    }

    public void Viewed() => Views++;

    public void Vote(bool helpful)
    {
        if (helpful)
        {
            HelpfulVotes++;
        }
        else
        {
            UnhelpfulVotes++;
        }
    }
}

internal static class HelpdeskDefaults
{
    public static readonly (string Code, string Name, string Description, int Response, int Resolution, bool Confidential)[] Categories =
    [
        ("IT", "IT support", "Laptops, accounts, access, software and network.", 4, 24, false),
        ("HR", "HR query", "Policies, letters, records and benefits.", 8, 48, false),
        ("PAYROLL", "Payroll and tax", "Salary, payslips, tax declarations and reimbursements.", 8, 72, false),
        ("ADMIN", "Facilities and admin", "Seating, ID cards, travel desk and office services.", 8, 48, false),
        ("GRIEVANCE", "Grievance", "Confidential concerns about conduct or the workplace; seen only by the grievance team.", 24, 240, true),
    ];

    public static readonly (string Category, string Title, string Body, string Tags)[] Articles =
    [
        ("IT", "Reset your password", "Use the 'Forgot password' link on the sign-in page. A reset link is emailed to your official address and is valid for 30 minutes. If you no longer receive email, raise an IT ticket.", "password, login, reset, account"),
        ("IT", "Connect to the VPN", "Install the VPN client from the software portal, sign in with your official email, and approve the prompt on your phone. Raise an IT ticket if the client shows 'access denied'.", "vpn, remote, network"),
        ("PAYROLL", "Where is my payslip?", "Payslips are published under My payslips once the month's payroll is closed, usually by the last working day. Older payslips stay available there.", "payslip, salary, payroll"),
        ("HR", "Request an employment letter", "Address-proof and employment letters are generated by HR. Raise an HR query with the letter type and who it is addressed to; it is usually issued within two working days.", "letter, address proof, employment certificate"),
        ("ADMIN", "Lost ID card", "Report a lost ID card at once through a Facilities ticket so it can be deactivated. A replacement is issued within three working days.", "id card, access card, lost"),
    ];
}
