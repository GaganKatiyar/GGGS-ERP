using Gggs.BuildingBlocks.Domain.Exceptions;
using Gggs.BuildingBlocks.Domain.Primitives;
using Gggs.Modules.Employee.Domain;
using Shouldly;
using EmployeeEntity = Gggs.Modules.Employee.Domain.Employee;

namespace Gggs.Employee.UnitTests;

public sealed class EmployeeTests
{
    private static readonly DateOnly Joined = new(2024, 1, 15);
    private static readonly DateOnly Today = new(2025, 6, 1);

    private static Assignment InDepartment(Guid department, Guid? manager = null)
        => new(null, null, null, department, null, null, null, null, manager, null, "full_time", "hybrid", "Engineer");

    private static EmployeeEntity Hire() => EmployeeEntity.Hire("emp00001", "Meera", null, "Iyer", Joined, Joined.AddMonths(6), InDepartment(Guid.NewGuid()));

    [Fact]
    public void Hiring_OpensTheFirstEmploymentRecordAndCopiesItAsCurrent()
    {
        EmployeeEntity employee = Hire();

        employee.EmployeeCode.ShouldBe("EMP00001");
        employee.DisplayName.ShouldBe("Meera Iyer");
        employee.Status.ShouldBe(EmployeeStatus.Active);
        employee.EmploymentRecords.ShouldHaveSingleItem().ChangeType.ShouldBe(AssignmentChangeType.Hire);
        employee.CurrentRecord!.EffectiveFrom.ShouldBe(Joined);
        employee.EmploymentTypeCode.ShouldBe("FULL_TIME");
    }

    [Fact]
    public void AChange_ClosesTheCurrentRecordTheDayBefore()
    {
        EmployeeEntity employee = Hire();
        Guid newDepartment = Guid.NewGuid();

        employee.ChangeAssignment(AssignmentChangeType.Transfer, new DateOnly(2025, 3, 1), InDepartment(newDepartment), "Restructure", Today);

        employee.EmploymentRecords.Count.ShouldBe(2);
        employee.EmploymentRecords[0].EffectiveTo.ShouldBe(new DateOnly(2025, 2, 28));
        employee.CurrentRecord!.DepartmentId.ShouldBe(newDepartment);
        employee.DepartmentId.ShouldBe(newDepartment);
    }

    [Fact]
    public void AChange_CannotPrecedeTheCurrentRecord_OrBeInTheFuture()
    {
        EmployeeEntity employee = Hire();

        Should.Throw<DomainRuleException>(() => employee.ChangeAssignment(AssignmentChangeType.Transfer, Joined, InDepartment(Guid.NewGuid()), null, Today))
            .ErrorCode.ShouldBe("EMPLOYEE_CHANGE_BEFORE_CURRENT");
        Should.Throw<DomainRuleException>(() => employee.ChangeAssignment(AssignmentChangeType.Transfer, Today.AddDays(1), InDepartment(Guid.NewGuid()), null, Today))
            .ErrorCode.ShouldBe("EMPLOYEE_CHANGE_IN_FUTURE");
    }

    [Fact]
    public void AnEmployee_CannotManageThemselves()
    {
        EmployeeEntity employee = Hire();

        Should.Throw<DomainRuleException>(() => employee.ChangeAssignment(
                AssignmentChangeType.ManagerChange, Today, InDepartment(employee.DepartmentId!.Value, employee.Id), null, Today))
            .ErrorCode.ShouldBe("EMPLOYEE_OWN_MANAGER");
    }

    [Fact]
    public void Exiting_ClosesTheRecordOnTheExitDate_AndFreezesTheEmployee()
    {
        EmployeeEntity employee = Hire();

        employee.Exit(new DateOnly(2025, 5, 31), "resignation");

        employee.Status.ShouldBe(EmployeeStatus.Exited);
        employee.IsActive.ShouldBeFalse();
        employee.CurrentRecord.ShouldBeNull();
        employee.EmploymentRecords[0].EffectiveTo.ShouldBe(new DateOnly(2025, 5, 31));
        Should.Throw<DomainRuleException>(() => employee.Confirm(Today)).ErrorCode.ShouldBe("EMPLOYEE_EXITED");
    }

    [Fact]
    public void ABankAccount_NeedsAnIfsc()
    {
        EmployeeEntity employee = Hire();

        Should.Throw<DomainRuleException>(() => employee.SetBank("HDFC Bank", null, "Meera", new ProtectedValue("cipher", "XXXX6789")))
            .ErrorCode.ShouldBe("EMPLOYEE_BANK_IFSC_REQUIRED");
    }

    [Fact]
    public void NamingANewPrimaryEmergencyContact_DemotesTheOld()
    {
        EmployeeEntity employee = Hire();

        EmergencyContact first = employee.AddEmergencyContact("Ravi Iyer", "spouse", "+91 98450 00000", null, isPrimary: false);
        EmergencyContact second = employee.AddEmergencyContact("Lata Iyer", "mother", "+91 98450 11111", null, isPrimary: true);

        first.IsPrimary.ShouldBeFalse("the first contact was primary only until another was named");
        second.IsPrimary.ShouldBeTrue();
    }

    [Theory]
    [InlineData("ABCDE1234F", 4, "XXXXXX234F")]
    [InlineData("50100123456789", 4, "XXXXXXXXXX6789")]
    [InlineData("123", 4, "XX3")]
    public void TheMask_KeepsOnlyTheLastCharacters(string plain, int visible, string expected)
        => ProtectedValue.MaskOf(plain, visible).ShouldBe(expected);
}
