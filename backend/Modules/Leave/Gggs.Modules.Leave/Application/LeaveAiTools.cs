using System.Globalization;
using System.Text.Json;
using Gggs.BuildingBlocks.Application;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Ai.Contracts;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Leave.Domain;
using Gggs.Modules.Leave.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Leave.Application;

/// <summary>The caller's own leave balances.</summary>
internal sealed class MyLeaveBalancesTool(LeaveService leave, ICurrentUser currentUser) : IAiTool
{
    public string Name => "my_leave_balances";

    public string Description => "This person's leave balances by type: available days, days pending approval and whether the type is paid.";

    public JsonElement InputSchema { get; } = AiJson.Schema();

    public AiToolKind Kind => AiToolKind.Read;

    public string? Permission => Permissions.Self.LeaveView;

    public async Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        if (currentUser.EmployeeId is not { } me)
        {
            return AiToolOutput.Problem("This account is not linked to an employee.");
        }

        IReadOnlyList<LeaveBalance> balances = await leave.BalancesAsync(me, cancellationToken);
        return AiToolOutput.Data(balances.Select(balance => new { balance.Code, balance.Name, balance.Available, balance.Pending, balance.IsPaid }));
    }
}

/// <summary>Who is away in a period, among the people whose leave the caller may see.</summary>
internal sealed class WhoIsOnLeaveTool(AppDbContext dbContext, IEmployeeDirectory employees, TimeProvider timeProvider) : IAiTool
{
    public string Name => "who_is_on_leave";

    public string Description => "Approved and pending leave between two dates (default: today) for the people whose leave this person may see.";

    public JsonElement InputSchema { get; } = AiJson.Schema(("from", "string", "First day, yyyy-mm-dd (default today)", false), ("to", "string", "Last day, yyyy-mm-dd (default the first day)", false));

    public AiToolKind Kind => AiToolKind.Read;

    public string? Permission => Permissions.Leave.RequestView;

    public async Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        DateOnly from = AiJson.Date(input, "from") ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        DateOnly to = AiJson.Date(input, "to") ?? from;
        if (to < from || to.DayNumber - from.DayNumber > 92)
        {
            return AiToolOutput.Problem("Give a range of up to three months.");
        }

        IReadOnlyCollection<Guid>? scope = await employees.InScopeIdsAsync(Permissions.Leave.RequestView, cancellationToken);
        IQueryable<LeaveRequest> query = dbContext.LeaveRequests.AsNoTracking()
            .Where(row => row.StartDate <= to && row.EndDate >= from && (row.Status == LeaveRequestStatus.Approved || row.Status == LeaveRequestStatus.Pending));
        if (scope is not null)
        {
            query = query.Where(row => scope.Contains(row.EmployeeId));
        }

        List<LeaveRequest> rows = await query.OrderBy(row => row.StartDate).Take(100).ToListAsync(cancellationToken);
        Dictionary<Guid, string> types = await dbContext.LeaveTypes.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken);
        Dictionary<Guid, string> names = (await employees.GetManyAsync(rows.Select(row => row.EmployeeId).Distinct(), cancellationToken)).ToDictionary(row => row.Id, row => row.DisplayName);
        return AiToolOutput.Data(rows.Select(row => new
        {
            Person = names.GetValueOrDefault(row.EmployeeId),
            Type = types.GetValueOrDefault(row.LeaveTypeId),
            row.StartDate,
            row.EndDate,
            row.Days,
            Status = row.Status.ToString(),
        }));
    }
}

/// <summary>Drafts a leave application for the person to approve; on approval it is applied exactly as from the leave page.</summary>
internal sealed class ApplyLeaveTool(AppDbContext dbContext, LeaveService leave) : IAiActionTool
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Name => "apply_leave";

    public string Description => "Drafts a leave application for this person: leave type code (from my_leave_balances), first and last day, optional half days and reason.";

    public JsonElement InputSchema { get; } = AiJson.Schema(
        ("leaveTypeCode", "string", "Leave type code, e.g. EL or SL", true),
        ("startDate", "string", "First day, yyyy-mm-dd", true),
        ("endDate", "string", "Last day, yyyy-mm-dd", true),
        ("startsSecondHalf", "boolean", "Starts after lunch on the first day", false),
        ("endsFirstHalf", "boolean", "Ends at lunch on the last day", false),
        ("reason", "string", "Reason, optional", false));

    public AiToolKind Kind => AiToolKind.Action;

    public string? Permission => Permissions.Self.LeaveApply;

    public Task<AiToolOutput> RunAsync(JsonElement input, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("apply_leave is an action; it is proposed, not run.");

    public async Task<AiProposal> ProposeAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        string code = (AiJson.Text(input, "leaveTypeCode") ?? string.Empty).Trim();
        LeaveType type = await dbContext.LeaveTypes.AsNoTracking().FirstOrDefaultAsync(row => row.Code == code && row.IsActive, cancellationToken)
            ?? throw new DomainRuleException("AI_LEAVE_TYPE", $"There is no active leave type with the code '{code}'.");
        DateOnly start = AiJson.Date(input, "startDate") ?? throw new DomainRuleException("AI_LEAVE_DATES", "Give the first day as yyyy-mm-dd.");
        DateOnly end = AiJson.Date(input, "endDate") ?? start;
        var application = new LeaveApplication(type.Id, start, end, AiJson.Flag(input, "startsSecondHalf") ?? false, AiJson.Flag(input, "endsFirstHalf") ?? false, AiJson.Text(input, "reason"));
        LeavePreview preview = await leave.PreviewAsync(application, onBehalf: false, cancellationToken);
        if (preview.Problems.Count > 0)
        {
            throw new DomainRuleException("AI_LEAVE_INVALID", string.Join(" ", preview.Problems));
        }

        string days = preview.Days.ToString("0.#", CultureInfo.InvariantCulture);
        string range = start == end ? $"on {start:d MMM yyyy}" : $"from {start:d MMM} to {end:d MMM yyyy}";
        return new AiProposal($"Apply for {days} day(s) of {type.Name} {range}{(application.Reason is null ? string.Empty : $" ({application.Reason})")}; {preview.Available.ToString("0.#", CultureInfo.InvariantCulture)} day(s) available", JsonSerializer.Serialize(application, Json));
    }

    public async Task<string> ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        LeaveApplication application = JsonSerializer.Deserialize<LeaveApplication>(payloadJson, Json) ?? throw new DomainRuleException("AI_PAYLOAD", "The proposal could not be read.");
        LeaveRequestItem request = await leave.ApplyAsync(application with { EmployeeId = null }, onBehalf: false, cancellationToken);
        return $"Leave applied: {request.Days.ToString("0.#", CultureInfo.InvariantCulture)} day(s) of {request.LeaveTypeName}, now {request.Status.ToString().ToLowerInvariant()}.";
    }
}
