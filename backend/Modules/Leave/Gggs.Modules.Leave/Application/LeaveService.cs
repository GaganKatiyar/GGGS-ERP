using System.Globalization;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Files;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Leave.Contracts;
using Gggs.Modules.Leave.Domain;
using Gggs.Modules.Leave.Infrastructure;
using Gggs.Modules.MasterData.Contracts;
using Gggs.Modules.Workflow.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Leave.Application;

public sealed record LeaveApplication(
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    bool StartsSecondHalf,
    bool EndsFirstHalf,
    string? Reason,
    Guid? EmployeeId = null);

public sealed record LeavePreview(decimal Days, decimal Available, bool WithinBalance, IReadOnlyList<string> Problems, bool DocumentRequired = false);

/// <summary>A supporting document on a request.</summary>
public sealed record LeaveAttachmentItem(Guid Id, string FileName, string ContentType, long SizeBytes, DateTime UploadedDate);

public sealed record IncomingAttachment(string FileName, long Length, Func<Stream> Open);

public sealed record AttachmentContent(Stream Content, string ContentType, string FileName);

public sealed record LeaveRequestItem(
    Guid Id,
    Guid EmployeeId,
    string? EmployeeName,
    Guid LeaveTypeId,
    string LeaveTypeCode,
    string LeaveTypeName,
    string Color,
    DateOnly StartDate,
    DateOnly EndDate,
    bool StartsSecondHalf,
    bool EndsFirstHalf,
    decimal Days,
    string? Reason,
    LeaveRequestStatus Status,
    Guid? WorkflowInstanceId,
    DateTime AppliedDate,
    DateTime? DecidedDate,
    string? CancellationReason,
    int AttachmentCount = 0);

public sealed record LeaveBalance(
    Guid LeaveTypeId,
    string Code,
    string Name,
    string Color,
    bool IsPaid,
    decimal Balance,
    decimal Pending,
    decimal Available,
    decimal AnnualEntitlement,
    decimal TakenThisYear);

public sealed record LedgerItem(Guid Id, DateOnly EntryDate, LedgerKind Kind, decimal Days, string? Note, Guid? RequestId);

public sealed record AdjustmentRequest(Guid EmployeeId, Guid LeaveTypeId, decimal Days, string Note);

public sealed record LeaveRequestQuery
{
    public Guid? EmployeeId { get; init; }

    public LeaveRequestStatus? Status { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public PageRequest ToPageRequest() => new() { Page = Page ?? 1, PageSize = PageSize ?? PageRequest.DefaultPageSize };
}

/// <summary>
/// Leave requests and balances (spec §20). Every rule the employee's leave policy sets is checked
/// when applying (<see cref="LeaveEligibility"/>), then overlaps and the balance after other pending
/// requests. Approval goes through Workflow; the days leave the balance when it is approved.
/// </summary>
internal sealed class LeaveService(
    AppDbContext dbContext,
    LeaveCalendarService calendar,
    IEmployeeDirectory employees,
    IWorkflowStarter workflow,
    IIntegrationEventPublisher publisher,
    ICurrentUser currentUser,
    ITenantClock clock,
    TimeProvider timeProvider,
    LeavePlanService plans,
    IMasterDataLookup lookup,
    IFileStorage fileStorage,
    IEnumerable<ILeaveApplicationRule> applicationRules)
{
    public const string SubjectType = "leave.request";

    public static WorkflowSubject Subject { get; } = new(
        SubjectType,
        "Leave request",
        "leave",
        [
            new WorkflowSubjectField("days", "Days", WorkflowFieldType.Number),
            new WorkflowSubjectField("leaveType", "Leave type code", WorkflowFieldType.Text),
            new WorkflowSubjectField("isPaid", "Paid (true/false)", WorkflowFieldType.Text),
        ]);

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // ----- Applying ----------------------------------------------------------------------------

    public async Task<LeavePreview> PreviewAsync(LeaveApplication application, bool onBehalf, CancellationToken cancellationToken)
    {
        Guid employeeId = await ResolveEmployeeAsync(application, onBehalf, cancellationToken);
        Evaluation evaluation = await EvaluateAsync(employeeId, application, onBehalf, excludeRequestId: null, cancellationToken);
        return new LeavePreview(evaluation.Days, evaluation.Available, evaluation.Days <= evaluation.Available, evaluation.Problems, evaluation.DocumentRequired);
    }

    public async Task<LeaveRequestItem> ApplyAsync(LeaveApplication application, bool onBehalf, CancellationToken cancellationToken)
    {
        Guid employeeId = await ResolveEmployeeAsync(application, onBehalf, cancellationToken);
        Evaluation evaluation = await EvaluateAsync(employeeId, application, onBehalf, excludeRequestId: null, cancellationToken);
        ThrowIfProblems(evaluation.Problems);

        LeaveType type = await dbContext.LeaveTypes.AsNoTracking().FirstAsync(row => row.Id == application.LeaveTypeId, cancellationToken);
        var request = LeaveRequest.Apply(
            employeeId, type.Id, application.StartDate, application.EndDate, application.StartsSecondHalf, application.EndsFirstHalf,
            evaluation.Days, application.Reason, onBehalf ? currentUser.UserId : null, awaitingDocument: evaluation.DocumentRequired, grantsItsDays: evaluation.GrantsItsDays);
        dbContext.LeaveRequests.Add(request);

        // A long request waits as a draft for its supporting document; Submit sends it for approval.
        if (!evaluation.DocumentRequired)
        {
            await StartApprovalAsync(request, type, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ToItemsAsync([request], cancellationToken))[0];
    }

    /// <summary>The draft has its supporting document: the rules are checked again and it goes for approval.</summary>
    public async Task<LeaveRequestItem> SubmitAsync(Guid requestId, CancellationToken cancellationToken)
    {
        LeaveRequest request = await OwnDraftAsync(requestId, cancellationToken);
        if (!await dbContext.LeaveRequestAttachments.AnyAsync(attachment => attachment.RequestId == requestId, cancellationToken))
        {
            throw new DomainRuleException("LEAVE_DOCUMENT_REQUIRED", "Attach the supporting document before sending the request.");
        }

        var application = new LeaveApplication(request.LeaveTypeId, request.StartDate, request.EndDate, request.StartsSecondHalf, request.EndsFirstHalf, request.Reason, request.EmployeeId);
        Evaluation evaluation = await EvaluateAsync(request.EmployeeId, application, request.AppliedByUserId is not null, excludeRequestId: request.Id, cancellationToken);
        ThrowIfProblems(evaluation.Problems);

        LeaveType type = await dbContext.LeaveTypes.AsNoTracking().FirstAsync(row => row.Id == request.LeaveTypeId, cancellationToken);
        request.Submit(evaluation.Days);
        await StartApprovalAsync(request, type, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (await ToItemsAsync([request], cancellationToken))[0];
    }

    // ----- Supporting documents ----------------------------------------------------------------

    /// <summary>A PDF or a photo of the document, up to 5 MB, judged by its first bytes.</summary>
    public const long MaxAttachmentBytes = 5 * 1024 * 1024;

    public async Task<LeaveAttachmentItem> AttachAsync(Guid requestId, IncomingAttachment file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        LeaveRequest request = await dbContext.LeaveRequests.AsNoTracking().FirstOrDefaultAsync(row => row.Id == requestId, cancellationToken)
            ?? throw new NotFoundException(nameof(LeaveRequest), requestId);
        if (!await CanWriteAsync(request, cancellationToken))
        {
            throw new NotFoundException(nameof(LeaveRequest), requestId);
        }

        if (request.Status is not (LeaveRequestStatus.Draft or LeaveRequestStatus.Pending))
        {
            throw new DomainRuleException("LEAVE_REQUEST_CLOSED", $"Documents cannot be added to a request that is {request.Status}.");
        }

        if (file.Length is <= 0 or > MaxAttachmentBytes)
        {
            throw new DomainRuleException("LEAVE_DOCUMENT_SIZE", "Attach a file of up to 5 MB.");
        }

        byte[] head = new byte[8];
        await using (Stream peek = file.Open())
        {
            int read = await peek.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken);
            head = head[..read];
        }

        string contentType = SniffDocument(head) ?? throw new DomainRuleException("LEAVE_DOCUMENT_TYPE", "Attach a PDF, JPEG or PNG file.");
        Guid attachmentId = Guid.CreateVersion7();
        string fileName = Path.GetFileName(file.FileName);
        StoredFile stored;
        await using (Stream content = file.Open())
        {
            stored = await fileStorage.SaveAsync(
                new FileToStore { Content = content, RelativeKey = $"leave/{requestId:N}/{attachmentId:N}", ContentType = contentType },
                cancellationToken);
        }

        var attachment = LeaveRequestAttachment.Create(attachmentId, requestId, string.IsNullOrWhiteSpace(fileName) ? "document" : fileName, contentType, stored.SizeBytes, stored.StorageKey);
        dbContext.LeaveRequestAttachments.Add(attachment);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await fileStorage.DeleteAsync(stored.StorageKey, CancellationToken.None);
            throw;
        }

        return new LeaveAttachmentItem(attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes, attachment.CreatedDate == default ? Now : attachment.CreatedDate);
    }

    public async Task<IReadOnlyList<LeaveAttachmentItem>> AttachmentsAsync(Guid requestId, CancellationToken cancellationToken)
    {
        await ReadableAsync(requestId, cancellationToken);
        return await dbContext.LeaveRequestAttachments.AsNoTracking()
            .Where(attachment => attachment.RequestId == requestId)
            .OrderBy(attachment => attachment.CreatedDate)
            .Select(attachment => new LeaveAttachmentItem(attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes, attachment.CreatedDate))
            .ToListAsync(cancellationToken);
    }

    public async Task<AttachmentContent> OpenAttachmentAsync(Guid requestId, Guid attachmentId, CancellationToken cancellationToken)
    {
        await ReadableAsync(requestId, cancellationToken);
        LeaveRequestAttachment attachment = await dbContext.LeaveRequestAttachments.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == attachmentId && row.RequestId == requestId, cancellationToken)
            ?? throw new NotFoundException(nameof(LeaveRequestAttachment), attachmentId);
        return new AttachmentContent(await fileStorage.OpenReadAsync(attachment.StorageKey, cancellationToken), attachment.ContentType, attachment.FileName);
    }

    /// <summary>The document type its first bytes declare: PDF, JPEG or PNG; anything else is refused.</summary>
    internal static string? SniffDocument(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith("%PDF"u8))
        {
            return "application/pdf";
        }

        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
        {
            return "image/jpeg";
        }

        return head.StartsWith((byte[])[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) ? "image/png" : null;
    }

    /// <summary>The employee themself, or whoever applied on their behalf.</summary>
    private async Task<bool> CanWriteAsync(LeaveRequest request, CancellationToken cancellationToken)
        => request.EmployeeId == currentUser.EmployeeId
            || (request.AppliedByUserId is not null && request.AppliedByUserId == currentUser.UserId)
            || await employees.IsInScopeAsync(request.EmployeeId, Permissions.Leave.RequestCreateOnBehalf, cancellationToken);

    /// <summary>The employee, HR and managers who can see the request, and its approvers through Workflow's inbox (they hold leave.request.view for their team).</summary>
    private async Task ReadableAsync(Guid requestId, CancellationToken cancellationToken)
    {
        LeaveRequest request = await dbContext.LeaveRequests.AsNoTracking().FirstOrDefaultAsync(row => row.Id == requestId, cancellationToken)
            ?? throw new NotFoundException(nameof(LeaveRequest), requestId);
        if (request.EmployeeId != currentUser.EmployeeId && !await employees.IsInScopeAsync(request.EmployeeId, Permissions.Leave.RequestView, cancellationToken) && !await CanWriteAsync(request, cancellationToken))
        {
            throw new NotFoundException(nameof(LeaveRequest), requestId);
        }
    }

    private async Task<LeaveRequest> OwnDraftAsync(Guid requestId, CancellationToken cancellationToken)
    {
        LeaveRequest request = await dbContext.LeaveRequests.FirstOrDefaultAsync(row => row.Id == requestId, cancellationToken)
            ?? throw new NotFoundException(nameof(LeaveRequest), requestId);
        if (!await CanWriteAsync(request, cancellationToken))
        {
            throw new NotFoundException(nameof(LeaveRequest), requestId);
        }

        return request.Status == LeaveRequestStatus.Draft
            ? request
            : throw new DomainRuleException("LEAVE_REQUEST_CLOSED", $"This request is already {request.Status}.");
    }

    private static void ThrowIfProblems(List<string> problems)
    {
        if (problems.Count > 0)
        {
            throw new DomainRuleException("LEAVE_NOT_ALLOWED", problems[0]) { Context = new Dictionary<string, object?> { ["problems"] = problems } };
        }
    }

    private async Task StartApprovalAsync(LeaveRequest request, LeaveType type, CancellationToken cancellationToken)
    {
        decimal days = request.Days;
        WorkflowStartResult started = await workflow.StartAsync(
            new WorkflowStartRequest
            {
                SubjectType = SubjectType,
                SubjectId = request.Id,
                Title = $"{type.Name}: {days.ToString("0.#", CultureInfo.InvariantCulture)} day(s) from {request.StartDate:d MMM}",
                RequesterEmployeeId = request.EmployeeId,
                Link = "/team/leave",
                Fields = new Dictionary<string, string>
                {
                    ["days"] = days.ToString(CultureInfo.InvariantCulture),
                    ["leaveType"] = type.Code,
                    ["isPaid"] = type.IsPaid ? "true" : "false",
                },
            },
            cancellationToken);

        if (started.Outcome == WorkflowStartOutcome.Pending)
        {
            request.AwaitApproval(started.InstanceId!.Value);
        }
        else
        {
            Approve(request, type);
        }
    }

    /// <summary>Approval came through: the request is approved and its days leave the balance.</summary>
    public async Task CompleteAsync(Guid requestId, bool approved, CancellationToken cancellationToken)
    {
        LeaveRequest? request = await dbContext.LeaveRequests.FirstOrDefaultAsync(row => row.Id == requestId, cancellationToken);
        if (request is null || request.Status != LeaveRequestStatus.Pending)
        {
            return;
        }

        if (approved)
        {
            LeaveType type = await dbContext.LeaveTypes.AsNoTracking().FirstAsync(row => row.Id == request.LeaveTypeId, cancellationToken);
            Approve(request, type);
        }
        else
        {
            request.Reject(Now);
        }
    }

    /// <summary>The employee (or, past the start, someone with leave.request.cancel) takes a request back.</summary>
    public async Task CancelAsync(Guid requestId, string? reason, CancellationToken cancellationToken)
    {
        LeaveRequest request = await dbContext.LeaveRequests.FirstOrDefaultAsync(row => row.Id == requestId, cancellationToken)
            ?? throw new NotFoundException(nameof(LeaveRequest), requestId);

        bool own = currentUser.EmployeeId == request.EmployeeId;
        bool administrator = await employees.IsInScopeAsync(request.EmployeeId, Permissions.Leave.RequestCancel, cancellationToken);
        if (!own && !administrator)
        {
            throw new NotFoundException(nameof(LeaveRequest), requestId);
        }

        DateOnly today = await clock.TodayAsync(cancellationToken);
        if (request.Status == LeaveRequestStatus.Approved && request.StartDate <= today && !administrator)
        {
            throw new DomainRuleException("LEAVE_ALREADY_STARTED", "Leave that has started can only be cancelled by HR.");
        }

        LeaveRequestStatus was = request.Status;
        request.Cancel(Now, reason);

        if (was == LeaveRequestStatus.Approved)
        {
            dbContext.LeaveLedger.Add(LeaveLedgerEntry.Record(request.EmployeeId, request.LeaveTypeId, today, LedgerKind.Restored, request.Days, request.Id, note: "Leave cancelled"));
            if (request.GrantsItsDays)
            {
                // The days were granted for this request only: they go with it.
                dbContext.LeaveLedger.Add(LeaveLedgerEntry.Record(request.EmployeeId, request.LeaveTypeId, today, LedgerKind.Grant, -request.Days, request.Id, note: "Grant withdrawn with the request"));
            }

            publisher.Publish(new LeaveCancelledIntegrationEvent(request.Id, request.EmployeeId, request.StartDate, request.EndDate));
        }
        else if (was == LeaveRequestStatus.Pending)
        {
            await workflow.WithdrawAsync(SubjectType, request.Id, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- Reading -----------------------------------------------------------------------------

    public async Task<PagedResult<LeaveRequestItem>> ListAsync(LeaveRequestQuery query, bool mine, CancellationToken cancellationToken)
    {
        IQueryable<LeaveRequest> requests = dbContext.LeaveRequests.AsNoTracking();

        if (mine)
        {
            Guid me = currentUser.EmployeeId ?? Guid.Empty;
            requests = requests.Where(request => request.EmployeeId == me);
        }
        else
        {
            requests = await ScopeAsync(requests, query.EmployeeId, Permissions.Leave.RequestView, cancellationToken);
        }

        if (query.Status is { } status)
        {
            requests = requests.Where(request => request.Status == status);
        }

        if (query.From is { } from)
        {
            requests = requests.Where(request => request.EndDate >= from);
        }

        if (query.To is { } to)
        {
            requests = requests.Where(request => request.StartDate <= to);
        }

        PageRequest page = query.ToPageRequest();
        long total = await requests.LongCountAsync(cancellationToken);
        List<LeaveRequest> rows = await requests.OrderByDescending(request => request.StartDate).Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);
        return PagedResult.From(await ToItemsAsync(rows, cancellationToken), page, total);
    }

    /// <summary>Approved and pending leave in a window, for the team calendar.</summary>
    public async Task<IReadOnlyList<LeaveRequestItem>> CalendarAsync(DateOnly from, DateOnly to, Guid? departmentId, CancellationToken cancellationToken)
    {
        if (to < from || to.DayNumber - from.DayNumber > 92)
        {
            throw new DomainRuleException("LEAVE_CALENDAR_RANGE", "The calendar shows at most three months.");
        }

        IQueryable<LeaveRequest> requests = await ScopeAsync(dbContext.LeaveRequests.AsNoTracking(), null, Permissions.Leave.RequestView, cancellationToken);
        if (departmentId is { } department)
        {
            List<Guid> inDepartment = [.. await employees.ListActiveIdsAsync([department], null, cancellationToken)];
            requests = requests.Where(request => inDepartment.Contains(request.EmployeeId));
        }

        List<LeaveRequest> rows = await requests
            .Where(request => (request.Status == LeaveRequestStatus.Approved || request.Status == LeaveRequestStatus.Pending)
                && request.StartDate <= to && request.EndDate >= from)
            .OrderBy(request => request.StartDate)
            .Take(1000)
            .ToListAsync(cancellationToken);

        return await ToItemsAsync(rows, cancellationToken);
    }

    public async Task<IReadOnlyList<LeaveBalance>> BalancesAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        EmployeeSummary person = await employees.GetAsync(employeeId, cancellationToken) ?? throw new NotFoundException("Employee", employeeId);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        LeavePlan? plan = (await plans.BookAsync([employeeId], cancellationToken)).For(person, today)?.Plan;
        DateOnly yearStart = LeaveYear.For(plan?.LeaveYearStartMonth ?? 1, today).Start;

        List<LeaveType> types = await dbContext.LeaveTypes.AsNoTracking().Where(type => type.IsActive).OrderBy(type => type.Name).ToListAsync(cancellationToken);

        var ledger = await dbContext.LeaveLedger.AsNoTracking()
            .Where(entry => entry.EmployeeId == employeeId)
            .GroupBy(entry => entry.LeaveTypeId)
            .Select(group => new
            {
                group.Key,
                Balance = group.Sum(entry => entry.Days),
                Taken = group.Where(entry => entry.Kind == LedgerKind.Taken && entry.EntryDate >= yearStart).Sum(entry => -entry.Days)
                    - group.Where(entry => entry.Kind == LedgerKind.Restored && entry.EntryDate >= yearStart).Sum(entry => entry.Days),
            })
            .ToDictionaryAsync(row => row.Key, cancellationToken);

        Dictionary<Guid, int> occurrences = await dbContext.LeaveRequests.AsNoTracking()
            .Where(request => request.EmployeeId == employeeId && (request.Status == LeaveRequestStatus.Pending || request.Status == LeaveRequestStatus.Approved))
            .GroupBy(request => request.LeaveTypeId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);

        Dictionary<Guid, decimal> pending = await dbContext.LeaveRequests.AsNoTracking()
            .Where(request => request.EmployeeId == employeeId && request.Status == LeaveRequestStatus.Pending)
            .GroupBy(request => request.LeaveTypeId)
            .Select(group => new { group.Key, Days = group.Sum(request => request.Days) })
            .ToDictionaryAsync(row => row.Key, row => row.Days, cancellationToken);

        var balances = new List<LeaveBalance>();
        foreach (LeaveType type in types)
        {
            // Only the policy's leave types the person can take, plus any other that still holds days.
            LeavePlanItem? item = plan?.ItemFor(type.Id);
            bool eligible = item is not null && (item.Genders.Count == 0 || (person.GenderCode is { } gender && item.Genders.Contains(gender, StringComparer.OrdinalIgnoreCase)));
            if (!eligible && !ledger.ContainsKey(type.Id))
            {
                continue;
            }

            decimal balance = ledger.TryGetValue(type.Id, out var row) ? row.Balance : 0;
            decimal held = pending.GetValueOrDefault(type.Id);
            if (item?.GrantPerOccurrence is { } grant)
            {
                // Granted each time it is taken: what one more occurrence would give, while any are left.
                int used = occurrences.GetValueOrDefault(type.Id);
                balance = item.MaxOccurrencesInEmployment is { } most && used >= most ? 0 : grant;
                held = 0;
            }
            balances.Add(new LeaveBalance(
                type.Id, type.Code, type.Name, type.Color, type.IsPaid, balance, held, balance - held,
                item?.AnnualEntitlement ?? 0, row?.Taken ?? 0));
        }

        return balances;
    }

    public async Task<IReadOnlyList<LedgerItem>> LedgerAsync(Guid employeeId, Guid? leaveTypeId, CancellationToken cancellationToken)
    {
        IQueryable<LeaveLedgerEntry> entries = dbContext.LeaveLedger.AsNoTracking().Where(entry => entry.EmployeeId == employeeId);
        if (leaveTypeId is { } typeId)
        {
            entries = entries.Where(entry => entry.LeaveTypeId == typeId);
        }

        return await entries
            .OrderByDescending(entry => entry.EntryDate).ThenByDescending(entry => entry.CreatedDate)
            .Take(500)
            .Select(entry => new LedgerItem(entry.Id, entry.EntryDate, entry.Kind, entry.Days, entry.Note, entry.RequestId))
            .ToListAsync(cancellationToken);
    }

    public async Task AdjustAsync(AdjustmentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Days == 0 || Math.Abs(request.Days) > 366 || decimal.Round(request.Days * 2) != request.Days * 2)
        {
            throw new DomainRuleException("LEAVE_ADJUSTMENT_DAYS", "Adjust by whole or half days, up to a year either way.");
        }

        if (string.IsNullOrWhiteSpace(request.Note))
        {
            throw new DomainRuleException("LEAVE_ADJUSTMENT_NOTE", "Say why the balance is being adjusted.");
        }

        if (!await employees.IsInScopeAsync(request.EmployeeId, Permissions.Leave.BalanceAdjust, cancellationToken))
        {
            throw new NotFoundException("Employee", request.EmployeeId);
        }

        if (!await dbContext.LeaveTypes.AnyAsync(type => type.Id == request.LeaveTypeId, cancellationToken))
        {
            throw new NotFoundException(nameof(LeaveType), request.LeaveTypeId);
        }

        dbContext.LeaveLedger.Add(LeaveLedgerEntry.Record(
            request.EmployeeId, request.LeaveTypeId, await clock.TodayAsync(cancellationToken),
            request.Days > 0 ? LedgerKind.Grant : LedgerKind.Adjustment, request.Days, note: request.Note));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ----- Internals ---------------------------------------------------------------------------

    /// <summary>
    /// Days the application takes, the balance available for it, and every rule it breaks. HR
    /// applying on someone's behalf is not held to the notice period.
    /// </summary>
    private sealed record Evaluation(decimal Days, decimal Available, List<string> Problems, bool DocumentRequired, bool GrantsItsDays);

    private async Task<Evaluation> EvaluateAsync(
        Guid employeeId,
        LeaveApplication application,
        bool onBehalf,
        Guid? excludeRequestId,
        CancellationToken cancellationToken)
    {
        var problems = new List<string>();

        if (application.EndDate < application.StartDate)
        {
            throw new DomainRuleException("LEAVE_DATES", "Leave cannot end before it starts.");
        }

        if (application.EndDate.DayNumber - application.StartDate.DayNumber > 365)
        {
            throw new DomainRuleException("LEAVE_TOO_LONG", "Apply for at most a year at a time.");
        }

        LeaveType type = await dbContext.LeaveTypes.AsNoTracking().FirstOrDefaultAsync(row => row.Id == application.LeaveTypeId && row.IsActive, cancellationToken)
            ?? throw new DomainRuleException("LEAVE_TYPE_UNKNOWN", "Choose an active leave type.");

        if (!type.AllowHalfDay && (application.StartsSecondHalf || application.EndsFirstHalf))
        {
            throw new DomainRuleException("LEAVE_NO_HALF_DAYS", $"{type.Name} is taken in whole days.");
        }

        // Rules other modules add (notice period restrictions and the like).
        foreach (ILeaveApplicationRule rule in applicationRules)
        {
            if (await rule.CheckAsync(employeeId, type.Code, application.StartDate, application.EndDate, cancellationToken) is { } problem)
            {
                problems.Add(problem);
            }
        }

        EmployeeSummary person = await employees.GetAsync(employeeId, cancellationToken) ?? throw new NotFoundException("Employee", employeeId);
        LeavePlan plan = (await plans.BookAsync([employeeId], cancellationToken)).For(person, application.StartDate)?.Plan
            ?? throw new DomainRuleException("LEAVE_NO_POLICY", $"No leave policy is assigned to {await EngagementTypeAsync(person, cancellationToken)}. Ask HR to assign one.");
        LeavePlanItem item = plan.ItemFor(type.Id)
            ?? throw new DomainRuleException("LEAVE_NOT_IN_POLICY", $"{type.Name} is not part of the leave policy '{plan.Name}'.");

        Dictionary<(Guid EmployeeId, DateOnly Day), bool> working = await calendar.WorkingDaysAsync([employeeId], application.StartDate, application.EndDate, cancellationToken);
        List<LeaveCalendarDay> days = [];
        for (DateOnly day = application.StartDate; day <= application.EndDate; day = day.AddDays(1))
        {
            days.Add(new LeaveCalendarDay(day, working.GetValueOrDefault((employeeId, day), true)));
        }

        decimal count = LeaveCalculator.CountDays(days, application.StartsSecondHalf, application.EndsFirstHalf, item.CountSandwichedDays);
        if (count <= 0)
        {
            problems.Add("Those dates fall on weekly offs or holidays; there is nothing to take.");
        }

        DateOnly today = await clock.TodayAsync(cancellationToken);
        LeaveYear leaveYear = LeaveYear.For(plan.LeaveYearStartMonth, application.StartDate);
        int occurrences = item.MaxOccurrencesInEmployment is null ? 0 : await dbContext.LeaveRequests.CountAsync(
            request => request.EmployeeId == employeeId
                && request.LeaveTypeId == type.Id
                && request.Id != excludeRequestId
                && (request.Status == LeaveRequestStatus.Pending || request.Status == LeaveRequestStatus.Approved),
            cancellationToken);
        int requestsThisYear = await dbContext.LeaveRequests.CountAsync(
            request => request.EmployeeId == employeeId
                && request.LeaveTypeId == type.Id
                && request.Id != excludeRequestId
                && (request.Status == LeaveRequestStatus.Pending || request.Status == LeaveRequestStatus.Approved)
                && request.StartDate >= leaveYear.Start
                && request.StartDate <= leaveYear.End,
            cancellationToken);

        problems.AddRange(LeaveEligibility.Check(
            type.Name,
            item.Rules,
            new LeaveApplicant(person.GenderCode, person.JoiningDate, person.ProbationEndDate, person.ConfirmationDate, person.Status == "OnNotice"),
            new LeaveAsk(application.StartDate, application.EndDate, count, today, onBehalf, requestsThisYear, occurrences)));

        bool overlaps = await dbContext.LeaveRequests.AnyAsync(
            request => request.EmployeeId == employeeId
                && request.Id != excludeRequestId
                && (request.Status == LeaveRequestStatus.Draft || request.Status == LeaveRequestStatus.Pending || request.Status == LeaveRequestStatus.Approved)
                && request.StartDate <= application.EndDate
                && request.EndDate >= application.StartDate,
            cancellationToken);
        if (overlaps)
        {
            problems.Add("These dates overlap leave already requested.");
        }

        decimal balance = await dbContext.LeaveLedger.Where(entry => entry.EmployeeId == employeeId && entry.LeaveTypeId == type.Id).SumAsync(entry => entry.Days, cancellationToken);
        decimal pending = await dbContext.LeaveRequests
            .Where(request => request.EmployeeId == employeeId && request.LeaveTypeId == type.Id && request.Status == LeaveRequestStatus.Pending && request.Id != excludeRequestId)
            .SumAsync(request => request.Days, cancellationToken);
        decimal available = balance - pending;

        PlanItemRules rules = item.Rules;
        if (rules.GrantPerOccurrence is { } grant)
        {
            // Granted for the request itself: there is no balance to run out of.
            available = grant;
        }
        else if (count > available + item.MaxNegativeBalance)
        {
            problems.Add($"Not enough {type.Name}: {available.ToString("0.#", CultureInfo.InvariantCulture)} day(s) available.");
        }

        bool documentRequired = rules.DocumentRequiredAboveDays is { } above && count > above;
        return new Evaluation(count, available, problems, documentRequired, rules.IsPerOccurrence);
    }

    private async Task<Guid> ResolveEmployeeAsync(LeaveApplication application, bool onBehalf, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (!onBehalf)
        {
            return currentUser.EmployeeId ?? throw new DomainRuleException("LEAVE_NO_EMPLOYEE", "Your account is not linked to an employee record.");
        }

        Guid employeeId = application.EmployeeId ?? throw new DomainRuleException("LEAVE_EMPLOYEE_REQUIRED", "Choose whose leave this is.");
        return await employees.IsInScopeAsync(employeeId, Permissions.Leave.RequestCreateOnBehalf, cancellationToken)
            ? employeeId
            : throw new NotFoundException("Employee", employeeId);
    }

    private void Approve(LeaveRequest request, LeaveType type)
    {
        request.Approve(Now);
        if (request.GrantsItsDays)
        {
            dbContext.LeaveLedger.Add(LeaveLedgerEntry.Record(request.EmployeeId, request.LeaveTypeId, request.StartDate, LedgerKind.Grant, request.Days, request.Id, note: $"{type.Name}: granted for this request"));
        }

        dbContext.LeaveLedger.Add(LeaveLedgerEntry.Record(request.EmployeeId, request.LeaveTypeId, request.StartDate, LedgerKind.Taken, -request.Days, request.Id, note: type.Name));
        publisher.Publish(new LeaveApprovedIntegrationEvent(request.Id, request.EmployeeId, request.StartDate, request.EndDate, request.Days, type.Code));
    }

    private async Task<IQueryable<LeaveRequest>> ScopeAsync(IQueryable<LeaveRequest> requests, Guid? employeeId, string permission, CancellationToken cancellationToken)
    {
        if (employeeId is { } single)
        {
            if (!await employees.IsInScopeAsync(single, permission, cancellationToken))
            {
                throw new NotFoundException("Employee", single);
            }

            return requests.Where(request => request.EmployeeId == single);
        }

        IReadOnlyCollection<Guid>? visible = await employees.InScopeIdsAsync(permission, cancellationToken);
        if (visible is null)
        {
            return requests;
        }

        List<Guid> ids = [.. visible];
        return requests.Where(request => ids.Contains(request.EmployeeId));
    }

    private async Task<List<LeaveRequestItem>> ToItemsAsync(List<LeaveRequest> requests, CancellationToken cancellationToken)
    {
        List<Guid> typeIds = [.. requests.Select(request => request.LeaveTypeId).Distinct()];
        Dictionary<Guid, LeaveType> types = await dbContext.LeaveTypes.AsNoTracking().Where(type => typeIds.Contains(type.Id)).ToDictionaryAsync(type => type.Id, cancellationToken);
        Dictionary<Guid, string> names = (await employees.GetManyAsync(requests.Select(request => request.EmployeeId), cancellationToken))
            .ToDictionary(person => person.Id, person => person.DisplayName);
        List<Guid> requestIds = [.. requests.Select(request => request.Id)];
        Dictionary<Guid, int> attachments = await dbContext.LeaveRequestAttachments.AsNoTracking()
            .Where(attachment => requestIds.Contains(attachment.RequestId))
            .GroupBy(attachment => attachment.RequestId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);

        return [.. requests.Select(request =>
        {
            LeaveType? type = types.GetValueOrDefault(request.LeaveTypeId);
            return new LeaveRequestItem(
                request.Id,
                request.EmployeeId,
                names.GetValueOrDefault(request.EmployeeId),
                request.LeaveTypeId,
                type?.Code ?? string.Empty,
                type?.Name ?? string.Empty,
                type?.Color ?? "#546E7A",
                request.StartDate,
                request.EndDate,
                request.StartsSecondHalf,
                request.EndsFirstHalf,
                request.Days,
                request.Reason,
                request.Status,
                request.WorkflowInstanceId,
                request.CreatedDate == default ? Now : request.CreatedDate,
                request.DecidedDate,
                request.CancellationReason,
                attachments.GetValueOrDefault(request.Id));
        })];
    }

    private async Task<string> EngagementTypeAsync(EmployeeSummary person, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(person.EmploymentTypeCode))
        {
            return "this employee's engagement type";
        }

        IReadOnlyDictionary<string, string> labels = await lookup.GetLabelsAsync(LookupLists.EmploymentType, [person.EmploymentTypeCode], cancellationToken);
        return labels.GetValueOrDefault(person.EmploymentTypeCode) ?? person.EmploymentTypeCode;
    }
}

internal sealed class ApproveLeaveOnApproval(LeaveService leave) : IIntegrationEventConsumer<WorkflowApprovedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowApprovedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == LeaveService.SubjectType)
        {
            await leave.CompleteAsync(integrationEvent.SubjectId, approved: true, cancellationToken);
        }
    }
}

internal sealed class RejectLeaveOnRejection(LeaveService leave) : IIntegrationEventConsumer<WorkflowRejectedIntegrationEvent>
{
    public async Task HandleAsync(WorkflowRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SubjectType == LeaveService.SubjectType)
        {
            await leave.CompleteAsync(integrationEvent.SubjectId, approved: false, cancellationToken);
        }
    }
}
