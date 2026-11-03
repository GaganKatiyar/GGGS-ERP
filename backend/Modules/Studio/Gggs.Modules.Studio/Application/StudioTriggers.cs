using System.Globalization;
using Gggs.BuildingBlocks.Messaging;
using Gggs.Modules.Documents.Contracts;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Learning.Contracts;
using Gggs.Modules.Leave.Contracts;
using Gggs.Modules.Organization.Contracts;
using Gggs.Modules.Payroll.Contracts;
using Gggs.Modules.Performance.Contracts;
using Gggs.Modules.Recruitment.Contracts;
using Gggs.Modules.Timesheets.Contracts;
using Gggs.Modules.Workflow.Contracts;

namespace Gggs.Modules.Studio.Application;

/// <summary>A value a trigger offers to conditions and to "{{placeholders}}", with an example for previews.</summary>
public sealed record TriggerField(string Key, string Label, string Sample);

/// <summary>A business event an automation can start from.</summary>
public sealed record TriggerDefinition(string Code, string Label, string Category, string Description, Type EventType, IReadOnlyList<TriggerField> Fields)
{
    public IReadOnlyDictionary<string, string> SampleFields => Fields.ToDictionary(entry => entry.Key, entry => entry.Sample, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The events automations can start from, and the fields each offers. Tenant lifecycle and
/// internal plumbing events are deliberately absent: an automation reacts to what happens to
/// people and their work.
/// </summary>
public static class StudioTriggers
{
    private static readonly TriggerField[] Person =
    [
        new("employeeName", "Employee name", "Priya Sharma"),
        new("employeeCode", "Employee code", "EMP00042"),
        new("employeeEmail", "Employee work email", "priya.sharma@example.com"),

        // Not a number anyone can be reached on: a test send must never text a stranger.
        new("employeeMobile", "Employee mobile", "+0000000000"),
        new("department", "Department", "Engineering"),
        new("designation", "Designation", "Senior engineer"),
        new("manager", "Manager", "Vikram Shah"),
    ];

    private static TriggerField[] With(params TriggerField[] fields) => [.. Person, .. fields];

    public static IReadOnlyList<TriggerDefinition> All { get; } =
    [
        new("employee.hired", "Employee hired", "People", "Someone joins: their record is created.", typeof(EmployeeHiredIntegrationEvent),
            With(new TriggerField("joiningDate", "Joining date", "2026-11-15"))),
        new("employee.exited", "Employee exited", "People", "Someone's employment ends.", typeof(EmployeeExitedIntegrationEvent),
            With(new TriggerField("exitDate", "Exit date", "2026-11-30"), new TriggerField("exitReason", "Exit reason code", "RESIGNATION"))),
        new("employee.assignment-changed", "Assignment changed", "People", "A transfer, promotion, new manager or confirmation.", typeof(EmployeeAssignmentChangedIntegrationEvent),
            With(new TriggerField("changeType", "Kind of change", "Promotion"), new TriggerField("effectiveFrom", "Effective from", "2026-12-01"))),
        new("organization.department-head-changed", "Department head changed", "People", "A department gets a new head.", typeof(DepartmentHeadChangedIntegrationEvent),
            [new("department", "Department", "Engineering"), new("head", "New head", "Anita Rao"), new("previousHead", "Previous head", "Vikram Shah")]),
        new("leave.approved", "Leave approved", "Time", "A leave request is approved.", typeof(LeaveApprovedIntegrationEvent),
            With(new TriggerField("leaveType", "Leave type code", "EL"), new TriggerField("startDate", "From", "2026-12-22"), new TriggerField("endDate", "To", "2026-12-26"), new TriggerField("days", "Days", "3"))),
        new("leave.cancelled", "Leave cancelled", "Time", "Approved leave is cancelled.", typeof(LeaveCancelledIntegrationEvent),
            With(new TriggerField("startDate", "From", "2026-12-22"), new TriggerField("endDate", "To", "2026-12-26"))),
        new("timesheets.approved", "Timesheet approved", "Time", "A week's timesheet is approved.", typeof(TimesheetApprovedIntegrationEvent),
            With(new TriggerField("weekStart", "Week starting", "2026-11-02"), new TriggerField("hours", "Hours", "40"))),
        new("workflow.task-assigned", "Approval waiting", "Approvals", "Someone has a new approval to decide.", typeof(WorkflowTaskAssignedIntegrationEvent),
            [new("title", "Request", "Earned leave: 3 day(s) from 22 Dec"), new("assignee", "Approver", "Vikram Shah"), new("assigneeEmail", "Approver email", "vikram.shah@example.com"), new("dueDate", "Due", "2026-11-05"), new("link", "Link", "/approvals"), new("subject", "Kind of request", "leave.request")]),
        new("workflow.approved", "Request approved", "Approvals", "An approval finishes with a yes.", typeof(WorkflowApprovedIntegrationEvent),
            [new("title", "Request", "Earned leave: 3 day(s) from 22 Dec"), new("requester", "Requested by", "Priya Sharma"), new("requesterEmail", "Requester email", "priya.sharma@example.com"), new("link", "Link", "/approvals"), new("subject", "Kind of request", "leave.request")]),
        new("workflow.rejected", "Request rejected", "Approvals", "An approver says no.", typeof(WorkflowRejectedIntegrationEvent),
            [new("title", "Request", "Earned leave: 3 day(s) from 22 Dec"), new("requester", "Requested by", "Priya Sharma"), new("requesterEmail", "Requester email", "priya.sharma@example.com"), new("comment", "Approver's comment", "Clashes with the release"), new("link", "Link", "/approvals"), new("subject", "Kind of request", "leave.request")]),
        new("payroll.run-finalized", "Payroll finalised", "Payroll", "A payroll run is finalised.", typeof(PayrollRunFinalizedIntegrationEvent),
            [new("period", "Month", "2026-10"), new("employees", "People paid", "128"), new("netPay", "Net pay", "8432150.00"), new("kind", "Kind of run", "Regular")]),
        new("payroll.run-paid", "Payroll paid", "Payroll", "A payroll run is marked paid.", typeof(PayrollRunPaidIntegrationEvent),
            [new("paidOn", "Paid on", "2026-10-31"), new("reference", "Bank reference", "UTR123456"), new("kind", "Kind of run", "Regular")]),
        new("recruitment.candidate-hired", "Candidate hired", "Hiring", "A candidate accepts and becomes an employee.", typeof(CandidateHiredIntegrationEvent),
            With(new TriggerField("joiningDate", "Joining date", "2026-11-15"))),
        new("document.signed", "Document signed", "Documents", "Someone signs a document sent for signature.", typeof(DocumentSignedIntegrationEvent),
            With(new TriggerField("signedDate", "Signed on", "2026-11-01"))),
        new("performance.review-released", "Review released", "Talent", "A performance review is shared with the employee.", typeof(ReviewReleasedIntegrationEvent),
            With(new TriggerField("rating", "Final rating", "4"))),
        new("learning.course-completed", "Course completed", "Talent", "Someone completes a course.", typeof(CourseCompletedIntegrationEvent),
            With(new TriggerField("courseTitle", "Course", "Secure coding"), new TriggerField("completedOn", "Completed on", "2026-11-01"))),
    ];

    public static TriggerDefinition? Find(string code) => All.FirstOrDefault(trigger => string.Equals(trigger.Code, code, StringComparison.OrdinalIgnoreCase));

    public static TriggerDefinition? For(Type eventType) => All.FirstOrDefault(trigger => trigger.EventType == eventType);
}

/// <summary>Turns an event into its trigger's fields, looking up names for the ids it carries.</summary>
public sealed class TriggerFieldResolver(IEmployeeDirectory employees, IEmployeeSeparations contacts, IOrgStructure organization)
{
    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    public async Task<IReadOnlyDictionary<string, string>> ResolveAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        switch (integrationEvent)
        {
            case EmployeeHiredIntegrationEvent hired:
                await PersonAsync(fields, hired.EmployeeId, cancellationToken);
                fields["joiningDate"] = Day(hired.JoiningDate);
                break;
            case EmployeeExitedIntegrationEvent exited:
                await PersonAsync(fields, exited.EmployeeId, cancellationToken);
                fields["exitDate"] = Day(exited.ExitDate);
                fields["exitReason"] = exited.ExitReasonCode;
                break;
            case EmployeeAssignmentChangedIntegrationEvent changed:
                await PersonAsync(fields, changed.EmployeeId, cancellationToken);
                fields["changeType"] = changed.ChangeType;
                fields["effectiveFrom"] = Day(changed.EffectiveFrom);
                break;
            case DepartmentHeadChangedIntegrationEvent head:
                fields["department"] = (await organization.GetNamesAsync(OrgUnitKind.Department, [head.DepartmentId], cancellationToken)).GetValueOrDefault(head.DepartmentId) ?? string.Empty;
                fields["head"] = await NameAsync(head.HeadEmployeeId, cancellationToken);
                fields["previousHead"] = await NameAsync(head.PreviousHeadEmployeeId, cancellationToken);
                break;
            case LeaveApprovedIntegrationEvent approved:
                await PersonAsync(fields, approved.EmployeeId, cancellationToken);
                fields["leaveType"] = approved.LeaveTypeCode;
                fields["startDate"] = Day(approved.StartDate);
                fields["endDate"] = Day(approved.EndDate);
                fields["days"] = Number(approved.Days);
                break;
            case LeaveCancelledIntegrationEvent cancelled:
                await PersonAsync(fields, cancelled.EmployeeId, cancellationToken);
                fields["startDate"] = Day(cancelled.StartDate);
                fields["endDate"] = Day(cancelled.EndDate);
                break;
            case TimesheetApprovedIntegrationEvent timesheet:
                await PersonAsync(fields, timesheet.EmployeeId, cancellationToken);
                fields["weekStart"] = Day(timesheet.WeekStart);
                fields["hours"] = Number(timesheet.TaskHours.Sum(task => task.Hours));
                break;
            case WorkflowTaskAssignedIntegrationEvent task:
                fields["title"] = task.Title;
                fields["subject"] = task.SubjectType;
                fields["link"] = task.Link ?? string.Empty;
                fields["dueDate"] = task.DueDate is { } due ? Day(DateOnly.FromDateTime(due)) : string.Empty;
                EmployeeSummary? assignee = await employees.GetAsync(task.AssigneeEmployeeId, cancellationToken);
                fields["assignee"] = assignee?.DisplayName ?? string.Empty;
                fields["assigneeEmail"] = assignee?.OfficialEmail ?? string.Empty;
                break;
            case WorkflowApprovedIntegrationEvent approvedFlow:
                await RequestAsync(fields, approvedFlow.Title, approvedFlow.SubjectType, approvedFlow.Link, approvedFlow.RequesterEmployeeId, cancellationToken);
                break;
            case WorkflowRejectedIntegrationEvent rejected:
                await RequestAsync(fields, rejected.Title, rejected.SubjectType, rejected.Link, rejected.RequesterEmployeeId, cancellationToken);
                fields["comment"] = rejected.Comment ?? string.Empty;
                break;
            case PayrollRunFinalizedIntegrationEvent run:
                fields["period"] = run.Period.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                fields["employees"] = run.Employees.ToString(CultureInfo.InvariantCulture);
                fields["netPay"] = run.NetPay.ToString("0.00", CultureInfo.InvariantCulture);
                fields["kind"] = run.Kind.ToString();
                break;
            case PayrollRunPaidIntegrationEvent paid:
                fields["paidOn"] = Day(paid.PaidOn);
                fields["reference"] = paid.Reference ?? string.Empty;
                fields["kind"] = paid.Kind.ToString();
                break;
            case CandidateHiredIntegrationEvent candidate:
                await PersonAsync(fields, candidate.EmployeeId, cancellationToken);
                fields["joiningDate"] = Day(candidate.JoiningDate);
                break;
            case DocumentSignedIntegrationEvent signed:
                await PersonAsync(fields, signed.SignerEmployeeId, cancellationToken);
                fields["signedDate"] = Day(DateOnly.FromDateTime(signed.SignedDate));
                break;
            case ReviewReleasedIntegrationEvent review:
                await PersonAsync(fields, review.EmployeeId, cancellationToken);
                fields["rating"] = review.FinalRating.ToString(CultureInfo.InvariantCulture);
                break;
            case CourseCompletedIntegrationEvent course:
                await PersonAsync(fields, course.EmployeeId, cancellationToken);
                fields["courseTitle"] = course.CourseTitle;
                fields["completedOn"] = Day(course.CompletedOn);
                break;
        }

        return fields;
    }

    private async Task PersonAsync(Dictionary<string, string> fields, Guid employeeId, CancellationToken cancellationToken)
    {
        EmployeeSummary? person = await employees.GetAsync(employeeId, cancellationToken);
        fields["employeeName"] = person?.DisplayName ?? string.Empty;
        fields["employeeCode"] = person?.EmployeeCode ?? string.Empty;
        fields["employeeEmail"] = person?.OfficialEmail ?? string.Empty;
        fields["employeeMobile"] = (await contacts.PersonalContactsAsync([employeeId], cancellationToken)).GetValueOrDefault(employeeId)?.MobilePhone ?? string.Empty;
        fields["designation"] = person?.Designation ?? string.Empty;
        fields["department"] = person?.DepartmentId is { } department
            ? (await organization.GetNamesAsync(OrgUnitKind.Department, [department], cancellationToken)).GetValueOrDefault(department) ?? string.Empty
            : string.Empty;
        fields["manager"] = await NameAsync(person?.ManagerEmployeeId, cancellationToken);
    }

    private async Task RequestAsync(Dictionary<string, string> fields, string title, string subject, string? link, Guid? requesterId, CancellationToken cancellationToken)
    {
        fields["title"] = title;
        fields["subject"] = subject;
        fields["link"] = link ?? string.Empty;
        EmployeeSummary? requester = requesterId is { } id ? await employees.GetAsync(id, cancellationToken) : null;
        fields["requester"] = requester?.DisplayName ?? string.Empty;
        fields["requesterEmail"] = requester?.OfficialEmail ?? string.Empty;
    }

    private async Task<string> NameAsync(Guid? employeeId, CancellationToken cancellationToken)
        => employeeId is { } id ? (await employees.GetAsync(id, cancellationToken))?.DisplayName ?? string.Empty : string.Empty;
}
