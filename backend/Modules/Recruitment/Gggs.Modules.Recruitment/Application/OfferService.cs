using System.Globalization;
using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Messaging;
using Gggs.BuildingBlocks.MultiTenancy;
using Gggs.BuildingBlocks.Security;
using Gggs.Data;
using Gggs.Modules.Employee.Contracts;
using Gggs.Modules.Notification.Contracts;
using Gggs.Modules.Payroll.Contracts;
using Gggs.Modules.Recruitment.Contracts;
using Gggs.Modules.Recruitment.Domain;
using Gggs.Modules.Recruitment.Infrastructure;
using Gggs.Modules.Workflow.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gggs.Modules.Recruitment.Application;

public sealed record OfferItem(
    Guid Id,
    string Number,
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateName,
    Guid RequisitionId,
    string RequisitionNumber,
    string RequisitionTitle,
    decimal AnnualCtc,
    string Currency,
    DateOnly JoiningDate,
    string Designation,
    Guid? GradeId,
    DateOnly? ExpiresOn,
    string? Notes,
    OfferStatus Status,
    Guid? WorkflowInstanceId,
    DateTime? SentDate,
    DateTime? RespondedDate,
    string? DeclineReason,
    DateTime CreatedDate,
    bool WithinBudget);

public sealed record OfferBody(Guid ApplicationId, OfferTerms Terms, bool Submit);

public sealed record RespondBody(bool Accepted, string? Reason);

/// <summary>Who the new joiner reports to and what they are hired as. Unset values come from the requisition.</summary>
public sealed record HireBody(Guid? ManagerEmployeeId, Guid? PositionId, Guid? LocationId, string? EmploymentTypeCode, string? WorkModeCode);

public sealed record HireResult(Guid EmployeeId, string EmployeeCode, bool RequisitionFilled);

/// <summary>
/// Offers (spec §12): drafted from an application, approved through the workflow, sent, and accepted or
/// declined. An accepted offer becomes an employee - the hire also records the starting salary and
/// counts against the requisition.
/// </summary>
internal sealed class OfferService(
    AppDbContext dbContext,
    RecruitmentAccess access,
    IWorkflowStarter workflow,
    IEmployeeHiring hiring,
    IPayrollSalaries salaries,
    IPayrollInputs payrollInputs,
    IEmployeeDirectory employees,
    INotifier notifier,
    IIntegrationEventPublisher publisher,
    ITenantClock clock,
    TimeProvider timeProvider)
{
    public const string SubjectType = "recruitment.offer";
    public const string ReferralBonusSource = "recruitment.referral";

    public static WorkflowSubject Subject { get; } = new(
        SubjectType,
        "Approve an offer",
        "recruitment",
        [
            new WorkflowSubjectField("annualCtc", "Annual CTC", WorkflowFieldType.Number),
            new WorkflowSubjectField("overBudget", "Above the requisition's salary range (true/false)", WorkflowFieldType.Text),
        ]);

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<OfferItem>> OffersAsync(OfferStatus? status, CancellationToken cancellationToken)
    {
        List<Guid>? visible = await access.RequisitionIdsAsync(Permissions.Recruitment.OfferView, cancellationToken);
        IQueryable<Offer> rows = dbContext.Offers.AsNoTracking();
        if (visible is not null)
        {
            rows = rows.Where(row => visible.Contains(row.RequisitionId));
        }

        if (status is { } wanted)
        {
            rows = rows.Where(row => row.Status == wanted);
        }

        return await ItemsAsync(await rows.OrderByDescending(row => row.CreatedDate).Take(500).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<OfferItem> OfferAsync(Guid id, CancellationToken cancellationToken)
    {
        Offer offer = await dbContext.Offers.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Offer), id);
        // An approver who acts on the offer's workflow task may read it without recruitment access.
        if (await access.HasAsync(Permissions.Recruitment.OfferView, cancellationToken))
        {
            await access.EnsureRequisitionAsync(offer.RequisitionId, Permissions.Recruitment.OfferView, cancellationToken);
        }

        return (await ItemsAsync([offer], cancellationToken))[0];
    }

    public async Task<OfferItem> DraftAsync(OfferBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        JobApplication application = await dbContext.JobApplications.FirstOrDefaultAsync(row => row.Id == body.ApplicationId, cancellationToken)
            ?? throw new NotFoundException(nameof(JobApplication), body.ApplicationId);
        await access.EnsureRequisitionAsync(application.RequisitionId, Permissions.Recruitment.OfferManage, cancellationToken);
        if (application.IsClosed)
        {
            throw new DomainRuleException("APPLICATION_CLOSED", $"The application is {application.Stage.ToString().ToLowerInvariant()}.");
        }

        if (await dbContext.Offers.AnyAsync(row => row.ApplicationId == application.Id && row.Status != OfferStatus.Declined && row.Status != OfferStatus.Withdrawn && row.Status != OfferStatus.Rejected, cancellationToken))
        {
            throw new DomainRuleException("OFFER_EXISTS", "The application already has a live offer; withdraw it to make another.");
        }

        Requisition requisition = await dbContext.Requisitions.AsNoTracking().FirstAsync(row => row.Id == application.RequisitionId, cancellationToken);
        requisition.EnsureAccepting();
        RecruitmentRules rules = await RulesAsync(cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);
        OfferTerms terms = body.Terms with
        {
            Currency = string.IsNullOrWhiteSpace(body.Terms.Currency) ? rules.Currency : body.Terms.Currency,
            ExpiresOn = body.Terms.ExpiresOn ?? Min(today.AddDays(rules.OfferValidDays), body.Terms.JoiningDate),
        };
        string number = await RecruitmentNumbers.NextAsync(
            dbContext.Offers.IgnoreQueryFilters([Gggs.BuildingBlocks.Persistence.QueryFilters.SoftDelete]).Select(row => row.Number), $"OFF-{today.Year}-", cancellationToken);
        var offer = Offer.Draft(number, application.Id, application.CandidateId, application.RequisitionId, terms);
        dbContext.Offers.Add(offer);
        if (application.Stage != ApplicationStage.Offer)
        {
            ForceOfferStage(application);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (body.Submit)
        {
            await SubmitAsync(offer, requisition, cancellationToken);
        }

        return await OfferAsync(offer.Id, cancellationToken);
    }

    public async Task<OfferItem> UpdateAsync(Guid id, OfferTerms terms, CancellationToken cancellationToken)
    {
        Offer offer = await ManagedOfferAsync(id, cancellationToken);
        offer.Update(terms);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await OfferAsync(id, cancellationToken);
    }

    public async Task<OfferItem> SubmitAsync(Guid id, CancellationToken cancellationToken)
    {
        Offer offer = await ManagedOfferAsync(id, cancellationToken);
        Requisition requisition = await dbContext.Requisitions.AsNoTracking().FirstAsync(row => row.Id == offer.RequisitionId, cancellationToken);
        await SubmitAsync(offer, requisition, cancellationToken);
        return await OfferAsync(id, cancellationToken);
    }

    public async Task<OfferItem> ActAsync(Guid id, string action, RespondBody? response, CancellationToken cancellationToken)
    {
        Offer offer = await ManagedOfferAsync(id, cancellationToken);
        switch (action)
        {
            case "send":
                offer.Send(Now);
                break;
            case "respond":
                ArgumentNullException.ThrowIfNull(response);
                offer.Respond(response.Accepted, response.Reason, Now, await clock.TodayAsync(cancellationToken));
                if (!response.Accepted)
                {
                    JobApplication application = await dbContext.JobApplications.FirstAsync(row => row.Id == offer.ApplicationId, cancellationToken);
                    application.Reject($"Declined the offer{(string.IsNullOrWhiteSpace(response.Reason) ? string.Empty : ": " + response.Reason.Trim())}", access.Me, Now);
                }

                break;
            case "withdraw":
                if (offer.Status == OfferStatus.PendingApproval)
                {
                    await workflow.WithdrawAsync(SubjectType, offer.Id, cancellationToken);
                }

                offer.Withdraw();
                break;
            default:
                throw new DomainRuleException("OFFER_ACTION", $"'{action}' is not an offer action.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await OfferAsync(id, cancellationToken);
    }

    public async Task DecideAsync(Guid id, bool? approved, CancellationToken cancellationToken)
    {
        Offer? offer = await dbContext.Offers.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (offer is not null)
        {
            offer.Decide(approved);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Hires the candidate of an accepted offer: creates the employee (future joining dates allowed), records the
    /// starting salary in payroll, closes the application, counts the hire against the requisition and starts
    /// the referral bonus clock. Onboarding follows from the employee-hired event.
    /// </summary>
    public async Task<HireResult> HireAsync(Guid offerId, HireBody body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        Offer offer = await ManagedOfferAsync(offerId, cancellationToken);
        if (offer.Status != OfferStatus.Accepted)
        {
            throw new DomainRuleException("OFFER_STATE", "Only an accepted offer can be turned into a hire.");
        }

        Candidate candidate = await dbContext.Candidates.FirstAsync(row => row.Id == offer.CandidateId, cancellationToken);
        if (candidate.EmployeeId is not null)
        {
            throw new DomainRuleException("CANDIDATE_HIRED", $"{candidate.FullName} has already been hired.");
        }

        if (await dbContext.BackgroundChecks.AnyAsync(row => row.CandidateId == candidate.Id && row.Status == BackgroundCheckStatus.Failed, cancellationToken))
        {
            throw new DomainRuleException("BGV_FAILED", "The candidate's background verification failed.");
        }

        Requisition requisition = await dbContext.Requisitions.FirstAsync(row => row.Id == offer.RequisitionId, cancellationToken);
        JobApplication application = await dbContext.JobApplications.FirstAsync(row => row.Id == offer.ApplicationId, cancellationToken);
        DateOnly today = await clock.TodayAsync(cancellationToken);

        (Guid employeeId, string employeeCode) = await hiring.HireAsync(
            new NewHire(
                candidate.FirstName, null, candidate.LastName ?? candidate.FirstName, candidate.Email, candidate.Mobile, offer.JoiningDate, requisition.DepartmentId,
                body.PositionId ?? requisition.PositionId, requisition.JobId, offer.GradeId, body.LocationId ?? requisition.LocationId,
                body.ManagerEmployeeId ?? requisition.HiringManagerEmployeeId, body.EmploymentTypeCode ?? requisition.EmploymentTypeCode,
                body.WorkModeCode ?? requisition.WorkModeCode, offer.Designation),
            cancellationToken);
        await salaries.ReviseAsync(new SalaryRevisionRequest(employeeId, offer.AnnualCtc, offer.JoiningDate, null, "Hire", SubjectType, offer.Id), cancellationToken);

        candidate.BecameEmployee(employeeId);
        application.Hire(access.Me, Now);
        bool filled = requisition.RecordHire(today);

        Referral? referral = await dbContext.Referrals.FirstOrDefaultAsync(row => row.CandidateId == candidate.Id, cancellationToken);
        if (referral is not null)
        {
            RecruitmentRules rules = await RulesAsync(cancellationToken);
            referral.Hired(employeeId, rules.ReferralBonus, offer.JoiningDate.AddMonths(rules.ReferralBonusAfterMonths));
        }

        if (filled)
        {
            foreach (JobPosting posting in await dbContext.JobPostings.Where(row => row.RequisitionId == requisition.Id && row.Status != PostingStatus.Closed).ToListAsync(cancellationToken))
            {
                posting.Close();
            }

            foreach (JobApplication other in await dbContext.JobApplications.Where(row => row.RequisitionId == requisition.Id && row.Id != application.Id && row.Stage < ApplicationStage.Hired).ToListAsync(cancellationToken))
            {
                other.Reject("The position has been filled.", access.Me, Now);
            }

            publisher.Publish(new RequisitionFilledIntegrationEvent(requisition.Id, requisition.ManpowerRequestId, requisition.Hired));
        }

        publisher.Publish(new CandidateHiredIntegrationEvent(candidate.Id, application.Id, requisition.Id, employeeId, offer.JoiningDate, referral?.ReferrerEmployeeId));
        await dbContext.SaveChangesAsync(cancellationToken);

        if (referral is not null)
        {
            await notifier.NotifyAsync(
                new NotificationRequest(
                    NotificationTemplates.ReferralUpdate,
                    [new NotificationRecipient(referral.ReferrerEmployeeId)],
                    new Dictionary<string, string> { ["candidateName"] = candidate.FullName, ["jobTitle"] = requisition.Title, ["status"] = "hired" },
                    "/me/referrals"),
                cancellationToken);
        }

        return new HireResult(employeeId, employeeCode, filled);
    }

    /// <summary>Pays referral bonuses that have come due, while the referred hire is still employed. Returns how many were awarded.</summary>
    public async Task<int> AwardDueReferralBonusesAsync(CancellationToken cancellationToken)
    {
        DateOnly today = await clock.TodayAsync(cancellationToken);
        List<Referral> due = await dbContext.Referrals
            .Where(row => row.Status == ReferralStatus.Hired && row.BonusAmount != null && row.BonusDueOn != null && row.BonusDueOn <= today)
            .ToListAsync(cancellationToken);
        int awarded = 0;
        foreach (Referral referral in due)
        {
            if (referral.HiredEmployeeId is not { } hire || !await employees.IsActiveAsync(hire, cancellationToken) || !await employees.IsActiveAsync(referral.ReferrerEmployeeId, cancellationToken))
            {
                continue;
            }

            await payrollInputs.AddAsync(
                new PayrollInputRequest(
                    referral.ReferrerEmployeeId, new DateOnly(today.Year, today.Month, 1), PayrollInputKind.Bonus, referral.BonusAmount!.Value,
                    "Employee referral bonus", ReferralBonusSource, referral.Id),
                cancellationToken);
            referral.BonusAwarded();
            awarded++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return awarded;
    }

    // ----- Internals ----------------------------------------------------------------------------

    internal static OfferItem Item(Offer offer, string candidateName, Requisition requisition) => new(
        offer.Id, offer.Number, offer.ApplicationId, offer.CandidateId, candidateName, requisition.Id, requisition.Number, requisition.Title, offer.AnnualCtc, offer.Currency,
        offer.JoiningDate, offer.Designation, offer.GradeId, offer.ExpiresOn, offer.Notes, offer.Status, offer.WorkflowInstanceId, offer.SentDate, offer.RespondedDate,
        offer.DeclineReason, offer.CreatedDate, requisition.SalaryMax is not { } max || offer.AnnualCtc <= max);

    private static DateOnly Min(DateOnly first, DateOnly second) => first < second ? first : second;

    /// <summary>An offer can follow from any open stage; the application moves to the offer stage with it.</summary>
    private void ForceOfferStage(JobApplication application)
    {
        if (application.Stage == ApplicationStage.Offer)
        {
            return;
        }

        application.MoveTo(ApplicationStage.Offer, access.Me, Now, "Offer drafted");
    }

    private async Task SubmitAsync(Offer offer, Requisition requisition, CancellationToken cancellationToken)
    {
        offer.Submit();
        await dbContext.SaveChangesAsync(cancellationToken);
        Candidate candidate = await dbContext.Candidates.AsNoTracking().FirstAsync(row => row.Id == offer.CandidateId, cancellationToken);
        WorkflowStartResult started = await workflow.StartAsync(
            new WorkflowStartRequest
            {
                SubjectType = SubjectType,
                SubjectId = offer.Id,
                Title = $"{offer.Number}: {candidate.FullName} as {offer.Designation}",
                RequesterEmployeeId = access.Me,
                Link = $"/recruitment/offers/{offer.Id}",
                Fields = new Dictionary<string, string>
                {
                    ["annualCtc"] = offer.AnnualCtc.ToString(CultureInfo.InvariantCulture),
                    ["overBudget"] = requisition.SalaryMax is { } max && offer.AnnualCtc > max ? "true" : "false",
                },
            },
            cancellationToken);
        if (started.Outcome == WorkflowStartOutcome.Pending)
        {
            offer.AwaitApproval(started.InstanceId!.Value);
        }
        else
        {
            offer.Decide(approved: true);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Offer> ManagedOfferAsync(Guid id, CancellationToken cancellationToken)
    {
        Offer offer = await dbContext.Offers.FirstOrDefaultAsync(row => row.Id == id, cancellationToken) ?? throw new NotFoundException(nameof(Offer), id);
        await access.EnsureRequisitionAsync(offer.RequisitionId, Permissions.Recruitment.OfferManage, cancellationToken);
        return offer;
    }

    private async Task<RecruitmentRules> RulesAsync(CancellationToken cancellationToken)
        => (await dbContext.RecruitmentSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken))?.ToRules() ?? RecruitmentRules.Default;

    private async Task<List<OfferItem>> ItemsAsync(IReadOnlyList<Offer> offers, CancellationToken cancellationToken)
    {
        List<Guid> candidateIds = [.. offers.Select(row => row.CandidateId).Distinct()];
        List<Guid> requisitionIds = [.. offers.Select(row => row.RequisitionId).Distinct()];
        Dictionary<Guid, string> candidates = await dbContext.Candidates.AsNoTracking().Where(row => candidateIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.LastName == null ? row.FirstName : row.FirstName + " " + row.LastName, cancellationToken);
        Dictionary<Guid, Requisition> requisitions = await dbContext.Requisitions.AsNoTracking().Where(row => requisitionIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);
        return [.. offers.Select(row => Item(row, candidates.GetValueOrDefault(row.CandidateId) ?? string.Empty, requisitions[row.RequisitionId]))];
    }
}
