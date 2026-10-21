using Gggs.BuildingBlocks.Domain.Abstractions;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;

namespace Gggs.Modules.Separation.Domain;

public enum ShowCauseStatus
{
    /// <summary>Notice served, waiting for the employee's explanation.</summary>
    Issued = 1,

    Replied = 2,

    /// <summary>A domestic enquiry is looking into it.</summary>
    UnderEnquiry = 3,

    Closed = 4,
}

public enum ShowCauseOutcome
{
    None = 0,
    Warning = 1,
    Termination = 2,
    Dropped = 3,
}

/// <summary>
/// A show-cause notice and what follows it - the employee's reply, an optional domestic enquiry and
/// the decision. Natural justice comes first: nobody is terminated before they have replied or the
/// time to reply has passed.
/// </summary>
public sealed class ShowCause : TenantAggregateRoot
{
    private ShowCause()
    {
        Allegation = null!;
    }

    public Guid EmployeeId { get; private set; }

    public string Allegation { get; private set; }

    public DateOnly IssuedOn { get; private set; }

    public DateOnly ReplyDueOn { get; private set; }

    public Guid? IssuedByEmployeeId { get; private set; }

    public Guid? NoticeLetterId { get; private set; }

    public string? Reply { get; private set; }

    public DateOnly? RepliedOn { get; private set; }

    public Guid? EnquiryOfficerEmployeeId { get; private set; }

    public string? EnquiryFindings { get; private set; }

    public DateOnly? EnquiryCompletedOn { get; private set; }

    public ShowCauseStatus Status { get; private set; }

    public ShowCauseOutcome Outcome { get; private set; }

    public string? DecisionNote { get; private set; }

    public DateOnly? DecidedOn { get; private set; }

    /// <summary>The termination this led to, if it did.</summary>
    public Guid? SeparationCaseId { get; private set; }

    public bool IsOpen => Status != ShowCauseStatus.Closed;

    public static ShowCause Issue(Guid employeeId, string allegation, DateOnly today, int replyDays, Guid? issuedBy)
    {
        if (replyDays is < 1 or > 30)
        {
            throw new DomainRuleException("SHOW_CAUSE_REPLY_DAYS", "Give 1 to 30 days to reply.");
        }

        return new ShowCause
        {
            EmployeeId = Guard.NotEmpty(employeeId),
            Allegation = Guard.MaxLength(Guard.NotNullOrWhiteSpace(allegation).Trim(), 2000),
            IssuedOn = today,
            ReplyDueOn = today.AddDays(replyDays),
            IssuedByEmployeeId = issuedBy,
            Status = ShowCauseStatus.Issued,
        };
    }

    public void NoticeIssued(Guid letterId) => NoticeLetterId = letterId;

    public void RecordReply(string reply, DateOnly today)
    {
        if (Status is not (ShowCauseStatus.Issued or ShowCauseStatus.Replied))
        {
            throw new DomainRuleException("SHOW_CAUSE_STATE", "This notice no longer takes a reply.");
        }

        Reply = Guard.MaxLength(Guard.NotNullOrWhiteSpace(reply).Trim(), 4000);
        RepliedOn = today;
        Status = ShowCauseStatus.Replied;
    }

    public void StartEnquiry(Guid officerEmployeeId)
    {
        EnsureOpen();
        if (officerEmployeeId == EmployeeId)
        {
            throw new DomainRuleException("SHOW_CAUSE_OFFICER", "The employee cannot hold their own enquiry.");
        }

        EnquiryOfficerEmployeeId = Guard.NotEmpty(officerEmployeeId);
        Status = ShowCauseStatus.UnderEnquiry;
    }

    public void RecordFindings(string findings, DateOnly today)
    {
        if (Status != ShowCauseStatus.UnderEnquiry)
        {
            throw new DomainRuleException("SHOW_CAUSE_STATE", "Start an enquiry before recording its findings.");
        }

        EnquiryFindings = Guard.MaxLength(Guard.NotNullOrWhiteSpace(findings).Trim(), 4000);
        EnquiryCompletedOn = today;
    }

    /// <summary>Whether the employee has had their say: replied, or let the time to reply run out.</summary>
    public bool Heard(DateOnly today) => RepliedOn is not null || today > ReplyDueOn;

    public void Decide(ShowCauseOutcome outcome, string? note, DateOnly today)
    {
        EnsureOpen();
        if (outcome == ShowCauseOutcome.None || !Enum.IsDefined(outcome))
        {
            throw new DomainRuleException("SHOW_CAUSE_OUTCOME", "Choose warning, termination or drop.");
        }

        if (outcome == ShowCauseOutcome.Termination && !Heard(today))
        {
            throw new DomainRuleException("SHOW_CAUSE_NOT_HEARD", "Wait for the employee's reply, or for the time to reply to pass, before terminating.");
        }

        if (outcome == ShowCauseOutcome.Termination && Status == ShowCauseStatus.UnderEnquiry && EnquiryCompletedOn is null)
        {
            throw new DomainRuleException("SHOW_CAUSE_ENQUIRY_OPEN", "Record the enquiry's findings before terminating.");
        }

        Outcome = outcome;
        DecisionNote = string.IsNullOrWhiteSpace(note) ? null : Guard.MaxLength(note.Trim(), 2000);
        DecidedOn = today;
        Status = ShowCauseStatus.Closed;
    }

    public void LedTo(Guid separationCaseId) => SeparationCaseId = separationCaseId;

    private void EnsureOpen()
    {
        if (!IsOpen)
        {
            throw new DomainRuleException("SHOW_CAUSE_STATE", "This show-cause is already decided.");
        }
    }
}

public enum AbsenceAlertStatus
{
    Open = 1,

    /// <summary>The employee came back or explained; no separation.</summary>
    Returned = 2,

    /// <summary>Treated as absconding: a separation was opened.</summary>
    Separated = 3,

    /// <summary>Not a concern (approved leave entered late, a data error).</summary>
    Dismissed = 4,
}

/// <summary>
/// A run of unexplained absence long enough to suggest absconding, found from attendance. HR sends
/// up to three notices (first, second, final), each with a date to respond by, before treating the
/// employee as having abandoned the job.
/// </summary>
public sealed class AbsenceAlert : TenantAggregateRoot
{
    public const int MaximumNotices = 3;

    private AbsenceAlert()
    {
    }

    public Guid EmployeeId { get; private set; }

    public DateOnly FirstAbsentDay { get; private set; }

    public DateOnly LastWorkedDay { get; private set; }

    public int WorkingDaysAbsent { get; private set; }

    public DateOnly DetectedOn { get; private set; }

    public AbsenceAlertStatus Status { get; private set; }

    public int NoticesSent { get; private set; }

    public DateOnly? LastNoticeOn { get; private set; }

    public DateOnly? RespondBy { get; private set; }

    public Guid? LastLetterId { get; private set; }

    public string? Note { get; private set; }

    public Guid? SeparationCaseId { get; private set; }

    public bool IsOpen => Status == AbsenceAlertStatus.Open;

    public static AbsenceAlert Raise(Guid employeeId, DateOnly firstAbsentDay, DateOnly lastWorkedDay, int workingDaysAbsent, DateOnly today) => new()
    {
        EmployeeId = Guard.NotEmpty(employeeId),
        FirstAbsentDay = firstAbsentDay,
        LastWorkedDay = lastWorkedDay,
        WorkingDaysAbsent = workingDaysAbsent,
        DetectedOn = today,
        Status = AbsenceAlertStatus.Open,
    };

    /// <summary>The absence goes on: the count grows with it.</summary>
    public void Extend(int workingDaysAbsent)
    {
        if (IsOpen)
        {
            WorkingDaysAbsent = Math.Max(WorkingDaysAbsent, workingDaysAbsent);
        }
    }

    public static string NoticeTitle(int number) => number switch
    {
        1 => "First notice",
        2 => "Second notice",
        _ => "Final notice",
    };

    public void SendNotice(DateOnly today, int respondWithinDays, Guid letterId)
    {
        EnsureOpen();
        if (NoticesSent >= MaximumNotices)
        {
            throw new DomainRuleException("ABSENCE_NOTICES", "The final notice has already been sent.");
        }

        NoticesSent++;
        LastNoticeOn = today;
        RespondBy = today.AddDays(respondWithinDays);
        LastLetterId = letterId;
    }

    /// <summary>Whether the employee has had a fair chance to respond: a notice sent and its date passed.</summary>
    public bool NoticeLapsed(DateOnly today) => NoticesSent > 0 && RespondBy is { } by && today > by;

    public void Close(AbsenceAlertStatus status, string? note)
    {
        EnsureOpen();
        if (status is not (AbsenceAlertStatus.Returned or AbsenceAlertStatus.Dismissed))
        {
            throw new DomainRuleException("ABSENCE_STATE", "Close an alert as returned or dismissed.");
        }

        Status = status;
        Note = string.IsNullOrWhiteSpace(note) ? null : Guard.MaxLength(note.Trim(), 1000);
    }

    public void Separated(Guid separationCaseId, DateOnly today)
    {
        EnsureOpen();
        if (!NoticeLapsed(today))
        {
            throw new DomainRuleException("ABSENCE_NOT_LAPSED", "Send a notice and let its date pass before treating the absence as absconding.");
        }

        SeparationCaseId = separationCaseId;
        Status = AbsenceAlertStatus.Separated;
    }

    private void EnsureOpen()
    {
        if (!IsOpen)
        {
            throw new DomainRuleException("ABSENCE_STATE", "This absence alert is closed.");
        }
    }
}
