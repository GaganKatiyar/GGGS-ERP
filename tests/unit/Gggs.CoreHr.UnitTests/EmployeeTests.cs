using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.CoreHr.Domain.Employees;
using Gggs.CoreHr.Domain.Employees.Events;
using Shouldly;

namespace Gggs.CoreHr.UnitTests;

/// <summary>
/// Account rules for the core employee record. <see cref="EmploymentRecord"/> and the service-level
/// cycle/reference checks have their own test classes; this is about the lifecycle Employee itself
/// enforces - status transitions and the two dates (joining, exit) nothing else can be true before.
/// </summary>
public sealed class EmployeeTests
{
    private static readonly DateOnly JoiningDate = new(2026, 1, 15);

    [Fact]
    public void Hiring_ComposesTheDisplayNameFromTheGivenParts()
    {
        Employee employee = Employee.Hire("EMP001", "Priya", "R", "Sharma", "priya@acme.example", JoiningDate);

        employee.DisplayName.ShouldBe("Priya R Sharma");
        employee.Status.ShouldBe(EmployeeStatus.PreBoarding);
        employee.DomainEvents.OfType<EmployeeHiredDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void HiringWithNoMiddleOrLastName_OmitsThemFromTheDisplayNameRatherThanLeavingGaps()
    {
        Employee employee = Employee.Hire("EMP002", "Priya", null, null, null, JoiningDate);

        employee.DisplayName.ShouldBe("Priya");
    }

    [Fact]
    public void AnInvalidWorkEmail_IsRejected()
    {
        Should.Throw<DomainRuleException>(() => Employee.Hire("EMP003", "Priya", null, null, "not-an-email", JoiningDate))
            .ErrorCode.ShouldBe("EMPLOYEE_WORK_EMAIL_INVALID");
    }

    [Fact]
    public void ConfirmingFromPreBoarding_SkipsProbationAndSucceeds()
    {
        // Not every tenant runs a probation period - hiring straight into Confirmed is a legitimate
        // policy, not a state the aggregate should refuse.
        Employee employee = Hire();

        employee.Confirm(JoiningDate.AddMonths(3));

        employee.Status.ShouldBe(EmployeeStatus.Confirmed);
        employee.DomainEvents.OfType<EmployeeConfirmedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void ConfirmingBeforeTheJoiningDate_IsRejected()
    {
        Employee employee = Hire();

        Should.Throw<DomainRuleException>(() => employee.Confirm(JoiningDate.AddDays(-1)))
            .ErrorCode.ShouldBe("EMPLOYEE_CONFIRMATION_BEFORE_JOINING");
    }

    [Fact]
    public void ConfirmingSomeoneAlreadyConfirmed_IsRejected()
    {
        Employee employee = Hire();
        employee.Confirm(JoiningDate.AddMonths(3));

        Should.Throw<DomainRuleException>(() => employee.Confirm(JoiningDate.AddMonths(4)))
            .ErrorCode.ShouldBe("EMPLOYEE_NOT_ON_PROBATION");
    }

    [Fact]
    public void ExitingBeforeTheJoiningDate_IsRejected()
    {
        Employee employee = Hire();

        Should.Throw<DomainRuleException>(() => employee.Exit(JoiningDate.AddDays(-1)))
            .ErrorCode.ShouldBe("EMPLOYEE_EXIT_BEFORE_JOINING");
    }

    [Fact]
    public void ExitingTwice_IsRejected()
    {
        Employee employee = Hire();
        employee.Exit(JoiningDate.AddYears(1));

        Should.Throw<DomainRuleException>(() => employee.Exit(JoiningDate.AddYears(1).AddDays(1)))
            .ErrorCode.ShouldBe("EMPLOYEE_ALREADY_LEFT");
    }

    [Fact]
    public void ExitingAsAbsconded_SetsADifferentStatusThanAnOrdinaryExit()
    {
        // Full and final settlement, exit interviews and rehire eligibility all treat the two very
        // differently, so the domain event and the status both have to carry which one happened.
        Employee employee = Hire();

        employee.Exit(JoiningDate.AddYears(1), wasAbsconded: true);

        employee.Status.ShouldBe(EmployeeStatus.Absconded);
        employee.DomainEvents.OfType<EmployeeExitedDomainEvent>().Single().ExitTypeCode.ShouldBe("ABSCONDED");
    }

    [Fact]
    public void RehiringSomeoneStillEmployed_IsRejected()
    {
        Employee employee = Hire();

        Should.Throw<DomainRuleException>(() => employee.Rehire(JoiningDate.AddYears(2)))
            .ErrorCode.ShouldBe("EMPLOYEE_NOT_EXITED");
    }

    [Fact]
    public void RehiringPreservesTheOriginalHireDateSeparatelyFromTheNewJoiningDate()
    {
        Employee employee = Hire();
        employee.Exit(JoiningDate.AddYears(1));

        DateOnly secondStint = JoiningDate.AddYears(2);
        employee.Rehire(secondStint);

        employee.OriginalHireDate.ShouldBe(JoiningDate);
        employee.DateOfJoining.ShouldBe(secondStint);
        employee.Status.ShouldBe(EmployeeStatus.PreBoarding);
        employee.ExitDate.ShouldBeNull();
    }

    [Fact]
    public void ASecondRehire_DoesNotOverwriteTheFirstOriginalHireDate()
    {
        // The whole point of OriginalHireDate: a third stint should still point back to the very
        // first day this person ever joined, not their most recent rehire.
        Employee employee = Hire();
        employee.Exit(JoiningDate.AddYears(1));
        employee.Rehire(JoiningDate.AddYears(2));
        employee.Exit(JoiningDate.AddYears(3));

        employee.Rehire(JoiningDate.AddYears(4));

        employee.OriginalHireDate.ShouldBe(JoiningDate);
    }

    private static Employee Hire() => Employee.Hire("EMP001", "Priya", null, "Sharma", null, JoiningDate);
}
